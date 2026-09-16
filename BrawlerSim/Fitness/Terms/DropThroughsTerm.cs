namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// THIN-PLATFORM TIEBREAKER: a small, never-negative nudge toward stages whose
/// drop-through platforms actually get used.
///
/// Specified as a tiebreaker and nothing more — "add a minor boost to fitness if
/// increase is observed, but don't punish" (designer, thin-platform round). At +0.25
/// per drop capped at +1 it sits an order of magnitude below every shaping term, so it
/// can only separate games that are otherwise equal. Matches on all-solid stages score
/// exactly as they would without it.
/// </summary>
public sealed class DropThroughsTerm : IFitnessTerm
{
    public const string TermId = "dropThroughs";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("reward", "REWARD EACH", StandardFitnessV5.DefaultDropThroughReward, 0f, 10f,
            "Fitness per crouch drop through a thin platform."),
        new FitnessTermParam("cap", "MATCH CEILING", StandardFitnessV5.DefaultDropThroughCap, 0f, 100f,
            "Most this term can earn one match."),
    };

    private readonly float _reward;
    private readonly float _cap;

    public DropThroughsTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _reward = TermValues.Read(values, Specs, "reward");
        _cap = TermValues.Read(values, Specs, "cap");
    }

    public DropThroughsTerm(float reward, float cap)
        : this(TermValues.Of(("reward", reward), ("cap", cap)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("reward", _reward), ("cap", _cap));

    public float Evaluate(FitnessInput input) =>
        MathF.Min(_reward * input.SumInt(p => p.DropThroughs), _cap);
}
