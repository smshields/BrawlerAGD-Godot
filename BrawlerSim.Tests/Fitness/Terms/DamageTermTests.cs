using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for DamageTerm: Σ per-stock damage, each life clipped at the
/// cap, over the scalar.</summary>
public class DamageTermTests
{
    private static DamageTerm Term(bool opponentOnly = false) => new(600f, 10f, opponentOnly);

    [Fact]
    public void SumsPerStockDamageOverAllPlayers()
    {
        // (100 + 200) + 50 = 350, over the scalar 10.
        Assert.Equal(35f, Term().Evaluate(In(
            P(perStock: new[] { 100f, 200f }),
            P(perStock: new[] { 50f }))), 0.0001f);
    }

    [Fact]
    public void ClipsEachLifeAtTheCapIndependently()
    {
        // 700 counts as 600; the SECOND life's 100 is untouched by the first's excess.
        Assert.Equal(70f, Term().Evaluate(In(P(perStock: new[] { 700f, 100f }))), 0.0001f);
        // Two farmed lives saturate separately: 600 + 600.
        Assert.Equal(120f, Term().Evaluate(In(P(perStock: new[] { 900f, 5000f }))), 0.0001f);
    }

    [Fact]
    public void FallsBackToTheUncappedTotalForLegacyFixtures()
    {
        // No per-stock record (hand-built fixtures): the raw total is used as-is.
        Assert.Equal(21f, Term().Evaluate(In(P(damage: 210f))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlySubtractsTheSelfInflictedSplitPerStock()
    {
        // Lives of 200 and 100, of which 50 and 40 were self-inflicted → 150 + 60.
        Assert.Equal(21f, Term(opponentOnly: true).Evaluate(In(
            P(perStock: new[] { 200f, 100f }, selfPerStock: new[] { 50f, 40f }))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyFloorsAtZeroPerStock()
    {
        // A life whose recorded self damage exceeds its total cannot earn NEGATIVE
        // interaction, and cannot offset a sibling life either.
        Assert.Equal(6f, Term(opponentOnly: true).Evaluate(In(
            P(perStock: new[] { 60f, 60f }, selfPerStock: new[] { 90f, 0f }))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyUsesTheMatchTotalsForLegacyFixtures()
    {
        Assert.Equal(12f, Term(opponentOnly: true).Evaluate(In(P(damage: 200f, selfDamage: 80f))), 0.0001f);
        Assert.Equal(0f, Term(opponentOnly: true).Evaluate(In(P(damage: 120f, selfDamage: 200f))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyEqualsCountedWhenNothingWasSelfInflicted()
    {
        // The reduction that keeps standard-v7 == standard-v5 on self-hit-free matches.
        var input = In(P(perStock: new[] { 100f, 200f }), P(perStock: new[] { 50f }));
        Assert.Equal(Term().Evaluate(input), Term(opponentOnly: true).Evaluate(input));
    }

    [Fact]
    public void CapAndScalarAreTunable()
    {
        Assert.Equal(20f, new DamageTerm(100f, 10f, false).Evaluate(In(P(perStock: new[] { 700f, 100f }))), 0.0001f);
        Assert.Equal(300f, new DamageTerm(600f, 1f, false).Evaluate(In(P(perStock: new[] { 100f, 200f }))), 0.0001f);
    }
}
