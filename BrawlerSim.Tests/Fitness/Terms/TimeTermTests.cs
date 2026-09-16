using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for TimeTerm: −|T − L| + overtime cliff, optionally with the
/// target re-anchored to map size.</summary>
public class TimeTermTests
{
    private static TimeTerm Flat() => new(45f, 60f, scaleWithMap: false);
    private static TimeTerm Scaled() => new(45f, 60f, scaleWithMap: true);

    [Theory]
    [InlineData(45f, 0f)]      // exactly on target
    [InlineData(30f, -15f)]    // short
    [InlineData(50f, -5f)]     // long, still under the cliff
    public void RewardsDistanceFromTheTarget(float length, float expected) =>
        Assert.Equal(expected, Flat().Evaluate(In(length, (int)(length * 60f), null, P())), 0.0001f);

    [Fact]
    public void OvertimeCliffFiresAtTheMaxInclusive()
    {
        // 59 s: −14, no cliff. 60 s: −15 AND the −35 cliff. 65 s: −20 − 35.
        Assert.Equal(-14f, Flat().Evaluate(In(59f, 3540, null, P())), 0.0001f);
        Assert.Equal(-50f, Flat().Evaluate(In(60f, 3600, null, P())), 0.0001f);
        Assert.Equal(-55f, Flat().Evaluate(In(65f, 3900, null, P())), 0.0001f);
    }

    [Fact]
    public void OvertimePenaltyIsTunable()
    {
        var soft = new TimeTerm(TermValuesFor(overtime: -5f));
        Assert.Equal(-20f, soft.Evaluate(In(60f, 3600, null, P())), 0.0001f);
    }

    private static Dictionary<string, float> TermValuesFor(float overtime) => new()
    {
        ["target"] = 45f, ["max"] = 60f, ["overtimePenalty"] = overtime,
        ["scaleWithMap"] = 0f, ["minScale"] = 0.5f, ["maxScale"] = 5f,
    };

    [Fact]
    public void MapScalingIsInertOnLegacySizedAndNullStages()
    {
        // s = 1 at legacy size, and a null stage (hand-built fixtures) is treated as
        // legacy — so the scaled term equals the flat one exactly.
        Assert.Equal(Flat().Evaluate(In(45f, 2700, null, P())),
                     Scaled().Evaluate(In(45f, 2700, null, P())));
        Assert.Equal(Flat().Evaluate(In(30f, 1800, LegacyStage, P())),
                     Scaled().Evaluate(In(30f, 1800, LegacyStage, P())));
    }

    [Fact]
    public void MapScalingReAnchorsTheTargetToLinearMapSize()
    {
        // Half extents doubled on both axes: area ×4, s = sqrt(4) = 2, target 90 s.
        // A 45 s match on that map is now 45 s SHORT of its target, not on it.
        Assert.Equal(-45f, Scaled().Evaluate(In(45f, 2700, Stage(2f, 2f), P())), 0.0001f);
        // At 90 s the scaled target is met exactly, so only the overtime cliff remains.
        Assert.Equal(-35f, Scaled().Evaluate(In(90f, 5400, Stage(2f, 2f), P())), 0.0001f);
    }

    [Fact]
    public void MapScaleTracksAreaNotEitherAxis()
    {
        // s is the geometric mean, so 4× wide by 1× tall scales the same as 2× by 2×.
        Assert.Equal(Scaled().Evaluate(In(45f, 2700, Stage(2f, 2f), P())),
                     Scaled().Evaluate(In(45f, 2700, Stage(4f, 1f), P())), 0.0001f);
    }

    [Fact]
    public void MapScaleClampsAtBothEnds()
    {
        // Clamped to [0.5, 5]: a 10×10 map would be s = 10 uncapped → target 225 s,
        // but clamps to 5 → target 225... no: 5 × 45 = 225. Clamp makes it 5, so the
        // target is 225 s and a 45 s match scores −180.
        Assert.Equal(-180f, Scaled().Evaluate(In(45f, 2700, Stage(10f, 10f), P())), 0.0001f);
        // And a tiny map clamps UP to 0.5 → target 22.5 s.
        Assert.Equal(-22.5f, Scaled().Evaluate(In(45f, 2700, Stage(0.1f, 0.1f), P())), 0.0001f);
    }

    [Fact]
    public void OvertimeCliffIsNotScaledByMapSize()
    {
        // The cliff prices the REAL match timeout, which does not move with the stage:
        // 60 s on a double-size map still fires it (target 90, so −30 − 35).
        Assert.Equal(-65f, Scaled().Evaluate(In(60f, 3600, Stage(2f, 2f), P())), 0.0001f);
    }

    [Fact]
    public void MapScaleHelperIsTheDocumentedFormula()
    {
        Assert.Equal(1f, TimeTerm.MapScale(null, 0.5f, 5f), 0.0001f);
        Assert.Equal(1f, TimeTerm.MapScale(LegacyStage, 0.5f, 5f), 0.0001f);
        Assert.Equal(2f, TimeTerm.MapScale(Stage(2f, 2f), 0.5f, 5f), 0.0001f);
        Assert.Equal(5f, TimeTerm.MapScale(Stage(10f, 10f), 0.5f, 5f), 0.0001f);
        Assert.Equal(0.5f, TimeTerm.MapScale(Stage(0.1f, 0.1f), 0.5f, 5f), 0.0001f);
    }

    [Fact]
    public void ValuesRoundTripThroughTheRegistry()
    {
        var term = new TimeTerm(30f, 120f, scaleWithMap: true);
        var rebuilt = new TimeTerm(term.Values);
        Assert.Equal(term.Values, rebuilt.Values);
        Assert.Equal(term.Evaluate(In(45f, 2700, Stage(2f, 2f), P())),
                     rebuilt.Evaluate(In(45f, 2700, Stage(2f, 2f), P())));
    }
}
