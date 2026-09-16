using BrawlerSim.Fitness.Terms;
using Xunit;
using static BrawlerSim.Tests.Fitness.Terms.TermFixture;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>Unit tests for JumpsTerm: weight × min(Σ jumps, saturation) / saturation.</summary>
public class JumpsTermTests
{
    private static JumpsTerm Term(float weight = 10f) => new(weight);

    private static JumpsTerm Unscaled() => new(new Dictionary<string, float>
    {
        ["weight"] = 10f, ["saturation"] = 40f, ["scaleWithPlayers"] = 0f,
    });

    [Fact]
    public void IsAFractionBelowSaturation()
    {
        Assert.Equal(0f, Term().Evaluate(In(P(jumps: 0), P(jumps: 0))), 0.0001f);
        Assert.Equal(5f, Term().Evaluate(In(P(jumps: 10), P(jumps: 10))), 0.0001f);
        Assert.Equal(10f, Term().Evaluate(In(P(jumps: 20), P(jumps: 20))), 0.0001f);
    }

    [Fact]
    public void SaturatesSoJumpSpamEarnsNothingExtra()
    {
        Assert.Equal(10f, Term().Evaluate(In(P(jumps: 400), P(jumps: 900))), 0.0001f);
    }

    [Fact]
    public void SaturationScalesWithPlayerCountToHoldPerPlayerExpectations()
    {
        // 40 per PAIR, so 20 per player: four players need 80 jumps to saturate.
        Assert.Equal(10f, Term().Evaluate(In(P(jumps: 20), P(jumps: 20), P(jumps: 20), P(jumps: 20))), 0.0001f);
        Assert.Equal(5f, Term().Evaluate(In(P(jumps: 10), P(jumps: 10), P(jumps: 10), P(jumps: 10))), 0.0001f);
    }

    [Fact]
    public void ScalingIsExactlyInertAtTwoPlayers()
    {
        // 40 × 2 / 2 == 40: why the two-player versions and the ffa versions agree.
        var input = In(P(jumps: 13), P(jumps: 11));
        Assert.Equal(Unscaled().Evaluate(input), Term().Evaluate(input));
    }

    [Fact]
    public void ScalingCanBeTurnedOff()
    {
        // Unscaled, four players saturate at 40 total rather than 80.
        Assert.Equal(10f, Unscaled().Evaluate(In(P(jumps: 10), P(jumps: 10), P(jumps: 10), P(jumps: 10))), 0.0001f);
    }

    [Fact]
    public void SaturationPointIsTunableAndWasPreviouslyHardCoded()
    {
        var early = new JumpsTerm(new Dictionary<string, float>
        {
            ["weight"] = 10f, ["saturation"] = 10f, ["scaleWithPlayers"] = 1f,
        });
        Assert.Equal(10f, early.Evaluate(In(P(jumps: 5), P(jumps: 5))), 0.0001f);
    }
}
