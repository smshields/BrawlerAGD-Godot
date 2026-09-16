namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// KIT BREADTH: rewards even use of every move a character has —
/// <c>moveCount × minUses / totalUses</c>, which is 1 when all moves see identical use
/// and 0 the moment any move is never touched.
///
/// Deliberately a NUDGE (max +10 at two players against a ±100 scale): the designer
/// wanted even usage encouraged but NOT opinionated builds ruled out. Legacy fixtures
/// with no move-use record score 0 — the term is simply inert for them.
/// </summary>
public sealed class MoveMixTerm : IFitnessTerm
{
    public const string TermId = "moveMix";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("weight", "WEIGHT", StandardFitnessV3.DefaultMoveMixWeight, 0f, 50f,
            "Fitness per player at perfectly even move usage."),
    };

    private readonly float _weight;

    public MoveMixTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _weight = TermValues.Read(values, Specs, "weight");
    }

    public MoveMixTerm(float weight)
        : this(TermValues.Of(("weight", weight)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(("weight", _weight));

    public float Evaluate(FitnessInput input) =>
        _weight * input.Sum(PlayerAggregates.MoveEvenness);
}
