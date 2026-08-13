using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
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
    public void MoveMixRewardsEvenUsageAndIgnoresLegacyFixtures()
    {
        // evenness = moveCount x minUse / total: (5,5) -> 1; (8,2) -> 0.4; (10,0) -> 0.
        var even = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 5, 5 });
        var skewed = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 8, 2 });
        var single = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 10, 0 });
        var legacy = new PlayerStats(0f, 0, 3, 0, new[] { 0f }); // no MoveUses -> inert

        MatchResult Match(PlayerStats p) =>
            new(new[] { p, legacy }, 0, 2700, 45f, 0, null);
        float Term(PlayerStats p) =>
            Fitness.Breakdown(Match(p)).First(t => t.Name == "moveMix").Value;

        Assert.Equal(StandardFitnessV3.DefaultMoveMixWeight * 1.0f, Term(even), 0.001f);
        Assert.Equal(StandardFitnessV3.DefaultMoveMixWeight * 0.4f, Term(skewed), 0.001f);
        Assert.Equal(0f, Term(single), 0.001f);
    }

    [Fact]
    public void StunCapClampsHitstun()
    {
        // A high-damage victim would take multi-second stun; the cap clamps it.
        var genome = TestGames.FlatArena(moveOverrides: new[] { (MoveParams.HitstunDuration, 1f) });
        var world = new SimWorld(genome, MatchConfig.Default with { MaxStunSeconds = 0.5f });
        world.Players[0].Position = new Vec2(-1f, -1.4f);
        world.Players[1].Position = new Vec2(0.2f, -1.4f);
        for (int i = 0; i < 120 && !world.Players[0].IsGrounded; i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        world.Players[1].Damage = 200f; // uncapped stun would be 1x(200+dmg)x0.2 > 40 s

        // P0 swings (warmup 12 + execute), P1 gets hit.
        Span<InputFrame> attack = stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral };
        world.Tick(attack);
        for (int i = 0; i < 30 && world.Players[1].State != PlayerState.Stun; i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(PlayerState.Stun, world.Players[1].State);
        Assert.InRange(world.Players[1].PhaseTicksLeft, 1, 30); // 0.5 s cap = 30 ticks
    }

    [Fact]
    public void StunLockPenaltyPricesChainsAboveTolerance()
    {
        // 3600-tick match; tolerance 15%. 26% stunned -> -5x100x0.11 = -55;
        // 46% -> -155; 10% -> 0.
        float Term(int stunTicks)
        {
            var p = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 1, 1 }, stunTicks);
            var other = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 1, 1 });
            var match = new MatchResult(new[] { p, other }, 0, 3600, 60f, 0, null);
            return Fitness.Breakdown(match).First(t => t.Name == "stunLock").Value;
        }
        Assert.Equal(0f, Term((int)(0.10f * 3600)), 0.01f);
        Assert.Equal(-55f, Term((int)(0.26f * 3600)), 0.5f);
        Assert.Equal(-155f, Term((int)(0.46f * 3600)), 0.5f);
    }

    [Fact]
    public void JumpRewardSaturates()
    {
        float Term(int j0, int j1)
        {
            var a = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 1, 1 }, 0, j0);
            var b = new PlayerStats(0f, 0, 3, 0, new[] { 0f }, new[] { 1, 1 }, 0, j1);
            var match = new MatchResult(new[] { a, b }, 0, 3600, 60f, 0, null);
            return Fitness.Breakdown(match).First(t => t.Name == "jumps").Value;
        }
        Assert.Equal(0f, Term(0, 0), 0.001f);
        Assert.Equal(5f, Term(10, 10), 0.001f);   // 20/40 x 10
        Assert.Equal(10f, Term(20, 20), 0.001f);  // saturated
        Assert.Equal(10f, Term(200, 200), 0.001f); // spam earns nothing extra
    }

    [Fact]
    public void JumpsAreCounted()
    {
        var world = new SimWorld(TestGames.FlatArena());
        world.Players[0].Position = new Vec2(-4f, -1.4f);
        world.Players[1].Position = new Vec2(6f, -1.4f);
        for (int i = 0; i < 120 && !world.Players[0].IsGrounded; i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        // Ground jump, then air jump two ticks later.
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral });
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral });
        Assert.Equal(2, world.Players[0].Jumps);
    }

    [Fact]
    public void RegistryCreatesEveryVersionAndRejectsUnknown()
    {
        Assert.Equal("standard-v2", FitnessRegistry.Create("standard-v2", 45f, 60f).Name);
        Assert.Equal("standard-v3", FitnessRegistry.Create("standard-v3", 45f, 60f).Name);
        Assert.Equal("standard-v4", FitnessRegistry.Create("standard-v4", 45f, 60f).Name);
        Assert.Equal("ffa-v1", FitnessRegistry.Create("ffa-v1", 45f, 60f).Name);
        // Auto default (2026-08-12): v4 for two-player runs, ffa-v1 for 3/4.
        Assert.Equal("standard-v4", FitnessRegistry.Create(null, 45f, 60f).Name);
        Assert.Equal("ffa-v1", FitnessRegistry.Create(null, 45f, 60f, playerCount: 4).Name);
        Assert.Throws<ArgumentException>(() => FitnessRegistry.Create("standard-v9", 45f, 60f));
        // 2P-only versions refuse N-player runs (their terms read exactly two players).
        Assert.Throws<ArgumentException>(() => FitnessRegistry.Create("standard-v3", 45f, 60f, playerCount: 3));
        Assert.Throws<ArgumentException>(() => FitnessRegistry.Create("standard-v4", 45f, 60f, playerCount: 4));
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
