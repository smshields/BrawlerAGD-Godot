using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>One platform: integer grid rect, position = bottom-left corner.</summary>
public readonly record struct PlatformGene(int X, int Y, int XSize, int YSize)
{
    /// <summary>Mirror across x = 0 (Unity Platform.xMirror parity).</summary>
    public PlatformGene MirrorX() => this with { X = -X - XSize };
}

/// <summary>
/// The stage segment of a game genome: an ordered platform list plus, since Map Size
/// (2026-07-21, docs/features/map-size.md), a stage ParamSet — map dimensions, KO
/// margin, symmetry, and spawn genes. The params-less constructor binds the legacy
/// dimensions and derived spawns, which is what keeps pre-v7 files bit-identical.
/// </summary>
public sealed class StageGenome
{
    private readonly PlatformGene[] _platforms;

    public ParamSet Params { get; }

    public StageGenome(IEnumerable<PlatformGene> platforms)
        : this(platforms, null)
    {
    }

    public StageGenome(IEnumerable<PlatformGene> platforms, ParamSet? stageParams)
    {
        _platforms = platforms.ToArray();
        if (_platforms.Length == 0)
        {
            throw new ArgumentException("A stage must have at least one platform.");
        }
        Params = stageParams ?? StageRules.LegacyParams(_platforms);
    }

    public IReadOnlyList<PlatformGene> Platforms => _platforms;

    /// <summary>
    /// Single-point crossover, Unity parity for the platform lists: point is drawn in
    /// [0, min(lenA, lenB)); the child is a's platforms before the point followed by
    /// b's platforms from the point onward (child length = b's length unless a is
    /// shorter). Since Map Size the stage params cross FIRST (standard single-point op)
    /// — draw order is part of the RNG stream contract (fingerprint golden).
    /// </summary>
    public static StageGenome SinglePointCrossover(StageGenome a, StageGenome b, Pcg32 rng)
    {
        ParamSet childParams = GenomeOps.SinglePointCrossover(a.Params, b.Params, rng);
        int point = rng.NextInt(Math.Min(a._platforms.Length, b._platforms.Length));
        var child = new List<PlatformGene>(point + b._platforms.Length - point);
        for (int i = 0; i < point; i++)
        {
            child.Add(a._platforms[i]);
        }
        for (int i = point; i < b._platforms.Length; i++)
        {
            child.Add(b._platforms[i]);
        }
        // Spawn genes recombined from two different maps may be illegal for the child
        // layout — repair here (identity for legal spawns), never at sim time.
        return new StageGenome(child, StageRules.RepairSpawns(child, childParams));
    }
}
