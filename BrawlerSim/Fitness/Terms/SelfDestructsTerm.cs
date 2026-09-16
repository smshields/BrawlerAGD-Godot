namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// STAGE HAZARD PUNISHMENT: −1 per death with no attributable enemy influence, CAPPED
/// so one degenerate match cannot drown every other signal.
///
/// "Punish stages that have characters who self-destruct" (designer, four-player
/// round). What counts as a self-destruct is the sim's KO-attribution rule — hits and
/// real pushes, cleared by 15 continuously grounded ticks — not this term's business.
/// </summary>
public sealed class SelfDestructsTerm : IFitnessTerm
{
    public const string TermId = "selfDestructs";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("penalty", "PENALTY EACH", StandardFitnessV4.DefaultSelfDestructPenalty, 0f, 20f,
            "Fitness cost per self-destruct."),
        new FitnessTermParam("cap", "MATCH FLOOR", StandardFitnessV4.DefaultSelfDestructCap, 0f, 100f,
            "Most this term can cost one match, as a positive magnitude."),
    };

    private readonly float _penalty;
    private readonly float _cap;

    public SelfDestructsTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _penalty = TermValues.Read(values, Specs, "penalty");
        _cap = TermValues.Read(values, Specs, "cap");
    }

    public SelfDestructsTerm(float penalty, float cap)
        : this(TermValues.Of(("penalty", penalty), ("cap", cap)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("penalty", _penalty), ("cap", _cap));

    public float Evaluate(FitnessInput input) =>
        -MathF.Min(_penalty * input.SumInt(p => p.SelfDestructs), _cap);
}
