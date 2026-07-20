using BrawlerSim.Genome;

namespace BrawlerSim.Sim;

/// <summary>
/// A dash move's genome parameters resolved to tick-domain values (2026-07-13,
/// FEATURES.md §Dash / docs/features/dash.md). Gravity is suspended during travel, so
/// the Acceleration gene degenerates to the travel SPEED, held for the duration. The
/// invulnerability genes are bools-as-floats (active ≥ 0.5). No cool-down by design.
/// </summary>
public sealed class SimDash
{
    public int WindUpTicks { get; }
    public int DurationTicks { get; }
    public float Speed { get; }
    public bool WarmUpInvulnerable { get; }
    public bool DurationInvulnerable { get; }

    /// <summary>2026-07-20 (designer): projectiles touching the dasher during the Dash
    /// state (either stage) are re-fired at their shooter — independent of i-frames.</summary>
    public bool Reflect { get; }

    public SimDash(MoveGenome genome, MatchConfig config)
    {
        Params.ParamSet p = genome.Params;
        WindUpTicks = Math.Max(1, config.ToTicks(p.Get(DashParams.WindUpDuration)));
        DurationTicks = Math.Max(1, config.ToTicks(p.Get(DashParams.Duration)));
        Speed = p.Get(DashParams.Acceleration);
        WarmUpInvulnerable = p.Get(DashParams.WarmUpInvulnerable) >= 0.5f;
        DurationInvulnerable = p.Get(DashParams.DurationInvulnerable) >= 0.5f;
        Reflect = p.Get(DashParams.Reflect) >= 0.5f;
    }
}
