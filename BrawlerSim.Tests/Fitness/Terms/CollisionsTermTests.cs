using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for CollisionsTerm: scalar × Σ hits received.</summary>
public class CollisionsTermTests
{
    private static CollisionsTerm Term(float scalar = 0.5f, bool opponentOnly = false) =>
        new(scalar, opponentOnly);

    [Fact]
    public void RewardsHitsAcrossAllPlayers()
    {
        Assert.Equal(8f, Term().Evaluate(In(P(hits: 10), P(hits: 6))), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(P(), P())), 0.0001f);
    }

    [Fact]
    public void IgnoresDamageEntirely()
    {
        // Interaction DENSITY: a hit counts the same whatever it did.
        Assert.Equal(Term().Evaluate(In(P(hits: 4, damage: 500f))),
                     Term().Evaluate(In(P(hits: 4, damage: 1f))));
    }

    [Fact]
    public void ScalarIsTheTunedHalfPoint()
    {
        Assert.Equal(16f, Term(scalar: 1f).Evaluate(In(P(hits: 10), P(hits: 6))), 0.0001f);
        Assert.Equal(0f, Term(scalar: 0f).Evaluate(In(P(hits: 10), P(hits: 6))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyExcludesSelfHits()
    {
        // 10 hits of which 4 were its own bolt, plus 6 clean → 12 opponent hits.
        var input = In(P(hits: 10, selfHits: 4), P(hits: 6));
        Assert.Equal(8f, Term().Evaluate(input), 0.0001f);
        Assert.Equal(6f, Term(opponentOnly: true).Evaluate(input), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyFloorsAtZeroPerPlayer()
    {
        Assert.Equal(0f, Term(opponentOnly: true).Evaluate(In(P(hits: 3, selfHits: 5))), 0.0001f);
    }
}
