using BrawlerSim.Fitness;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

public class StandardFitnessTests
{
    private static readonly StandardFitness Fitness = new();

    private static MatchResult Result(
        float lengthSeconds, float d1, float d2, int h1, int h2, int s1, int s2) =>
        new(
            new[] { new PlayerStats(d1, h1, s1, 0), new PlayerStats(d2, h2, s2, 0) },
            LoserIndex: 0,
            Ticks: (int)(lengthSeconds * 60),
            LengthSeconds: lengthSeconds,
            FinalHash: 0,
            Trace: null);

    [Fact]
    public void IdealMatchScoresByHand()
    {
        // 45 s, 100/100 damage, 10/10 hits, 1/1 stocks:
        // time 0 + damage 20 + hits 20 + damageFair 0 + stockFair 3 + no cap penalty = 43.
        Assert.Equal(43f, Fitness.Evaluate(Result(45f, 100f, 100f, 10, 10, 1, 1)), 0.001f);
    }

    [Fact]
    public void OvertimeDrawTakesTheFlatPenalty()
    {
        // time = −|45−60| − 35 = −50; everything else zero except stockFair 3 − 0 = 3...
        // stocks 3/3 → stockFair 3; cap: stocksLost 0 → penalty −0... totalDamage 0 ≥ 0 → penalty 0−0 = 0.
        Assert.Equal(-50f + 3f, Fitness.Evaluate(Result(60f, 0f, 0f, 0, 0, 3, 3)), 0.001f);
    }

    [Fact]
    public void ExcessDamageWithoutKillsIsPenalized()
    {
        // The anti-"corner grinding" cap: 600 total damage with zero stocks lost.
        // time −15, damage +60, hits +20, fair 0, stockFair 3, penalty 0−600 = −600.
        float score = Fitness.Evaluate(Result(30f, 300f, 300f, 10, 10, 3, 3));
        Assert.Equal(-15f + 60f + 20f + 0f + 3f - 600f, score, 0.001f);
    }

    [Fact]
    public void StockFairnessActuallyComparesThePlayers()
    {
        // Regression for Unity defect #1 (|s1 − s1| ≡ 0 made the term a constant 3):
        // a 3-0 stock blowout must score 3 lower than a 2-1 near-tie, all else equal.
        float blowout = Fitness.Evaluate(Result(45f, 100f, 100f, 10, 10, 3, 0));
        float close = Fitness.Evaluate(Result(45f, 100f, 100f, 10, 10, 2, 1));
        Assert.Equal(2f, close - blowout, 0.001f);
    }

    [Fact]
    public void DamageCapUsesTotalStocksLost()
    {
        // Regression for Unity defect #2 (6 − s1 + s2). One stock lost total → cap 100.
        // 150 total damage → penalty 100 − 150 = −50 under the corrected formula;
        // the buggy formula would have computed cap (6−2+3)·100 = 700 → no penalty.
        float score = Fitness.Evaluate(Result(45f, 75f, 75f, 5, 5, 2, 3));
        float noPenaltyPart = 0f + 15f + 10f + 0f + 2f;
        Assert.Equal(noPenaltyPart - 50f, score, 0.001f);
    }

    [Fact]
    public void DamageFairnessPenalizesLopsidedDamage()
    {
        float even = Fitness.Evaluate(Result(45f, 50f, 50f, 5, 5, 2, 2));
        float lopsided = Fitness.Evaluate(Result(45f, 100f, 0f, 5, 5, 2, 2));
        // Same total damage; lopsided loses |100−0|/10 = 10.
        Assert.Equal(10f, even - lopsided, 0.001f);
    }
}
