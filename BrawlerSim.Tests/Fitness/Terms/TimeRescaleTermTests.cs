using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for TimeRescaleTerm — standard-v6's correction, which backs a
/// flat time reward out and puts the map-scaled one in.</summary>
public class TimeRescaleTermTests
{
    private static TimeRescaleTerm Term() => new(45f);

    [Fact]
    public void IsExactlyZeroWhereTheMapScaleIsOne()
    {
        // This is why standard-v6 == standard-v5 on legacy-sized stages.
        Assert.Equal(0f, Term().Evaluate(In(45f, 2700, null, P())), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(20f, 1200, LegacyStage, P())), 0.0001f);
        Assert.Equal(0f, Term().Evaluate(In(200f, 12000, LegacyStage, P())), 0.0001f);
    }

    [Fact]
    public void SwapsTheFlatTargetForTheScaledOne()
    {
        // s = 2 → target 90. At L = 45: +|45−45| − |90−45| = 0 − 45 = −45.
        Assert.Equal(-45f, Term().Evaluate(In(45f, 2700, Stage(2f, 2f), P())), 0.0001f);
        // At L = 90 the scaled target is met: +|45−90| − |90−90| = +45.
        Assert.Equal(45f, Term().Evaluate(In(90f, 5400, Stage(2f, 2f), P())), 0.0001f);
    }

    [Fact]
    public void CancelsTheFlatTimeTermExactly()
    {
        // The contract that makes v6 expressible as v5 + this: flat + correction must
        // equal the scaled term, for the same target, on any stage.
        var flat = new TimeTerm(45f, 60f, scaleWithMap: false);
        var scaled = new TimeTerm(45f, 60f, scaleWithMap: true);
        foreach (float length in new[] { 12f, 45f, 90f, 200f })
        {
            var input = In(length, (int)(length * 60f), Stage(3f, 2f), P());
            Assert.Equal(scaled.Evaluate(input), flat.Evaluate(input) + Term().Evaluate(input), 0.001f);
        }
    }
}
