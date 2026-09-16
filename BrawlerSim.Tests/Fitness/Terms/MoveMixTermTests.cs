using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for MoveMixTerm: weight × Σ (moveCount × minUses / totalUses).</summary>
public class MoveMixTermTests
{
    private static MoveMixTerm Term(float weight = 5f) => new(weight);

    [Fact]
    public void PaysFullWeightForPerfectlyEvenUsage()
    {
        // 4 moves × 4 min / 16 total = 1.0 per player; two such players → +10.
        Assert.Equal(10f, Term().Evaluate(In(
            P(moves: new[] { 4, 4, 4, 4 }),
            P(moves: new[] { 4, 4, 4, 4 }))), 0.0001f);
    }

    [Fact]
    public void FallsOffWithUnevenUsage()
    {
        // 4 × 4 / 20 = 0.8 → +4.
        Assert.Equal(4f, Term().Evaluate(In(P(moves: new[] { 8, 4, 4, 4 }))), 0.0001f);
    }

    [Fact]
    public void CollapsesToZeroIfAnyMoveIsNeverUsed()
    {
        // The min is 0, so the whole player scores 0 no matter how even the rest is.
        Assert.Equal(0f, Term().Evaluate(In(P(moves: new[] { 5, 5, 0 }))), 0.0001f);
    }

    [Fact]
    public void IsInertForLegacyFixturesAndEmptyKits()
    {
        Assert.Equal(0f, Term().Evaluate(In(P(moves: null))), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(P(moves: Array.Empty<int>()))), 0.0001f);
        // A kit that was never used at all: no division by zero, just 0.
        Assert.Equal(0f, Term().Evaluate(In(P(moves: new[] { 0, 0, 0 }))), 0.0001f);
    }

    [Fact]
    public void IsNudgeScaled()
    {
        // The designer's ceiling: +10 at two players against a ±100 fitness scale.
        Assert.Equal(10f, Term().Evaluate(In(
            P(moves: new[] { 7, 7 }), P(moves: new[] { 2, 2, 2 }))), 0.0001f);
        Assert.Equal(2f, Term(weight: 1f).Evaluate(In(
            P(moves: new[] { 7, 7 }), P(moves: new[] { 2, 2, 2 }))), 0.0001f);
    }
}
