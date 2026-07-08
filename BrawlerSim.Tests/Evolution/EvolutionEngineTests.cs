using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

public class EvolutionEngineTests
{
    private static EvolutionConfig SmallConfig(int parallelism = 0) => new()
    {
        Seed = 99,
        PopulationSize = 8,
        RoundsPerIndividual = 1,
        Parallelism = parallelism,
    };

    private static ulong Fingerprint(IReadOnlyList<GameGenome> population)
    {
        ulong hash = Fnv1a.OffsetBasis;
        foreach (GameGenome genome in population)
        {
            string json = GameGenomeJson.Serialize(new GameRecord("g", null, genome));
            hash = Fnv1a.Hash(Encoding.UTF8.GetBytes(json), hash);
        }
        return hash;
    }

    [Fact]
    public void RunsAreDeterministic()
    {
        var a = new EvolutionEngine(SmallConfig());
        var b = new EvolutionEngine(SmallConfig());
        for (int gen = 0; gen < 3; gen++)
        {
            GenerationStats sa = a.Step();
            GenerationStats sb = b.Step();
            Assert.Equal(sa, sb);
        }
        Assert.Equal(Fingerprint(a.Population), Fingerprint(b.Population));
    }

    [Fact]
    public void ParallelismDoesNotChangeResults()
    {
        var serial = new EvolutionEngine(SmallConfig(parallelism: 1));
        var parallel = new EvolutionEngine(SmallConfig(parallelism: 8));
        for (int gen = 0; gen < 3; gen++)
        {
            Assert.Equal(serial.Step(), parallel.Step());
        }
        Assert.Equal(Fingerprint(serial.Population), Fingerprint(parallel.Population));
    }

    [Fact]
    public void SurvivorsCarryTheirGenomesForward()
    {
        var engine = new EvolutionEngine(SmallConfig());
        var beforeJson = engine.Population
            .Select(g => GameGenomeJson.Serialize(new GameRecord("g", null, g)))
            .ToHashSet();
        engine.Step();
        int retained = engine.Population
            .Count(g => beforeJson.Contains(GameGenomeJson.Serialize(new GameRecord("g", null, g))));
        // Dropout 0.5 on pop 8 → exactly 4 survivors keep their genomes.
        Assert.True(retained >= 4, $"only {retained} genomes survived selection");
    }

    [Fact]
    public void ResumedRunMatchesUninterruptedRun()
    {
        // Straight-through run: 5 generations.
        var straight = new EvolutionEngine(SmallConfig());
        var straightStats = new List<GenerationStats>();
        for (int gen = 0; gen < 5; gen++)
        {
            straightStats.Add(straight.Step());
        }

        // Interrupted run: 3 generations, checkpoint to disk, load, 2 more.
        string runDir = Path.Combine(Path.GetTempPath(), $"brawler-run-{Guid.NewGuid():N}");
        try
        {
            var first = new EvolutionEngine(SmallConfig());
            var history = new List<GenerationStats>();
            for (int gen = 0; gen < 3; gen++)
            {
                history.Add(first.Step());
            }
            RunStore.SaveCheckpoint(runDir, first, SmallConfig(), history);

            (EvolutionEngine resumed, _, List<GenerationStats> loadedHistory) = RunStore.Load(runDir);
            Assert.Equal(3, resumed.GenerationsCompleted);
            Assert.Equal(history, loadedHistory);

            var resumedStats = new List<GenerationStats>(loadedHistory);
            for (int gen = 0; gen < 2; gen++)
            {
                resumedStats.Add(resumed.Step());
            }

            Assert.Equal(straightStats, resumedStats);
            Assert.Equal(Fingerprint(straight.Population), Fingerprint(resumed.Population));
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void FitnessImprovesOverGenerations()
    {
        var engine = new EvolutionEngine(new EvolutionConfig { Seed = 7, PopulationSize = 16 });
        GenerationStats first = engine.Step();
        GenerationStats last = first;
        for (int gen = 1; gen < 8; gen++)
        {
            last = engine.Step();
        }
        Assert.True(last.AverageSurvivorFitness > first.AverageSurvivorFitness,
            $"survivor fitness did not improve: {first.AverageSurvivorFitness:F2} → {last.AverageSurvivorFitness:F2}");
    }

    [Fact]
    public void ReplayedEvaluationReproducesTheGradedFitness()
    {
        // The audit-trail guarantee end to end: replaying the best individual's
        // evaluation match yields exactly the fitness the engine recorded.
        var engine = new EvolutionEngine(SmallConfig());
        GenerationStats stats = engine.Step();
        (var result, var trace) = engine.ReplayEvaluation(stats.BestIndex, stats.Generation);

        Assert.Equal(stats.TopFitness, engine.FitnessFunction.Evaluate(result));
        Assert.True(trace.TickCount > 0);
    }
}
