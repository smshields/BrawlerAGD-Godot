using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

/// <summary>
/// MapElitesEngine determinism contract (2026-09-10, docs/features/map-elites.md):
/// same config ⇒ same archive at any parallelism, resume == uninterrupted, and every
/// elite's grading match is replayable from (runSeed, candidate).
/// </summary>
public class MapElitesEngineTests
{
    /// <summary>Small shared pilot — deterministic, so every test binning agrees.</summary>
    private static readonly DescriptorBins SmallBins =
        DescriptorBins.FromPilot(GenerationConfig.Default, pilotSeed: 42, samples: 256);

    private static MapElitesConfig SmallConfig(int parallelism = 0) => new()
    {
        Seed = 99,
        BatchSize = 6,
        InitialRandomCandidates = 6, // batch 0 random, later batches breed
        RoundsPerIndividual = 1,
        Parallelism = parallelism,
        Match = BrawlerSim.Sim.MatchConfig.Default with { MaxMatchSeconds = 30f },
        Bins = SmallBins,
    };

    private static ulong Fingerprint(MapElitesArchive archive)
    {
        ulong hash = Fnv1a.OffsetBasis;
        foreach (KeyValuePair<int, ArchiveEntry> kv in archive.Cells)
        {
            string json = GameGenomeJson.Serialize(new GameRecord("g", null, kv.Value.Genome));
            hash = Fnv1a.Hash(Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{kv.Key}:{kv.Value.Fitness}:{kv.Value.Candidate}:{json}")), hash);
        }
        return hash;
    }

    [Fact]
    public void RunsAreDeterministic()
    {
        var a = new MapElitesEngine(SmallConfig());
        var b = new MapElitesEngine(SmallConfig());
        for (int batch = 0; batch < 4; batch++)
        {
            Assert.Equal(a.Step(), b.Step());
        }
        Assert.Equal(Fingerprint(a.Archive), Fingerprint(b.Archive));
    }

    [Fact]
    public void ParallelismDoesNotChangeResults()
    {
        var serial = new MapElitesEngine(SmallConfig(parallelism: 1));
        var parallel = new MapElitesEngine(SmallConfig(parallelism: 8));
        for (int batch = 0; batch < 4; batch++)
        {
            Assert.Equal(serial.Step(), parallel.Step());
        }
        Assert.Equal(Fingerprint(serial.Archive), Fingerprint(parallel.Archive));
    }

    [Fact]
    public void VariationBreedsFromElitesAfterInitialization()
    {
        var engine = new MapElitesEngine(SmallConfig());
        MapElitesBatchStats first = engine.Step();
        Assert.True(first.Filled > 0, "the random initialization batch filled no cells");
        // Later batches breed; the archive can only grow or improve.
        MapElitesBatchStats second = engine.Step();
        Assert.True(second.Filled >= first.Filled);
        Assert.Equal(12, engine.CandidatesEvaluated);
        Assert.True(second.QdScore >= first.QdScore
            || second.Filled > first.Filled, "archive neither grew nor improved");
    }

    [Fact]
    public void ResumedRunMatchesUninterruptedRun()
    {
        var straight = new MapElitesEngine(SmallConfig());
        var straightStats = new List<MapElitesBatchStats>();
        for (int batch = 0; batch < 5; batch++)
        {
            straightStats.Add(straight.Step());
        }

        string runDir = Path.Combine(Path.GetTempPath(), $"brawler-me-run-{Guid.NewGuid():N}");
        try
        {
            var first = new MapElitesEngine(SmallConfig());
            var history = new List<MapElitesBatchStats>();
            for (int batch = 0; batch < 3; batch++)
            {
                history.Add(first.Step());
            }
            MapElitesStore.SaveCheckpoint(runDir, first, SmallConfig(), history);

            (MapElitesEngine resumed, MapElitesConfig loaded, List<MapElitesBatchStats> loadedHistory) =
                MapElitesStore.Load(runDir);
            Assert.Equal(3, resumed.BatchesCompleted);
            Assert.Equal(18, resumed.CandidatesEvaluated);
            Assert.Equal(history, loadedHistory);
            for (int axis = 0; axis < Descriptors.Count; axis++)
            {
                Assert.Equal(SmallBins.Edges[axis], loaded.Bins.Edges[axis]); // frozen edges survive
            }

            var resumedStats = new List<MapElitesBatchStats>(loadedHistory);
            for (int batch = 0; batch < 2; batch++)
            {
                resumedStats.Add(resumed.Step());
            }

            Assert.Equal(straightStats, resumedStats);
            Assert.Equal(Fingerprint(straight.Archive), Fingerprint(resumed.Archive));
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void CheckpointRewritesOnlyDirtyCellsButStaysComplete()
    {
        string runDir = Path.Combine(Path.GetTempPath(), $"brawler-me-dirty-{Guid.NewGuid():N}");
        try
        {
            var engine = new MapElitesEngine(SmallConfig());
            var history = new List<MapElitesBatchStats> { engine.Step() };
            MapElitesStore.SaveCheckpoint(runDir, engine, SmallConfig(), history);
            // Every archived cell has its file after the first checkpoint...
            foreach (KeyValuePair<int, ArchiveEntry> kv in engine.Archive.Cells)
            {
                Assert.True(File.Exists(Path.Combine(runDir, MapElitesStore.ArchiveDirName,
                    MapElitesStore.CellFileName(kv.Key))), $"cell {kv.Key} missing after checkpoint");
            }
            // ...and after more batches + another checkpoint, still every cell.
            history.Add(engine.Step());
            history.Add(engine.Step());
            MapElitesStore.SaveCheckpoint(runDir, engine, SmallConfig(), history);
            foreach (KeyValuePair<int, ArchiveEntry> kv in engine.Archive.Cells)
            {
                Assert.True(File.Exists(Path.Combine(runDir, MapElitesStore.ArchiveDirName,
                    MapElitesStore.CellFileName(kv.Key))), $"cell {kv.Key} missing after second checkpoint");
            }
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void ReplayedEvaluationReproducesTheArchivedFitness()
    {
        var engine = new MapElitesEngine(SmallConfig());
        engine.Step();
        ArchiveEntry best = engine.Archive.Best!;
        (var result, var trace) = engine.ReplayEvaluation(best);
        Assert.Equal(best.Fitness, engine.FitnessFunction.Evaluate(result));
        Assert.True(trace.TickCount > 0);
    }

    [Fact]
    public void ArchivedDescriptorsMatchRecomputation()
    {
        // The stored raw 4-vector is exactly the pure function of the stored genome —
        // the guarantee that lets a COPY of the archive be re-binned later.
        var engine = new MapElitesEngine(SmallConfig());
        engine.Step();
        foreach (KeyValuePair<int, ArchiveEntry> kv in engine.Archive.Cells)
        {
            Assert.Equal(Descriptors.Compute(kv.Value.Genome), kv.Value.Descriptor);
            Assert.Equal(kv.Key, SmallBins.CellIndex(kv.Value.Descriptor));
        }
    }

    [Fact]
    public void IsMapElitesRunDistinguishesRunKinds()
    {
        string meDir = Path.Combine(Path.GetTempPath(), $"brawler-me-kind-{Guid.NewGuid():N}");
        string gaDir = Path.Combine(Path.GetTempPath(), $"brawler-ga-kind-{Guid.NewGuid():N}");
        try
        {
            var me = new MapElitesEngine(SmallConfig());
            MapElitesStore.SaveCheckpoint(meDir, me, SmallConfig(),
                new List<MapElitesBatchStats> { me.Step() });
            var ga = new EvolutionEngine(new EvolutionConfig { Seed = 1, PopulationSize = 4 });
            ga.Step();
            RunStore.SaveCheckpoint(gaDir, ga, new EvolutionConfig { Seed = 1, PopulationSize = 4 },
                new List<GenerationStats>());

            Assert.True(MapElitesStore.IsMapElitesRun(meDir));
            Assert.False(MapElitesStore.IsMapElitesRun(gaDir));
            Assert.False(MapElitesStore.IsMapElitesRun(Path.GetTempPath()));

            // Both loaders refuse the other's run dir LOUDLY (2026-09-10 review: a
            // map-elites manifest read as a GA RunManifest would otherwise resume as
            // a 0-population engine and its first checkpoint would destroy the
            // archive index).
            Assert.Throws<InvalidDataException>(() => RunStore.Load(meDir));
            Assert.Throws<InvalidDataException>(() => MapElitesStore.Load(gaDir));
        }
        finally
        {
            Directory.Delete(meDir, recursive: true);
            Directory.Delete(gaDir, recursive: true);
        }
    }

    [Fact]
    public void DegenerateConfigsAreRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            new MapElitesEngine(SmallConfig() with { RoundsPerIndividual = 0 }));
        Assert.Throws<ArgumentException>(() =>
            new MapElitesEngine(SmallConfig() with { BatchSize = 0 }));
    }

    [Fact]
    public void OutOfPilotRangeStatIsPerBatchNotCumulative()
    {
        // Sum of the per-batch stats must equal the archive's cumulative counter.
        var engine = new MapElitesEngine(SmallConfig());
        int total = 0;
        for (int batch = 0; batch < 4; batch++)
        {
            total += engine.Step().OutOfPilotRange;
        }
        Assert.Equal(engine.Archive.OutOfPilotRangeCount, total);
    }

    [Fact]
    public void FourPlayerArchivesUseFfaFitnessAndPerConfigurationBins()
    {
        GenerationConfig fourPlayer = GenerationConfig.Default with { CharacterCount = 4 };
        var config = new MapElitesConfig
        {
            Seed = 5,
            BatchSize = 4,
            InitialRandomCandidates = 4,
            Parallelism = 2,
            Generation = fourPlayer,
            Match = BrawlerSim.Sim.MatchConfig.Default with { MaxMatchSeconds = 30f },
            Bins = DescriptorBins.FromPilot(fourPlayer, pilotSeed: 42, samples: 128),
        };
        var engine = new MapElitesEngine(config);
        Assert.Equal("ffa-v2", engine.FitnessFunction.Name);
        MapElitesBatchStats stats = engine.Step();
        Assert.True(stats.Filled > 0);
        foreach (KeyValuePair<int, ArchiveEntry> kv in engine.Archive.Cells)
        {
            Assert.Equal(4, kv.Value.Genome.Characters.Count);
        }
    }
}
