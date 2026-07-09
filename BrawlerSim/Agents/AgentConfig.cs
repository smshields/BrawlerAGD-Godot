using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

public enum AgentKind
{
    /// <summary>Utility-based playtester — the fitness instrument from 2026-07-09 on.</summary>
    Utility,

    /// <summary>
    /// The AIIDE '22 decision-tree port. Kept for comparison studies and the historical
    /// golden test; scheduled for archival once the utility pivot is designer-confirmed.
    /// </summary>
    DecisionTree,
}

/// <summary>
/// Per-run agent configuration (docs/features/utility-agent.md). Recorded in run.json so
/// every evolution run is reproducible including its instrument. The knobs are per
/// AGENT INSTANCE: a future feature can construct per-player configs from genome
/// parameters (the designer wants randomness evolvable eventually) without any
/// structural change here.
/// </summary>
public sealed record AgentConfig
{
    public AgentKind Kind { get; init; } = AgentKind.Utility;

    /// <summary>
    /// Selection stochasticity r ∈ [0,1]: with probability (1−r) a channel picks its
    /// argmax utility; with probability r it samples proportionally to the normalized
    /// utilities. 0 = deterministic best; 1 = fully proportional.
    /// </summary>
    public float Randomness { get; init; } = 0.15f;

    /// <summary>
    /// How many ticks a decision is held before re-evaluating (salient events re-decide
    /// early). 1 = every tick; 15 ≈ 250 ms — the human-reaction upper end.
    /// </summary>
    public int DecisionIntervalTicks { get; init; } = 8;

    public static readonly AgentConfig Default = new();

    public IInputSource CreateSource(Pcg32 rng) => Kind switch
    {
        AgentKind.DecisionTree => new DecisionTreeAgent(rng),
        _ => new UtilityAgent(rng, this),
    };
}
