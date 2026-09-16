using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for StunLockTerm: −weight × 100 × Σ (stun share − tolerance)⁺.</summary>
public class StunLockTermTests
{
    private static StunLockTerm Term(float weight = 5f) => new(weight);

    private static StunLockTerm WithTolerance(float tolerance) => new(new Dictionary<string, float>
    {
        ["weight"] = 5f, ["tolerance"] = tolerance, ["percentScale"] = 100f,
    });

    [Fact]
    public void IsFreeUpToTheTolerance()
    {
        // 150/1000 ticks = exactly the 0.15 tolerance.
        Assert.Equal(0f, Term().Evaluate(In(1000, P(stunTicks: 150))), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(1000, P(stunTicks: 10))), 0.0001f);
    }

    [Fact]
    public void PunishesOnlyTheExcessShare()
    {
        // 0.25 − 0.15 = 0.10 excess → −5 × 100 × 0.10.
        Assert.Equal(-50f, Term().Evaluate(In(1000, P(stunTicks: 250))), 0.0001f);
        // The documented reference points: 26% costs −55, 46% costs −155.
        Assert.Equal(-55f, Term().Evaluate(In(1000, P(stunTicks: 260))), 0.001f);
        Assert.Equal(-155f, Term().Evaluate(In(1000, P(stunTicks: 460))), 0.001f);
    }

    [Fact]
    public void SumsExcessPerPlayerRatherThanAveraging()
    {
        // Two players each 0.10 over → −100, not −50.
        Assert.Equal(-100f, Term().Evaluate(In(1000, P(stunTicks: 250), P(stunTicks: 250))), 0.0001f);
        // And a healthy partner does not dilute a chained one.
        Assert.Equal(-50f, Term().Evaluate(In(1000, P(stunTicks: 250), P(stunTicks: 0))), 0.0001f);
    }

    [Fact]
    public void ZeroTickMatchesScoreZeroRatherThanDividingByZero()
    {
        Assert.Equal(0f, Term().Evaluate(In(0f, 0, null, P(stunTicks: 500))), 0.0001f);
    }

    [Fact]
    public void ToleranceIsTunableAndWasPreviouslyUnreachable()
    {
        // At tolerance 0 the whole stun share is priced: 0.25 × 5 × 100.
        Assert.Equal(-125f, WithTolerance(0f).Evaluate(In(1000, P(stunTicks: 250))), 0.0001f);
        // At a tolerance above the share, nothing is.
        Assert.Equal(0f, WithTolerance(0.5f).Evaluate(In(1000, P(stunTicks: 250))), 0.0001f);
    }

    [Fact]
    public void WeightScalesThePenalty()
    {
        Assert.Equal(-100f, Term(weight: 10f).Evaluate(In(1000, P(stunTicks: 250))), 0.0001f);
        Assert.Equal(0f, Term(weight: 0f).Evaluate(In(1000, P(stunTicks: 900))), 0.0001f);
    }
}
