using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using Xunit;

namespace BrawlerSim.Tests.Genome;

public class MoveRulesTests
{
    private static ParamSet Move(params (string Key, float Value)[] overrides)
    {
        // Neutral baseline, then apply overrides.
        var baseline = new Dictionary<string, float>
        {
            [MoveParams.MoveDist] = 1f,
            [MoveParams.MoveAngle] = 0f,
            [MoveParams.WidthScalar] = 1f,
            [MoveParams.HeightScalar] = 1f,
            [MoveParams.WarmUpDuration] = 0.2f,
            [MoveParams.ExecutionDuration] = 0.2f,
            [MoveParams.CoolDownDuration] = 0.2f,
            [MoveParams.DamageFactor] = 5f,
            [MoveParams.KnockbackScalar] = 8f,
            [MoveParams.KnockbackModX] = 1f,
            [MoveParams.KnockbackModY] = 0f,
            [MoveParams.HitstunDuration] = 0.5f,
        };
        foreach ((string key, float value) in overrides)
        {
            baseline[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Move, baseline);
    }

    [Fact]
    public void DamageGivenIsBasePlusDurationsTimesFactor()
    {
        // 5 + (0.2 + 0.2 + 0.2) * 5 = 8
        Assert.Equal(8f, MoveRules.DamageGiven(Move()), 0.0001f);
    }

    [Fact]
    public void MoveLocationComesFromPolarParams()
    {
        var loc = MoveRules.MoveLocation(Move(
            (MoveParams.MoveDist, 1.5f),
            (MoveParams.MoveAngle, MathF.PI / 2f)));
        Assert.Equal(0f, loc.X, 0.0001f);
        Assert.Equal(1.5f, loc.Y, 0.0001f);
    }

    [Fact]
    public void KnockbackAlignedWithHitboxIsFlipped()
    {
        // Hitbox at angle 0 (pointing +X), knockback also +X → within 45° → X flips.
        var move = Move((MoveParams.KnockbackModX, 1f), (MoveParams.KnockbackModY, 0.1f));
        Vec2 effective = MoveRules.EffectiveKnockback(move);
        Assert.Equal(-1f, effective.X, 0.0001f);
        Assert.Equal(0.1f, effective.Y, 0.0001f);
    }

    [Fact]
    public void KnockbackAwayFromHitboxIsUntouched()
    {
        // Hitbox +X, knockback straight up (90°) → no flip.
        var move = Move((MoveParams.KnockbackModX, 0f), (MoveParams.KnockbackModY, 1f));
        Vec2 effective = MoveRules.EffectiveKnockback(move);
        Assert.Equal(0f, effective.X, 0.0001f);
        Assert.Equal(1f, effective.Y, 0.0001f);
    }

    [Fact]
    public void ConstrainKnockbackPullsWideAnglesUnder135Degrees()
    {
        // Hitbox points +X; knockback nearly opposite (just inside [0,1]×[-1,1] ranges).
        var move = Move((MoveParams.KnockbackModX, 0f), (MoveParams.KnockbackModY, -1f),
            (MoveParams.MoveAngle, MathF.PI * 0.75f)); // hitbox at 135° → kb at -90° is 225° apart → 135° unsigned
        var constrained = MoveRules.ConstrainKnockback(move);

        Vec2 kb = new(constrained.Get(MoveParams.KnockbackModX), constrained.Get(MoveParams.KnockbackModY));
        Assert.True(Vec2.AngleDeg(MoveRules.MoveLocation(constrained), kb) < 135f);
        // The hitbox params themselves must be untouched.
        Assert.Equal(move.Get(MoveParams.MoveAngle), constrained.Get(MoveParams.MoveAngle));
        Assert.Equal(move.Get(MoveParams.MoveDist), constrained.Get(MoveParams.MoveDist));
    }

    [Fact]
    public void ConstrainKnockbackLeavesNarrowAnglesAlone()
    {
        var move = Move((MoveParams.KnockbackModX, 0f), (MoveParams.KnockbackModY, 1f)); // 90° from +X hitbox
        var constrained = MoveRules.ConstrainKnockback(move);
        Assert.Equal(move.ToArray(), constrained.ToArray());
    }

    [Fact]
    public void GeneratedMovesAlwaysSatisfyTheConstraint()
    {
        var rng = new Pcg32(2024);
        for (int i = 0; i < 500; i++)
        {
            var move = MoveGenome.Generate(GenerationConfig.Default, rng);
            float angle = Vec2.AngleDeg(
                MoveRules.MoveLocation(move.Params),
                new Vec2(move.Params.Get(MoveParams.KnockbackModX), move.Params.Get(MoveParams.KnockbackModY)));
            Assert.True(angle < 135f, $"generated move {i} violates the knockback constraint ({angle}°)");
            Assert.Empty(move.Params.Validate());
        }
    }
}
