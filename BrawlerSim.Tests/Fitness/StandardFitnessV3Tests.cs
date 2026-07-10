using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Fitness;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>standard-v3: per-stock damage shaping (docs/features/fitness-v3.md).</summary>
public class StandardFitnessV3Tests
{
    private static readonly StandardFitnessV3 Fitness = new();

    private static MatchResult Result(
        float lengthSeconds,
        (float[] Stocks, int Hits, int Remaining) p1,
        (float[] Stocks, int Hits, int Remaining) p2) =>
        new(
            new[]
            {
                new PlayerStats(p1.Stocks.Sum(), p1.Hits, p1.Remaining, 0, p1.Stocks),
                new PlayerStats(p2.Stocks.Sum(), p2.Hits, p2.Remaining, 0, p2.Stocks),
            },
            LoserIndex: 0,
            Ticks: (int)(lengthSeconds * 60),
            LengthSeconds: lengthSeconds,
            FinalHash: 0,
            Trace: null);

    [Fact]
    public void HealthyMatchScoresByHand()
    {
        // 45 s → time 0. P1 stocks [100,50] (counted 150, no excess), P2 [400]
        // (counted 400, excess 100). damage (150+400)/10 = 55; farmPenalty −100;
        // collisions 0.5×(10+5) = 7.5 (scalar re-tuned to 0.5 on 2026-07-10);
        // damageFairness −|150−400|/10 = −25; stockFairness 3−|2−3| = 2.
        // Total = 0+55−100+7.5−25+2 = −60.5.
        MatchResult result = Result(45f, (new[] { 100f, 50f }, 10, 2), (new[] { 400f }, 5, 3));
        Assert.Equal(-60.5f, Fitness.Evaluate(result), 0.001f);
    }

    [Fact]
    public void PunishmentStartsAtThreeHundredPerStock()
    {
        // Just under the threshold: no penalty, damage fully rewarded.
        MatchResult under = Result(45f, (new[] { 299f }, 0, 3), (new[] { 299f }, 0, 3));
        // time 0 + damage 59.8 + farm 0 + hits 0 + fair 0 + stocks 3 = 62.8
        Assert.Equal(62.8f, Fitness.Evaluate(under), 0.01f);

        // 100 over: −100 penalty appears, reward keeps growing (counted 400).
        MatchResult over = Result(45f, (new[] { 400f }, 0, 3), (new[] { 400f }, 0, 3));
        // time 0 + damage 80 − farm 200 + stocks 3 = −117
        Assert.Equal(-117f, Fitness.Evaluate(over), 0.01f);
    }

    [Fact]
    public void DamageBeyondTheStockCapCountsForNothing()
    {
        // 700 and 6000 damage in a stock score IDENTICALLY: counted 600, excess 300.
        // The v2 hole — stalling longer digs an ever-deeper penalty — is closed.
        MatchResult seven = Result(60f, (new[] { 700f }, 0, 3), (new[] { 0f }, 0, 3));
        MatchResult huge = Result(60f, (new[] { 6000f }, 0, 3), (new[] { 0f }, 0, 3));
        Assert.Equal(Fitness.Evaluate(seven), Fitness.Evaluate(huge), 0.001f);

        // And the per-stock penalty saturates at −(600−300) = −300.
        var breakdown = Fitness.Breakdown(huge).ToDictionary(t => t.Name, t => t.Value);
        Assert.Equal(-300f, breakdown["farmPenalty"], 0.001f);
        Assert.Equal(60f, breakdown["damage"], 0.001f);
    }

    [Fact]
    public void PerStockShapingBeatsMatchTotals()
    {
        // Same 500 total damage: spread over two stocks (250 each — healthy kills) vs
        // farmed into one stock. The farm is punished, the spread is not.
        MatchResult spread = Result(45f, (new[] { 250f, 250f }, 20, 1), (new[] { 0f }, 0, 3));
        MatchResult farmed = Result(45f, (new[] { 500f }, 20, 2), (new[] { 0f }, 0, 3));
        Assert.True(Fitness.Evaluate(spread) > Fitness.Evaluate(farmed) + 150f);
    }

    [Fact]
    public void SimRecordsDamagePerStockConsistently()
    {
        // Integration: a real match's per-stock damages must exist for both players,
        // sum to the total, and count lives correctly (deaths + the live/final stock).
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameC")).Genome;
        MatchResult result = MatchRunner.Run(genome, new IInputSource[]
        {
            AgentConfig.Default.CreateSource(new Pcg32(20260709, 0)),
            AgentConfig.Default.CreateSource(new Pcg32(20260709, 1)),
        });

        foreach (PlayerStats player in result.Players)
        {
            Assert.NotNull(player.DamagePerStock);
            Assert.Equal(player.TotalDamageTaken, player.DamagePerStock!.Sum(), 0.01f);
            // Lives used = completed deaths + the in-progress (or fatal) life.
            Assert.Equal(3 - player.RemainingStocks + 1, player.DamagePerStock!.Count);
        }
    }

    [Fact]
    public void RegistryCreatesBothVersionsAndRejectsUnknown()
    {
        Assert.Equal("standard-v2", FitnessRegistry.Create("standard-v2", 45f, 60f).Name);
        Assert.Equal("standard-v3", FitnessRegistry.Create("standard-v3", 45f, 60f).Name);
        Assert.Equal("standard-v3", FitnessRegistry.Create(null, 45f, 60f).Name);
        Assert.Throws<ArgumentException>(() => FitnessRegistry.Create("standard-v9", 45f, 60f));
    }

    [Fact]
    public void ResumedRunsKeepTheirRecordedFitness()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"brawler-v3-test-{Guid.NewGuid():N}");
        try
        {
            var config = new EvolutionConfig
            {
                Seed = 3, PopulationSize = 6, RoundsPerIndividual = 1, FitnessName = "standard-v2",
            };
            var engine = new EvolutionEngine(config);
            engine.Step();
            RunStore.SaveCheckpoint(dir, engine, config, new List<GenerationStats>());

            (EvolutionEngine resumed, EvolutionConfig loaded, _) = RunStore.Load(dir);
            Assert.Equal("standard-v2", loaded.FitnessName);
            Assert.Equal("standard-v2", resumed.FitnessFunction.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
