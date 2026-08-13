using BrawlerSim.Fitness;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// standard-v4 (v3 + the self-destruct punishment) and ffa-v1 (v3 generalized to N
/// players + the same punishment) — 2026-08-12, docs/features/four-player.md.
/// Designer spec: −1 per self-destruct, capped at −4 per match.
/// </summary>
public class FourPlayerFitnessTests
{
    private static MatchResult TwoPlayerResult(int sd0 = 0, int sd1 = 0) =>
        new(
            new[]
            {
                new PlayerStats(150f, 10, 2, 0, new[] { 100f, 50f }, SelfDestructs: sd0),
                new PlayerStats(400f, 5, 3, 0, new[] { 400f }, SelfDestructs: sd1),
            },
            LoserIndex: 0,
            Ticks: 45 * 60,
            LengthSeconds: 45f,
            FinalHash: 0,
            Trace: null);

    [Fact]
    public void V4IsExactlyV3PlusTheSelfDestructPenalty()
    {
        var v3 = new StandardFitnessV3();
        var v4 = new StandardFitnessV4();

        // No self-destructs: identical scores (the healthy-match hand computation
        // from the v3 tests gives −60.5).
        Assert.Equal(v3.Evaluate(TwoPlayerResult()), v4.Evaluate(TwoPlayerResult()));
        Assert.Equal(-60.5f, v4.Evaluate(TwoPlayerResult()), 0.001f);

        // −1 each: 1 + 2 SDs → −3.
        Assert.Equal(-63.5f, v4.Evaluate(TwoPlayerResult(sd0: 1, sd1: 2)), 0.001f);
    }

    [Fact]
    public void SelfDestructPenaltyCapsAtMinusFourPerMatch()
    {
        var v4 = new StandardFitnessV4();
        float baseline = v4.Evaluate(TwoPlayerResult());
        // 3 + 9 = 12 SDs would be −12 uncapped; the cap keeps one degenerate match
        // from drowning every other signal (designer).
        Assert.Equal(baseline - 4f, v4.Evaluate(TwoPlayerResult(sd0: 3, sd1: 9)), 0.001f);
    }

    [Fact]
    public void V4BreakdownAppendsTheSelfDestructTerm()
    {
        var v4 = new StandardFitnessV4();
        var terms = v4.Breakdown(TwoPlayerResult(sd0: 2));
        (string name, float value) = (terms[^1].Name, terms[^1].Value);
        Assert.Equal("selfDestructs", name);
        Assert.Equal(-2f, value, 0.001f);
    }

    [Fact]
    public void FfaV1MatchesV4OnTwoPlayerMatches()
    {
        // Every ffa-v1 generalization is exact at N = 2 (spread == |a − b|, summed
        // terms == the fixed pair, jump saturation 40·2/2 == 40) — so on any 2P
        // result the two new fitnesses agree.
        var v4 = new StandardFitnessV4();
        var ffa = new FfaFitnessV1();
        foreach (MatchResult result in new[]
                 {
                     TwoPlayerResult(),
                     TwoPlayerResult(sd0: 1),
                     TwoPlayerResult(sd0: 3, sd1: 9),
                 })
        {
            Assert.Equal(v4.Evaluate(result), ffa.Evaluate(result));
        }
    }

    [Fact]
    public void FfaV1ScoresAFourPlayerMatchByHand()
    {
        // Four players, 45 s (time 0). Counted damage 100/200/300/400 (no stock past
        // 300 except p3's 400 → farm excess 100). Hits 4/6/8/2. Stocks 3/2/1/0.
        // Jumps 10 each. One self-destruct on p0.
        //   damage        (100+200+300+400)/10                    = 100
        //   farmPenalty   −1 × 100                                = −100
        //   collisions    0.5 × (4+6+8+2)                         = 10
        //   damageFairness −(400−100)/10                          = −30
        //   stockFairness 3 − (3−0)                               = 0
        //   moveMix/stunLock/blocks                               = 0
        //   jumps         10 × min(40, 40·4/2)/(40·4/2) = 10×40/80 = 5
        //   selfDestructs −1                                       = −1
        //   total                                                  = −16
        var result = new MatchResult(
            new[]
            {
                new PlayerStats(100f, 4, 3, 0, new[] { 100f }, Jumps: 10, SelfDestructs: 1),
                new PlayerStats(200f, 6, 2, 0, new[] { 200f }, Jumps: 10),
                new PlayerStats(300f, 8, 1, 0, new[] { 300f }, Jumps: 10),
                new PlayerStats(400f, 2, 0, 0, new[] { 400f }, Jumps: 10),
            },
            LoserIndex: 3,
            Ticks: 45 * 60,
            LengthSeconds: 45f,
            FinalHash: 0,
            Trace: null);
        Assert.Equal(-16f, new FfaFitnessV1().Evaluate(result), 0.001f);
    }

    [Fact]
    public void FfaV1BreakdownCarriesElevenNamedTerms()
    {
        var ffa = new FfaFitnessV1();
        var names = ffa.Breakdown(TwoPlayerResult()).Select(t => t.Name).ToArray();
        Assert.Equal(
            new[]
            {
                "time", "damage", "farmPenalty", "collisions", "damageFairness",
                "stockFairness", "moveMix", "stunLock", "jumps", "blocks", "selfDestructs",
            },
            names);
    }
}
