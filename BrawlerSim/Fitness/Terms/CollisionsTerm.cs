namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// INTERACTION DENSITY: a flat reward per hit landed, over all players.
///
/// The weight is the tuned half-point, not a round number. At v2's implicit 1.0 a
/// 191-hit farmed stock recouped 65% of its farm penalty through this term, and
/// healthy matches double-counted hits (collisions ran ~1.5× the damage term, since a
/// hit averages ~6.5 damage). At 0.5 the two terms come out level on healthy champion
/// rounds and farm recoup falls to 32%.
/// </summary>
public sealed class CollisionsTerm : IFitnessTerm
{
    public const string TermId = "collisions";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("scalar", "REWARD PER HIT", StandardFitnessV3.DefaultCollisionScalar, 0f, 5f,
            "Fitness per hit received across all players."),
        FitnessTermParam.Flag("opponentOnly", "OPPONENT HITS ONLY", false,
            "Flag: a bolt clipping its own shooter is not interaction."),
    };

    private readonly float _scalar;
    private readonly bool _opponentOnly;

    public CollisionsTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _scalar = TermValues.Read(values, Specs, "scalar");
        _opponentOnly = TermValues.Flag(values, Specs, "opponentOnly");
    }

    public CollisionsTerm(float scalar, bool opponentOnly)
        : this(TermValues.Of(("scalar", scalar), ("opponentOnly", opponentOnly ? 1f : 0f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("scalar", _scalar), ("opponentOnly", _opponentOnly ? 1f : 0f));

    public float Evaluate(FitnessInput input) =>
        _scalar * input.Sum(p => PlayerAggregates.HitsReceived(p, _opponentOnly));
}
