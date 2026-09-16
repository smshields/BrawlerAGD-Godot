using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// PACING. Distance from a target match length, plus a cliff for hitting the timeout:
/// <c>−|T − L| + (L ≥ M ? overtime : 0)</c>. The largest shaper in practice.
///
/// <c>scaleWithMap</c> (standard-v6's idea, graduated to default in v7) re-anchors the
/// target to the map's LINEAR size, <c>s = clamp(sqrt(mapArea / legacyArea), min, max)</c>:
/// bigger kill distances mean higher KO damage and longer stocks, so without it large
/// and enclosed stages lose on pacing before any of their other qualities can score.
/// The overtime cliff stays anchored to the UNSCALED max — it prices hitting the real
/// match timeout, which does not move with the stage.
/// </summary>
public sealed class TimeTerm : IFitnessTerm
{
    public const string TermId = "time";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("target", "TARGET SECONDS", 45f, 5f, 300f,
            "Match length the term rewards being close to."),
        new FitnessTermParam("max", "OVERTIME AT", 60f, 5f, 600f,
            "Length at or past which the overtime penalty fires. Not scaled by map size."),
        new FitnessTermParam("overtimePenalty", "OVERTIME PENALTY", StandardFitnessV3.OvertimePenalty, -200f, 0f,
            "Flat cliff added once the match reaches the overtime length."),
        FitnessTermParam.Flag("scaleWithMap", "SCALE TARGET BY MAP SIZE", false,
            "Flag: re-anchor the target to sqrt(mapArea / legacyArea)."),
        new FitnessTermParam("minScale", "MIN MAP SCALE", StandardFitnessV6.MinTimeScale, 0.1f, 5f,
            "Lower clamp on the map scale. Matches the stage schema's generation envelope."),
        new FitnessTermParam("maxScale", "MAX MAP SCALE", StandardFitnessV6.MaxTimeScale, 0.5f, 20f,
            "Upper clamp on the map scale, so beyond-domain stage ranges cannot run the target to infinity."),
    };

    private readonly float _target;
    private readonly float _max;
    private readonly float _overtimePenalty;
    private readonly bool _scaleWithMap;
    private readonly float _minScale;
    private readonly float _maxScale;

    public TimeTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _target = TermValues.Read(values, Specs, "target");
        _max = TermValues.Read(values, Specs, "max");
        _overtimePenalty = TermValues.Read(values, Specs, "overtimePenalty");
        _scaleWithMap = TermValues.Flag(values, Specs, "scaleWithMap");
        _minScale = TermValues.Read(values, Specs, "minScale");
        _maxScale = TermValues.Read(values, Specs, "maxScale");
    }

    public TimeTerm(float target, float max, bool scaleWithMap)
        : this(TermValues.Of(
            ("target", target),
            ("max", max),
            ("overtimePenalty", StandardFitnessV3.OvertimePenalty),
            ("scaleWithMap", scaleWithMap ? 1f : 0f),
            ("minScale", StandardFitnessV6.MinTimeScale),
            ("maxScale", StandardFitnessV6.MaxTimeScale)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("target", _target),
        ("max", _max),
        ("overtimePenalty", _overtimePenalty),
        ("scaleWithMap", _scaleWithMap ? 1f : 0f),
        ("minScale", _minScale),
        ("maxScale", _maxScale));

    public float Evaluate(FitnessInput input)
    {
        float target = _scaleWithMap
            ? MapScale(input.Stage, _minScale, _maxScale) * _target
            : _target;
        return -DetMath.Abs(target - input.LengthSeconds)
            + (input.LengthSeconds >= _max ? _overtimePenalty : 0f);
    }

    /// <summary>sqrt(area ratio) vs the legacy map, clamped; exactly 1 for a null
    /// stage (hand-built legacy fixtures) and for a legacy-size stage.</summary>
    public static float MapScale(StageMetrics? stage, float min, float max)
    {
        if (stage is null)
        {
            return 1f;
        }
        float ratio = (stage.VisibleHalfWidth * stage.VisibleHalfHeight)
            / (StageRules.LegacyVisibleHalfWidth * StageRules.LegacyVisibleHalfHeight);
        return Math.Clamp(MathF.Sqrt(ratio), min, max);
    }
}
