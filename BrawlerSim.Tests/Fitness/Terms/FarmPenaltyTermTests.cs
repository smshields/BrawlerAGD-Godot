using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for FarmPenaltyTerm: −slope × Σ per-stock damage beyond the
/// punish threshold, each life's excess saturating at (cap − start).</summary>
public class FarmPenaltyTermTests
{
    private static FarmPenaltyTerm Term(float slope = 1f, bool opponentOnly = false) =>
        new(300f, 600f, slope, opponentOnly);

    [Fact]
    public void IsZeroBelowTheThreshold()
    {
        Assert.Equal(0f, Term().Evaluate(In(P(perStock: new[] { 250f, 299f }))), 0.0001f);
        // Exactly at the threshold is still free.
        Assert.Equal(0f, Term().Evaluate(In(P(perStock: new[] { 300f }))), 0.0001f);
    }

    [Fact]
    public void PunishesEachLifesExcessSeparately()
    {
        Assert.Equal(-100f, Term().Evaluate(In(P(perStock: new[] { 400f }))), 0.0001f);
        // 100 + 50 across two lives — a healthy life does not dilute a farmed one.
        Assert.Equal(-150f, Term().Evaluate(In(P(perStock: new[] { 400f, 350f }))), 0.0001f);
    }

    [Fact]
    public void SaturatesAtCapMinusStartPerLife()
    {
        // THE bounded-penalty rule: stalling longer cannot dig an ever-deeper hole.
        Assert.Equal(-300f, Term().Evaluate(In(P(perStock: new[] { 900f }))), 0.0001f);
        Assert.Equal(-300f, Term().Evaluate(In(P(perStock: new[] { 99999f }))), 0.0001f);
        // Two fully farmed lives cost twice, and no more.
        Assert.Equal(-600f, Term().Evaluate(In(P(perStock: new[] { 900f, 4000f }))), 0.0001f);
    }

    [Fact]
    public void SumsOverAllPlayers()
    {
        Assert.Equal(-150f, Term().Evaluate(In(
            P(perStock: new[] { 400f }),
            P(perStock: new[] { 350f }))), 0.0001f);
    }

    [Fact]
    public void SlopeScalesThePenalty()
    {
        Assert.Equal(-200f, Term(slope: 2f).Evaluate(In(P(perStock: new[] { 400f }))), 0.0001f);
        Assert.Equal(0f, Term(slope: 0f).Evaluate(In(P(perStock: new[] { 900f }))), 0.0001f);
    }

    [Fact]
    public void FallsBackToTheMatchTotalForLegacyFixtures()
    {
        Assert.Equal(-200f, Term().Evaluate(In(P(damage: 500f))), 0.0001f);
        Assert.Equal(-300f, Term().Evaluate(In(P(damage: 5000f))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyDoesNotPunishSelfInflictedDamage()
    {
        // 500 taken, 300 of it self-inflicted → 200 opponent damage, under the
        // threshold, so nothing is punished. Under the blind form it would cost −200.
        var input = In(P(perStock: new[] { 500f }, selfPerStock: new[] { 300f }));
        Assert.Equal(-200f, Term().Evaluate(input), 0.0001f);
        Assert.Equal(0f, Term(opponentOnly: true).Evaluate(input), 0.0001f);
    }
}
