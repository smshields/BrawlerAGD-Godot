using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v6 (2026-09-09, designer-directed stage-diversity experiment): standard-v5's
/// EXACT terms with the time reward RE-ANCHORED to map size. Rationale: the flat
/// −|45 − length| term is a homogenizer — bigger kill distances mean higher KO damage
/// and longer stocks, so large and enclosed stages lose on pacing before any of their
/// other qualities can score (measured in the 2026-09-04 zoning round: seed-2003 top
/// 86→40 purely on pacing terms). Scaling the target with the map's linear size lets
/// each map size have its own healthy pacing instead of one global one.
///
/// The scale s = sqrt(mapArea / legacyMapArea) — the geometric mean of the two
/// visible-half-extent ratios, so s tracks the map's LINEAR dimension (KO distance),
/// not its area. Legacy-size stages give exactly s = 1, where v6 ≡ v5. s is clamped
/// to the generation-range envelope [0.5, 5] (per-axis extents generate at 0.5×–5×
/// legacy) so beyond-domain range overrides cannot run the target to infinity.
/// Scored term: −|s·target − length| replaces −|target − length|; the overtime cliff
/// stays anchored to the UNSCALED max (it prices hitting the real match timeout).
/// Null StageMetrics (hand-built legacy fixtures) means s = 1, i.e. v5 verbatim.
///
/// NOT the default: v5 remains the default for new runs until the designer gates this
/// experiment. Selectable via --fitness standard-v6; run.json records it as usual.
/// </summary>
public sealed class StandardFitnessV6 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    /// <summary>Clamp bounds for the time scale — the stage schema's generation
    /// envelope (visible half extents generate at 0.5×–5× legacy per axis).</summary>
    public const float MinTimeScale = 0.5f;
    public const float MaxTimeScale = 5f;

    private readonly FitnessComposer _composed;

    public StandardFitnessV6(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar)
    {
        _composed = new FitnessComposer("standard-v6", ShippedTermLists.V6(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;

    /// <summary>sqrt(area ratio) vs the legacy map, clamped; 1 for null metrics.
    /// The body lives on TimeTerm since 2026-09-16; this stays as the name the
    /// scaled-time work and its tests already call.</summary>
    public static float TimeScale(StageMetrics? stage) =>
        TimeTerm.MapScale(stage, MinTimeScale, MaxTimeScale);
}
