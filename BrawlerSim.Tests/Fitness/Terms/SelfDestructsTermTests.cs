using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for SelfDestructsTerm: −min(penalty × Σ SDs, cap).</summary>
public class SelfDestructsTermTests
{
    private static SelfDestructsTerm Term(float penalty = 1f, float cap = 4f) => new(penalty, cap);

    [Fact]
    public void IsZeroWithNoSelfDestructs()
    {
        Assert.Equal(0f, Term().Evaluate(In(P(), P())), 0.0001f);
    }

    [Fact]
    public void CostsOneEachAcrossAllPlayers()
    {
        Assert.Equal(-3f, Term().Evaluate(In(P(selfDestructs: 1), P(selfDestructs: 2))), 0.0001f);
    }

    [Fact]
    public void CapsSoOneDegenerateMatchCannotDrownEveryOtherSignal()
    {
        Assert.Equal(-4f, Term().Evaluate(In(P(selfDestructs: 3), P(selfDestructs: 9))), 0.0001f);
        Assert.Equal(-4f, Term().Evaluate(In(P(selfDestructs: 500))), 0.0001f);
    }

    [Fact]
    public void IsNeverPositive()
    {
        foreach (int count in new[] { 0, 1, 4, 40 })
        {
            Assert.True(Term().Evaluate(In(P(selfDestructs: count))) <= 0f);
        }
    }

    [Fact]
    public void PenaltyAndCapAreTunable()
    {
        Assert.Equal(-6f, Term(penalty: 2f, cap: 100f).Evaluate(In(P(selfDestructs: 3))), 0.0001f);
        Assert.Equal(-4f, Term(penalty: 2f).Evaluate(In(P(selfDestructs: 3))), 0.0001f);
        Assert.Equal(0f, Term(cap: 0f).Evaluate(In(P(selfDestructs: 9))), 0.0001f);
    }
}
