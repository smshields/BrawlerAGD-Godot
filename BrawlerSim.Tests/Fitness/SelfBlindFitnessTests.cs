using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// standard-v7 / ffa-v3 (2026-09-14, designer-directed): the projectile self-hit
/// reward fix. Through v6, the damage/collisions/blocks terms read victim-side
/// totals, so a hitsSelf bolt clipping its own shooter scored exactly like enemy
/// interaction — measured on the EVOLUTION 8 favorites at 85-97% of ALL match
/// damage being self-inflicted. v7 counts opponent interaction only, and carries
/// the v6 scaled time target as the live term (graduated to default in the same
/// designer decision).
/// </summary>
public class SelfBlindFitnessTests
{
    private static PlayerStats Stats(
        float damage, int hits, int stocks, float[] perStock,
        float selfDamage = 0f, int selfHits = 0, float[]? selfPerStock = null,
        int blocked = 0, int selfBlocked = 0) =>
        new(damage, hits, stocks, 0, perStock,
            SelfDamageTaken: selfDamage, SelfHitsReceived: selfHits,
            SelfDamagePerStock: selfPerStock, BlockedHits: blocked,
            SelfBlockedHits: selfBlocked);

    private static MatchResult Result(params PlayerStats[] players) =>
        new(players, LoserIndex: 0, Ticks: 45 * 60, LengthSeconds: 45f,
            FinalHash: 0, Trace: null);

    private static float Term(IFitnessBreakdown fitness, MatchResult result, string name) =>
        fitness.Breakdown(result).First(t => t.Name == name).Value;

    // ---- Term arithmetic (hand-computed, via the public breakdown) -----------

    [Fact]
    public void OpponentCountedDamageSubtractsThePerStockSelfShare()
    {
        var v7 = new StandardFitnessV7();
        // Stock damage 300/100, of which 250/0 self → opponent 50/100 = 150 →
        // damage term 150 / 10 = 15 (the other player contributes zero).
        MatchResult mixed = Result(
            Stats(400f, 20, 2, new[] { 300f, 100f },
                selfDamage: 250f, selfHits: 15, selfPerStock: new[] { 250f, 0f }),
            Stats(0f, 0, 3, new[] { 0f }));
        Assert.Equal(15f, Term(v7, mixed, "damage"), 0.001f);
        // Collisions count opponent hits only: 20 − 15 = 5 → 5 × 0.5 = 2.5.
        Assert.Equal(2.5f, Term(v7, mixed, "collisions"), 0.001f);

        // The cap applies AFTER the subtraction: 800 with 100 self is 700 opponent
        // damage → counts 600 (term 60), and farm excess starts from the opponent
        // share: 700 capped 600, minus 300 = 300 → farmPenalty −300.
        MatchResult farmed = Result(
            Stats(800f, 40, 2, new[] { 800f },
                selfDamage: 100f, selfHits: 5, selfPerStock: new[] { 100f }),
            Stats(0f, 0, 3, new[] { 0f }));
        Assert.Equal(60f, Term(v7, farmed, "damage"), 0.001f);
        Assert.Equal(-300f, Term(v7, farmed, "farmPenalty"), 0.001f);
    }

    [Fact]
    public void OpponentCountsFallBackToTotalsWithoutTheSelfSplit()
    {
        var v7 = new StandardFitnessV7();
        // Legacy fixture shape: no per-stock lists, no self split — the frozen
        // totals flow through unchanged (150/10 = 15; 10 hits × 0.5 = 5).
        MatchResult legacy = Result(
            new PlayerStats(150f, 10, 2, 0),
            new PlayerStats(0f, 0, 3, 0));
        Assert.Equal(15f, Term(v7, legacy, "damage"), 0.001f);
        Assert.Equal(5f, Term(v7, legacy, "collisions"), 0.001f);
    }

    // ---- The regression (the designer-reported exploit) ----------------------

    /// <summary>THE bug: a genome whose interaction is entirely self-inflicted
    /// must score exactly like one with no interaction at all — under v6 it was
    /// scoring like a healthy damage trade.</summary>
    [Fact]
    public void PureSelfHitInteractionScoresLikeNoInteraction()
    {
        var v7 = new StandardFitnessV7();
        MatchResult selfFarm = Result(
            Stats(200f, 15, 3, new[] { 200f }, selfDamage: 200f, selfHits: 15,
                selfPerStock: new[] { 200f }),
            Stats(0f, 0, 3, new[] { 0f }, selfPerStock: new[] { 0f }));
        MatchResult inert = Result(
            Stats(0f, 0, 3, new[] { 0f }, selfPerStock: new[] { 0f }),
            Stats(0f, 0, 3, new[] { 0f }, selfPerStock: new[] { 0f }));
        Assert.Equal(v7.Evaluate(inert), v7.Evaluate(selfFarm), 0.001f);

        // …and v6 was paying it: +20 damage (200/10) + 7.5 collisions (15 × 0.5),
        // minus the 20 damageFairness spread it fakes = +7.5 net reward.
        var v6 = new StandardFitnessV6();
        Assert.Equal(7.5f, v6.Evaluate(selfFarm) - v6.Evaluate(inert), 0.01f);
    }

    [Fact]
    public void SelfBlocksEarnNothing()
    {
        var v7 = new StandardFitnessV7();
        MatchResult selfBlocks = Result(
            Stats(0f, 0, 3, new[] { 0f }, blocked: 4, selfBlocked: 4),
            Stats(0f, 0, 3, new[] { 0f }));
        MatchResult realBlocks = Result(
            Stats(0f, 0, 3, new[] { 0f }, blocked: 4),
            Stats(0f, 0, 3, new[] { 0f }));
        MatchResult none = Result(
            Stats(0f, 0, 3, new[] { 0f }),
            Stats(0f, 0, 3, new[] { 0f }));
        Assert.Equal(v7.Evaluate(none), v7.Evaluate(selfBlocks), 0.001f);
        Assert.Equal(8f, v7.Evaluate(realBlocks) - v7.Evaluate(none), 0.001f); // 4 × 2.0
    }

    // ---- Equivalences --------------------------------------------------------

    [Fact]
    public void V7EqualsV6WhenNothingIsSelfInflicted()
    {
        var v6 = new StandardFitnessV6();
        var v7 = new StandardFitnessV7();
        MatchResult clean = Result(
            Stats(150f, 10, 2, new[] { 100f, 50f }, blocked: 2),
            Stats(400f, 25, 3, new[] { 400f }));
        Assert.Equal(v6.Evaluate(clean), v7.Evaluate(clean), 0.001f);
        // And on a scaled stage the shared time anchor moves identically.
        MatchResult scaled = clean with
        {
            Stage = new StageMetrics(
                StageRules.LegacyVisibleHalfWidth * 2f,
                StageRules.LegacyVisibleHalfHeight * 2f),
        };
        Assert.Equal(v6.Evaluate(scaled), v7.Evaluate(scaled), 0.001f);
    }

    [Fact]
    public void FfaV3IsTheSameFormulaAsV7AtEveryCount()
    {
        var v7 = new StandardFitnessV7();
        var ffa = new FfaFitnessV3();
        MatchResult two = Result(
            Stats(150f, 10, 2, new[] { 100f, 50f }, selfDamage: 30f, selfHits: 2,
                selfPerStock: new[] { 30f, 0f }),
            Stats(400f, 25, 3, new[] { 400f }));
        Assert.Equal(v7.Evaluate(two), ffa.Evaluate(two)); // same builder — exact
        MatchResult four = Result(
            Stats(150f, 10, 2, new[] { 100f, 50f }),
            Stats(400f, 25, 3, new[] { 400f }),
            Stats(90f, 6, 1, new[] { 90f }, selfDamage: 90f, selfHits: 6,
                selfPerStock: new[] { 90f }),
            Stats(0f, 0, 3, new[] { 0f }));
        Assert.Equal(v7.Evaluate(four), ffa.Evaluate(four));
    }

    [Fact]
    public void RegistryDefaultsToTheSelfBlindPair()
    {
        Assert.Equal("standard-v7", FitnessRegistry.Create(null, 45f, 300f).Name);
        Assert.Equal("ffa-v3", FitnessRegistry.Create(null, 45f, 300f, playerCount: 4).Name);
        // standard-v7 keeps the family's 2P-only registry guard.
        Assert.Throws<ArgumentException>(
            () => FitnessRegistry.Create("standard-v7", 45f, 300f, playerCount: 3));
        // The frozen versions stay constructible for resume.
        Assert.Equal("standard-v5", FitnessRegistry.Create("standard-v5", 45f, 300f).Name);
        Assert.Equal("ffa-v2", FitnessRegistry.Create("ffa-v2", 45f, 300f, playerCount: 3).Name);
    }

    // ---- End to end against the sim ------------------------------------------

    /// <summary>The live exploit shape: a decelerating hitsSelf bolt comes back and
    /// clips its shooter. Under v6 the damage and collisions terms pay for it;
    /// under v7 both read zero.</summary>
    [Fact]
    public void SimSelfHitMatchEarnsNoInteractionTermsUnderV7()
    {
        CharacterGenome Make(string name) => new(name, 3, 0, TestGames.Character(),
            new[]
            {
                new MoveGenome(TestGames.Projectile(
                    (ProjectileParams.Velocity, 4f),
                    (ProjectileParams.DoesAccelerate, 1f),
                    (ProjectileParams.Acceleration, -8f),
                    (ProjectileParams.TimeToDecay, 3f),
                    (ProjectileParams.HitsSelf, 1f)), 0, MoveType.Projectile),
            },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[] { Make("P1"), Make("P2") },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));
        var world = new SimWorld(genome);
        world.Players[0].Position = new Vec2(-4f, -1.4f);
        world.Players[1].Position = new Vec2(7f, -1.4f);
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        world.Tick(stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        for (int t = 0; t < 300 && world.Players[0].SelfHitsReceived == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(1, world.Players[0].SelfHitsReceived);
        MatchResult result = world.BuildResult();

        var v6 = new StandardFitnessV6();
        var v7 = new StandardFitnessV7();
        Assert.True(Term(v6, result, "damage") > 0f);      // v6 pays for the self-hit
        Assert.True(Term(v6, result, "collisions") > 0f);
        Assert.Equal(0f, Term(v7, result, "damage"));      // v7 is blind to it
        Assert.Equal(0f, Term(v7, result, "collisions"));
    }
}
