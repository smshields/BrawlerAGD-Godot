using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for BlocksTerm: reward × Σ blocked hits.</summary>
public class BlocksTermTests
{
    private static BlocksTerm Term(float reward = 2f, bool opponentOnly = false) =>
        new(reward, opponentOnly);

    [Fact]
    public void RewardsBlocksAcrossAllPlayers()
    {
        Assert.Equal(16f, Term().Evaluate(In(P(blocked: 3), P(blocked: 5))), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(P(), P())), 0.0001f);
    }

    [Fact]
    public void RewardIsTunable()
    {
        Assert.Equal(48f, Term(reward: 6f).Evaluate(In(P(blocked: 3), P(blocked: 5))), 0.0001f);
        // Zeroing it reproduces the shield-BLIND fitness the term was added to fix.
        Assert.Equal(0f, Term(reward: 0f).Evaluate(In(P(blocked: 8))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyExcludesBlocksOfYourOwnProjectile()
    {
        var input = In(P(blocked: 5, selfBlocked: 2));
        Assert.Equal(10f, Term().Evaluate(input), 0.0001f);
        Assert.Equal(6f, Term(opponentOnly: true).Evaluate(input), 0.0001f);
    }
}
