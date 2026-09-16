namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// The INTERACTION REWARD: Σ per-stock damage taken, each life clipped at a cap, over
/// a scalar. The cap is the "this game cannot kill" rule — knockback grows with damage,
/// so past the cap a hit should almost always kill, and a stock that absorbs more than
/// that is a farm. Damage past the cap therefore counts for NOTHING rather than paying
/// out forever.
///
/// <c>opponentOnly</c> (standard-v7's self-hit reward fix) excludes damage a fighter
/// inflicted on itself — the measured exploit was champions taking 85-97% of all match
/// damage from their own bolts and having it scored as interaction.
/// </summary>
public sealed class DamageTerm : IFitnessTerm
{
    public const string TermId = "damage";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("stockDamageCap", "STOCK DAMAGE CAP", StandardFitnessV3.DefaultStockDamageCap, 50f, 2000f,
            "Per-life ceiling on counted damage. Damage past it scores nothing."),
        new FitnessTermParam("damageScalar", "DAMAGE DIVISOR", 10f, 1f, 100f,
            "Divides the counted total into fitness units."),
        FitnessTermParam.Flag("opponentOnly", "OPPONENT DAMAGE ONLY", false,
            "Flag: exclude self-inflicted damage from the reward."),
    };

    private readonly float _cap;
    private readonly float _scalar;
    private readonly bool _opponentOnly;

    public DamageTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _cap = TermValues.Read(values, Specs, "stockDamageCap");
        _scalar = TermValues.Read(values, Specs, "damageScalar");
        _opponentOnly = TermValues.Flag(values, Specs, "opponentOnly");
    }

    public DamageTerm(float stockDamageCap, float damageScalar, bool opponentOnly)
        : this(TermValues.Of(
            ("stockDamageCap", stockDamageCap),
            ("damageScalar", damageScalar),
            ("opponentOnly", opponentOnly ? 1f : 0f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("stockDamageCap", _cap), ("damageScalar", _scalar), ("opponentOnly", _opponentOnly ? 1f : 0f));

    public float Evaluate(FitnessInput input) =>
        input.Sum(p => PlayerAggregates.CountedDamage(p, _cap, _opponentOnly)) / _scalar;
}
