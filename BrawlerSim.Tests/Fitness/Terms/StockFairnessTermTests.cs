using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for StockFairnessTerm: baseline − remaining-stock spread.</summary>
public class StockFairnessTermTests
{
    private static StockFairnessTerm Term() => new();

    [Fact]
    public void PaysTheFullBaselineForADeadEvenFinish()
    {
        Assert.Equal(3f, Term().Evaluate(In(P(stocks: 2), P(stocks: 2))), 0.0001f);
        Assert.Equal(3f, Term().Evaluate(In(P(stocks: 0), P(stocks: 0))), 0.0001f);
    }

    [Fact]
    public void FallsToZeroOnASweep()
    {
        Assert.Equal(1f, Term().Evaluate(In(P(stocks: 3), P(stocks: 1))), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(P(stocks: 3), P(stocks: 0))), 0.0001f);
    }

    [Fact]
    public void CanGoNegativeWhenTheSpreadExceedsTheBaseline()
    {
        // Nothing floors this term — a five-stock gap costs, which is the intent.
        Assert.Equal(-2f, Term().Evaluate(In(P(stocks: 5), P(stocks: 0))), 0.0001f);
    }

    [Fact]
    public void ReadsTheFullSpreadAtThreeOrMorePlayers()
    {
        Assert.Equal(0f, Term().Evaluate(In(P(stocks: 3), P(stocks: 2), P(stocks: 0))), 0.0001f);
    }

    [Fact]
    public void BaselineIsTunableAndWasPreviouslyABareLiteral()
    {
        Assert.Equal(3f, new StockFairnessTerm(5f).Evaluate(In(P(stocks: 3), P(stocks: 1))), 0.0001f);
        Assert.Equal(0f, new StockFairnessTerm(0f).Evaluate(In(P(stocks: 2), P(stocks: 2))), 0.0001f);
    }
}
