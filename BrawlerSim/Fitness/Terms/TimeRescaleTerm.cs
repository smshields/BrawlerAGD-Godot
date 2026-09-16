using BrawlerSim.Sim;

namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// standard-v6's CORRECTION term, and nothing else: <c>+|T − L| − |s·T − L|</c> backs
/// the flat time reward out and puts the map-scaled one in.
///
/// It exists only because v6 was built as a decorator over v5 and could therefore only
/// ADD to a sum it could not edit. Keeping it is what lets v6 stay bit-frozen through
/// the term refactor. Nothing new should use it — set <c>scaleWithMap</c> on
/// <see cref="TimeTerm"/> instead, which is what standard-v7 does.
/// </summary>
public sealed class TimeRescaleTerm : IFitnessTerm
{
    public const string TermId = "timeRescale";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("target", "TARGET SECONDS", 45f, 5f, 300f,
            "Must match the time term's target — this backs that term's reward out."),
        new FitnessTermParam("minScale", "MIN MAP SCALE", StandardFitnessV6.MinTimeScale, 0.1f, 5f,
            "Lower clamp on the map scale."),
        new FitnessTermParam("maxScale", "MAX MAP SCALE", StandardFitnessV6.MaxTimeScale, 0.5f, 20f,
            "Upper clamp on the map scale."),
    };

    private readonly float _target;
    private readonly float _minScale;
    private readonly float _maxScale;

    public TimeRescaleTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _target = TermValues.Read(values, Specs, "target");
        _minScale = TermValues.Read(values, Specs, "minScale");
        _maxScale = TermValues.Read(values, Specs, "maxScale");
    }

    public TimeRescaleTerm(float target)
        : this(TermValues.Of(
            ("target", target),
            ("minScale", StandardFitnessV6.MinTimeScale),
            ("maxScale", StandardFitnessV6.MaxTimeScale)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("target", _target), ("minScale", _minScale), ("maxScale", _maxScale));

    public float Evaluate(FitnessInput input)
    {
        float scale = TimeTerm.MapScale(input.Stage, _minScale, _maxScale);
        return MathF.Abs(_target - input.LengthSeconds)
            - MathF.Abs(scale * _target - input.LengthSeconds);
    }
}
