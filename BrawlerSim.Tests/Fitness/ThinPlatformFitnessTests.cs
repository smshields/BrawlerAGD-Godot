using BrawlerSim.Fitness;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// standard-v5 (v4 + the thin-platform drop-through tiebreaker) and ffa-v2 (ffa-v1 +
/// the same term) — 2026-09-01, docs/features/thin-platforms.md. Designer spec: a
/// MINOR boost when drop-throughs are observed, never a punishment, tiebreaker-sized:
/// +0.25 per drop over all players, capped at +1 per match.
/// </summary>
public class ThinPlatformFitnessTests
{
    private static MatchResult TwoPlayerResult(int drops0 = 0, int drops1 = 0, int sd0 = 0) =>
        new(
            new[]
            {
                new PlayerStats(150f, 10, 2, 0, new[] { 100f, 50f },
                    SelfDestructs: sd0, DropThroughs: drops0),
                new PlayerStats(400f, 5, 3, 0, new[] { 400f }, DropThroughs: drops1),
            },
            LoserIndex: 0,
            Ticks: 45 * 60,
            LengthSeconds: 45f,
            FinalHash: 0,
            Trace: null);

    [Fact]
    public void V5IsExactlyV4PlusTheDropThroughBonus()
    {
        var v4 = new StandardFitnessV4();
        var v5 = new StandardFitnessV5();

        // No drops: identical scores (the v4 hand computation gives −60.5).
        Assert.Equal(v4.Evaluate(TwoPlayerResult()), v5.Evaluate(TwoPlayerResult()));
        Assert.Equal(-60.5f, v5.Evaluate(TwoPlayerResult()), 0.001f);

        // +0.25 each: 1 + 2 drops → +0.75.
        Assert.Equal(-59.75f, v5.Evaluate(TwoPlayerResult(drops0: 1, drops1: 2)), 0.001f);
        // Never a punishment — the term is bounded below by 0.
        Assert.True(v5.Evaluate(TwoPlayerResult(drops0: 1)) > v5.Evaluate(TwoPlayerResult()));
    }

    [Fact]
    public void DropThroughBonusCapsAtPlusOnePerMatch()
    {
        var v5 = new StandardFitnessV5();
        float baseline = v5.Evaluate(TwoPlayerResult());
        // 30 + 20 drops would be +12.5 uncapped; the cap keeps the tiebreaker from
        // becoming a farmable objective (designer: MINOR tiebreaker only).
        Assert.Equal(baseline + 1f, v5.Evaluate(TwoPlayerResult(drops0: 30, drops1: 20)), 0.001f);
    }

    [Fact]
    public void V5BreakdownAppendsTheDropThroughTerm()
    {
        var terms = new StandardFitnessV5().Breakdown(TwoPlayerResult(drops0: 2));
        Assert.Equal("dropThroughs", terms[^1].Name);
        Assert.Equal(0.5f, terms[^1].Value, 0.001f);
        // The v4 self-destruct term is still in there, untouched.
        Assert.Contains(terms, t => t.Name == "selfDestructs");
    }

    [Fact]
    public void FfaV2MatchesV5OnTwoPlayerMatches()
    {
        // ffa-v1 ≡ v4 at N = 2, and the added term is player-count-agnostic, so
        // ffa-v2 ≡ v5 on any 2P result.
        var v5 = new StandardFitnessV5();
        var ffa = new FfaFitnessV2();
        foreach (MatchResult result in new[]
                 {
                     TwoPlayerResult(),
                     TwoPlayerResult(drops0: 2),
                     TwoPlayerResult(drops0: 30, drops1: 20, sd0: 3),
                 })
        {
            Assert.Equal(v5.Evaluate(result), ffa.Evaluate(result));
        }
    }

    [Fact]
    public void RegistryDefaultsAndConstructionCoverTheNewVersions()
    {
        // 2026-09-14: defaults graduated to the self-hit-blind scaled-time pair
        // (standard-v7 / ffa-v3, designer-directed); v5/ffa-v2 stay constructible.
        Assert.Equal("standard-v7", FitnessRegistry.DefaultName);
        Assert.Equal("standard-v7", FitnessRegistry.DefaultNameFor(2));
        Assert.Equal("ffa-v3", FitnessRegistry.DefaultNameFor(4));
        Assert.Equal("standard-v7", FitnessRegistry.Create(null, 45f, 60f).Name);
        Assert.Equal("ffa-v3", FitnessRegistry.Create(null, 45f, 60f, playerCount: 3).Name);
        // Frozen versions stay constructible (resume contract), and the 2P-only
        // guard admits both ffa versions for N-player runs.
        Assert.Equal("standard-v4", FitnessRegistry.Create("standard-v4", 45f, 60f).Name);
        Assert.Equal("ffa-v1", FitnessRegistry.Create("ffa-v1", 45f, 60f, playerCount: 4).Name);
        Assert.Throws<ArgumentException>(
            () => FitnessRegistry.Create("standard-v5", 45f, 60f, playerCount: 4));
    }
}
