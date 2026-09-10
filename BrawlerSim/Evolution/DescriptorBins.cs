using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerSim.Evolution;

/// <summary>
/// Frozen equal-frequency bin edges for the four descriptor axes (2026-09-10,
/// docs/features/map-elites.md §Binning). Edges come from a PILOT — N random genomes
/// generated with the run's exact GenerationConfig (mode and player count change the
/// distributions, so pilots are per-configuration) — and are then FROZEN for the life
/// of an archive: adaptive edges would silently re-bin and break elite placement.
///
/// Out-of-pilot-range values still bin (below the first edge → bin 0, above the last
/// → the top bin) but are counted by the caller via <see cref="IsOutsidePilotRange"/>;
/// a nonzero count is a signal to re-pilot the NEXT run, never to re-bin this one.
/// </summary>
public sealed class DescriptorBins
{
    public const int BinsPerAxis = 8;
    public const int DefaultPilotSamples = 10_000;
    public const int CellCount = BinsPerAxis * BinsPerAxis * BinsPerAxis * BinsPerAxis;

    /// <summary>Interior edges per axis: BinsPerAxis − 1 ascending values.</summary>
    public IReadOnlyList<IReadOnlyList<float>> Edges { get; }

    /// <summary>Observed pilot extremes per axis — the out-of-range detector.</summary>
    public IReadOnlyList<float> PilotMin { get; }
    public IReadOnlyList<float> PilotMax { get; }

    /// <summary>How the edges were made — recorded so a checkpoint is self-describing.</summary>
    public ulong PilotSeed { get; }
    public int PilotSamples { get; }

    public DescriptorBins(IReadOnlyList<IReadOnlyList<float>> edges,
        IReadOnlyList<float> pilotMin, IReadOnlyList<float> pilotMax,
        ulong pilotSeed, int pilotSamples)
    {
        if (edges.Count != Descriptors.Count)
        {
            throw new ArgumentException($"Need edges for {Descriptors.Count} axes, got {edges.Count}.");
        }
        foreach (IReadOnlyList<float> axis in edges)
        {
            if (axis.Count != BinsPerAxis - 1)
            {
                throw new ArgumentException(
                    $"Each axis needs {BinsPerAxis - 1} interior edges, got {axis.Count}.");
            }
        }
        Edges = edges;
        PilotMin = pilotMin;
        PilotMax = pilotMax;
        PilotSeed = pilotSeed;
        PilotSamples = pilotSamples;
    }

    /// <summary>The deterministic default pilot seed for a run: derived from the run
    /// seed on its own stream tag, so the pilot never touches run RNG and a legacy
    /// run's lazily-built shadow bins are reproducible from its manifest alone.</summary>
    public static ulong DefaultPilotSeed(ulong runSeed) => SeedMix.Mix(runSeed ^ 0x50494C4F54UL); // "PILOT"

    /// <summary>
    /// Runs the pilot: generates <paramref name="samples"/> genomes with the given
    /// config on a private RNG stream, computes all four descriptors, and takes
    /// per-axis octile (equal-frequency) edges. Deterministic in (config, seed, N).
    /// </summary>
    public static DescriptorBins FromPilot(GenerationConfig generation, ulong pilotSeed,
        int samples = DefaultPilotSamples)
    {
        if (samples < BinsPerAxis)
        {
            throw new ArgumentException($"Pilot needs at least {BinsPerAxis} samples, got {samples}.");
        }
        var rng = new Pcg32(pilotSeed);
        var values = new float[Descriptors.Count][];
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            values[axis] = new float[samples];
        }
        for (int i = 0; i < samples; i++)
        {
            float[] descriptor = Descriptors.Compute(GameGenome.Generate(generation, rng));
            for (int axis = 0; axis < Descriptors.Count; axis++)
            {
                values[axis][i] = descriptor[axis];
            }
        }
        var edges = new float[Descriptors.Count][];
        var min = new float[Descriptors.Count];
        var max = new float[Descriptors.Count];
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            Array.Sort(values[axis]);
            min[axis] = values[axis][0];
            max[axis] = values[axis][samples - 1];
            edges[axis] = new float[BinsPerAxis - 1];
            for (int k = 1; k < BinsPerAxis; k++)
            {
                edges[axis][k - 1] = values[axis][(int)((long)k * samples / BinsPerAxis)];
            }
        }
        return new DescriptorBins(edges, min, max, pilotSeed, samples);
    }

    /// <summary>Bin index (0..BinsPerAxis−1) of a value on one axis: the number of
    /// interior edges ≤ value. Values beyond the pilot range clamp into the end bins.</summary>
    public int BinOf(int axis, float value)
    {
        IReadOnlyList<float> edges = Edges[axis];
        int bin = 0;
        for (int k = 0; k < edges.Count && value >= edges[k]; k++)
        {
            bin++;
        }
        return bin;
    }

    /// <summary>True when a value falls outside what the pilot ever observed on that
    /// axis (possible under range overrides or schema drift) — the re-pilot signal.</summary>
    public bool IsOutsidePilotRange(float[] descriptor)
    {
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            if (descriptor[axis] < PilotMin[axis] || descriptor[axis] > PilotMax[axis])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Flat archive cell index of a raw descriptor 4-vector.</summary>
    public int CellIndex(float[] descriptor)
    {
        int cell = 0;
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            cell = cell * BinsPerAxis + BinOf(axis, descriptor[axis]);
        }
        return cell;
    }

    /// <summary>Per-axis bin coordinates of a flat cell index (inverse of CellIndex).</summary>
    public static int[] CellCoordinates(int cell)
    {
        var coords = new int[Descriptors.Count];
        for (int axis = Descriptors.Count - 1; axis >= 0; axis--)
        {
            coords[axis] = cell % BinsPerAxis;
            cell /= BinsPerAxis;
        }
        return coords;
    }

    // ── Persistence (descriptor-bins.json for shadow archives; embedded in the
    //    MAP-Elites run manifest) ──────────────────────────────────────────────────

    public sealed class Doc
    {
        public float[][]? Edges { get; set; }
        public float[]? PilotMin { get; set; }
        public float[]? PilotMax { get; set; }
        public ulong PilotSeed { get; set; }
        public int PilotSamples { get; set; }
    }

    public Doc ToDoc() => new()
    {
        Edges = Edges.Select(a => a.ToArray()).ToArray(),
        PilotMin = PilotMin.ToArray(),
        PilotMax = PilotMax.ToArray(),
        PilotSeed = PilotSeed,
        PilotSamples = PilotSamples,
    };

    public static DescriptorBins FromDoc(Doc doc) => new(
        doc.Edges ?? throw new JsonException("descriptor bins document has no edges"),
        doc.PilotMin ?? throw new JsonException("descriptor bins document has no pilotMin"),
        doc.PilotMax ?? throw new JsonException("descriptor bins document has no pilotMax"),
        doc.PilotSeed, doc.PilotSamples);

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(ToDoc(), JsonOptions.Document));

    public static DescriptorBins Load(string path) => FromDoc(
        JsonSerializer.Deserialize<Doc>(File.ReadAllText(path), JsonOptions.Document)
        ?? throw new JsonException($"Could not parse {path}."));
}
