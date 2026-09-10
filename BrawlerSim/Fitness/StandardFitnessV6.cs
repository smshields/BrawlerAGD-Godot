using BrawlerSim.Genome;
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
public sealed class StandardFitnessV6 : IFitnessFunction, IFitnessBreakdown
{
    /// <summary>Clamp bounds for the time scale — the stage schema's generation
    /// envelope (visible half extents generate at 0.5×–5× legacy per axis).</summary>
    public const float MinTimeScale = 0.5f;
    public const float MaxTimeScale = 5f;

    private readonly StandardFitnessV5 _v5;
    private readonly float _target;

    public StandardFitnessV6(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar)
    {
        _v5 = new StandardFitnessV5(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar);
        _target = targetLengthSeconds;
    }

    public string Name => "standard-v6";

    public float Evaluate(MatchResult result) =>
        _v5.Evaluate(result) + TimeRescaleTerm(result, _target);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result)
    {
        var terms = new List<(string, float)>(_v5.Breakdown(result))
        {
            ("timeRescale", TimeRescaleTerm(result, _target)),
        };
        return terms;
    }

    /// <summary>sqrt(area ratio) vs the legacy map, clamped; 1 for null metrics.</summary>
    public static float TimeScale(StageMetrics? stage)
    {
        if (stage is null)
        {
            return 1f;
        }
        float ratio = (stage.VisibleHalfWidth * stage.VisibleHalfHeight)
            / (StageRules.LegacyVisibleHalfWidth * StageRules.LegacyVisibleHalfHeight);
        return Math.Clamp(MathF.Sqrt(ratio), MinTimeScale, MaxTimeScale);
    }

    /// <summary>Swaps the v3 time reward's anchor: +|target − len| backs out the flat
    /// term, −|s·target − len| replaces it. Exactly 0 at s = 1 (v5 verbatim); the
    /// overtime cliff is untouched by construction.</summary>
    internal static float TimeRescaleTerm(MatchResult result, float target)
    {
        float s = TimeScale(result.Stage);
        return MathF.Abs(target - result.LengthSeconds)
            - MathF.Abs(s * target - result.LengthSeconds);
    }
}
