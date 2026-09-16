namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// BALANCE, damage side: punishes the spread between the most and least damaged
/// fighter. At two players that spread IS |d0 − d1|, which is what v2 and v3 wrote.
///
/// Reads COUNTED damage, not raw — so a farm cannot distort fairness the way it would
/// if one player's uncapped 900-damage stock counted in full.
/// </summary>
public sealed class DamageFairnessTerm : IFitnessTerm
{
    public const string TermId = "damageFairness";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("damageScalar", "DAMAGE DIVISOR", 10f, 1f, 100f,
            "Divides the spread into fitness units. Shipped versions share one value with the damage term; a recipe may weight them apart."),
        new FitnessTermParam("stockDamageCap", "STOCK DAMAGE CAP", StandardFitnessV3.DefaultStockDamageCap, 50f, 2000f,
            "Per-life ceiling, so farming cannot distort the fairness reading."),
        FitnessTermParam.Flag("opponentOnly", "OPPONENT DAMAGE ONLY", false,
            "Flag: compare opponent-inflicted damage only."),
    };

    private readonly float _scalar;
    private readonly float _cap;
    private readonly bool _opponentOnly;

    public DamageFairnessTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _scalar = TermValues.Read(values, Specs, "damageScalar");
        _cap = TermValues.Read(values, Specs, "stockDamageCap");
        _opponentOnly = TermValues.Flag(values, Specs, "opponentOnly");
    }

    public DamageFairnessTerm(float damageScalar, float stockDamageCap, bool opponentOnly)
        : this(TermValues.Of(
            ("damageScalar", damageScalar),
            ("stockDamageCap", stockDamageCap),
            ("opponentOnly", opponentOnly ? 1f : 0f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("damageScalar", _scalar), ("stockDamageCap", _cap), ("opponentOnly", _opponentOnly ? 1f : 0f));

    public float Evaluate(FitnessInput input) =>
        -input.Spread(p => PlayerAggregates.CountedDamage(p, _cap, _opponentOnly)) / _scalar;
}
