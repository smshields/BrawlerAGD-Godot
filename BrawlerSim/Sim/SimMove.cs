using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Sim;

/// <summary>The phase of a move's execution. Hitbox is live only during Execute.</summary>
public enum MovePhase
{
    None,
    WarmUp,
    Execute,
    CoolDown,
}

/// <summary>
/// A move genome resolved into tick-domain runtime values. Immutable; per-activation
/// state (phase, countdown) lives on the owning SimPlayer.
/// </summary>
public sealed class SimMove
{
    /// <summary>Hitbox center offset from the player, at facing = +1 (X mirrors with facing).</summary>
    public Vec2 Offset { get; }

    /// <summary>Hitbox half extents BEFORE the owning player's scale multiplies in.</summary>
    public Vec2 BaseHalf { get; }

    public int WarmUpTicks { get; }
    public int ExecuteTicks { get; }
    public int CoolDownTicks { get; }

    public float DamageGiven { get; }
    public float KnockbackScalar { get; }

    /// <summary>Unit knockback direction at facing = +1 (Unity normalized it at move init).</summary>
    public Vec2 KnockbackDirection { get; }

    /// <summary>Base hitstun in seconds; effective stun scales with victim damage at hit time.</summary>
    public float HitstunDuration { get; }

    public SimMove(MoveGenome genome, MatchConfig config)
    {
        ParamSet p = genome.Params;
        Offset = MoveRules.MoveLocation(p);
        BaseHalf = new Vec2(
            config.MoveBaseSize * p.Get(MoveParams.WidthScalar) / 2f,
            config.MoveBaseSize * p.Get(MoveParams.HeightScalar) / 2f);

        WarmUpTicks = config.ToTicks(p.Get(MoveParams.WarmUpDuration));
        ExecuteTicks = config.ToTicks(p.Get(MoveParams.ExecutionDuration));
        CoolDownTicks = config.ToTicks(p.Get(MoveParams.CoolDownDuration));

        DamageGiven = MoveRules.DamageGiven(p);
        KnockbackScalar = p.Get(MoveParams.KnockbackScalar);
        HitstunDuration = p.Get(MoveParams.HitstunDuration);

        Vec2 raw = MoveRules.EffectiveKnockback(p);
        float length = raw.Length();
        KnockbackDirection = length > 1e-6f ? raw * (1f / length) : Vec2.Zero;
    }
}
