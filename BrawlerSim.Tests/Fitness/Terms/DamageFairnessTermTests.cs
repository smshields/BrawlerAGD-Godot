using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for DamageFairnessTerm: −(max − min) counted damage over the
/// scalar.</summary>
public class DamageFairnessTermTests
{
    private static DamageFairnessTerm Term(bool opponentOnly = false) => new(10f, 600f, opponentOnly);

    [Fact]
    public void IsZeroWhenDamageIsEven()
    {
        Assert.Equal(0f, Term().Evaluate(In(
            P(perStock: new[] { 150f }),
            P(perStock: new[] { 150f }))), 0.0001f);
    }

    [Fact]
    public void PunishesTheSpreadRegardlessOfPlayerOrder()
    {
        // |300 − 100| either way round — the spread is symmetric by construction.
        Assert.Equal(-20f, Term().Evaluate(In(
            P(perStock: new[] { 300f }), P(perStock: new[] { 100f }))), 0.0001f);
        Assert.Equal(-20f, Term().Evaluate(In(
            P(perStock: new[] { 100f }), P(perStock: new[] { 300f }))), 0.0001f);
    }

    [Fact]
    public void AtThreeOrMorePlayersItIsTheFullSpreadNotAPairwiseMean()
    {
        // 300 / 250 / 100 → the extremes are what matter: 200 spread.
        Assert.Equal(-20f, Term().Evaluate(In(
            P(perStock: new[] { 300f }),
            P(perStock: new[] { 250f }),
            P(perStock: new[] { 100f }))), 0.0001f);
    }

    [Fact]
    public void ReadsCountedDamageSoFarmingCannotDistortIt()
    {
        // 900 counts as 600; the spread is 600 − 100, not 900 − 100.
        Assert.Equal(-50f, Term().Evaluate(In(
            P(perStock: new[] { 900f }), P(perStock: new[] { 100f }))), 0.0001f);
    }

    [Fact]
    public void OpponentOnlyComparesOpponentInflictedDamage()
    {
        // Player 1 took 400 but 300 of it from itself → 100 opponent damage, which
        // matches player 2 exactly, so the match reads as FAIR.
        var input = In(
            P(perStock: new[] { 400f }, selfPerStock: new[] { 300f }),
            P(perStock: new[] { 100f }));
        Assert.Equal(-30f, Term().Evaluate(input), 0.0001f);
        Assert.Equal(0f, Term(opponentOnly: true).Evaluate(input), 0.0001f);
    }
}
