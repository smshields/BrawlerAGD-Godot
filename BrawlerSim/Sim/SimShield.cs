using BrawlerSim.Genome;

namespace BrawlerSim.Sim;

/// <summary>
/// A shield move's genome parameters resolved to tick-domain runtime values
/// (2026-07-12, FEATURES.md §Shield / docs/features/shield.md). All rates are
/// per-tick; BreakStunTicks is deliberately EXEMPT from MatchConfig.MaxStunSeconds —
/// the break stun is the mechanic's designed counterweight (designer decision).
/// </summary>
public sealed class SimShield
{
    public int WindUpTicks { get; }
    public int CoolDownTicks { get; }

    /// <summary>Radius at full health (genome stores the DIAMETER).</summary>
    public float InitialRadius { get; }

    public float HoldDegradationPerTick { get; }
    public float HitDegradationScalar { get; }
    public float KnockbackReduction { get; }
    public float SpacingPush { get; }
    public float RegenPerTick { get; }
    public int BreakStunTicks { get; }

    public SimShield(MoveGenome genome, MatchConfig config)
    {
        Params.ParamSet p = genome.Params;
        WindUpTicks = Math.Max(1, config.ToTicks(p.Get(ShieldParams.WindUpDuration)));
        CoolDownTicks = Math.Max(1, config.ToTicks(p.Get(ShieldParams.CoolDownDuration)));
        InitialRadius = p.Get(ShieldParams.InitialSize) / 2f;
        HoldDegradationPerTick = p.Get(ShieldParams.HoldDegradationRate) * config.Dt;
        HitDegradationScalar = p.Get(ShieldParams.HitDegradationScalar);
        KnockbackReduction = p.Get(ShieldParams.KnockbackReduction);
        SpacingPush = p.Get(ShieldParams.SpacingPush);
        RegenPerTick = p.Get(ShieldParams.RegenRate) * config.Dt;
        BreakStunTicks = config.ToTicks(p.Get(ShieldParams.BreakStunDuration)); // cap-EXEMPT
    }
}
