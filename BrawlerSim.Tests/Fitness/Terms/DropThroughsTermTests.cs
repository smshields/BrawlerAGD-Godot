using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for DropThroughsTerm: +min(reward × Σ drops, cap).</summary>
public class DropThroughsTermTests
{
    private static DropThroughsTerm Term(float reward = 0.25f, float cap = 1f) => new(reward, cap);

    [Fact]
    public void IsZeroOnAllSolidStages()
    {
        // The reduction that keeps standard-v5 == standard-v4 where no thin platform
        // was ever dropped through.
        Assert.Equal(0f, Term().Evaluate(In(P(), P())), 0.0001f);
    }

    [Fact]
    public void PaysPerDropAcrossAllPlayers()
    {
        Assert.Equal(0.5f, Term().Evaluate(In(P(drops: 2))), 0.0001f);
        Assert.Equal(0.75f, Term().Evaluate(In(P(drops: 2), P(drops: 1))), 0.0001f);
    }

    [Fact]
    public void CapsAtTiebreakerScale()
    {
        // An order of magnitude below every shaping term, by construction.
        Assert.Equal(1f, Term().Evaluate(In(P(drops: 2), P(drops: 7))), 0.0001f);
        Assert.Equal(1f, Term().Evaluate(In(P(drops: 1000))), 0.0001f);
    }

    [Fact]
    public void IsNeverNegative()
    {
        // "Add a minor boost if increase is observed, but don't punish" — designer.
        foreach (int drops in new[] { 0, 1, 4, 400 })
        {
            Assert.True(Term().Evaluate(In(P(drops: drops))) >= 0f);
        }
    }

    [Fact]
    public void RewardAndCapAreTunable()
    {
        Assert.Equal(9f, Term(reward: 1f, cap: 100f).Evaluate(In(P(drops: 2), P(drops: 7))), 0.0001f);
        Assert.Equal(0f, Term(reward: 0f).Evaluate(In(P(drops: 9))), 0.0001f);
    }
}
