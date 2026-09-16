namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// The FARM PUNISHMENT: Σ per-stock damage beyond a threshold, each stock's excess
/// saturating at (cap − start). Punishes the single stock that absorbed far more than
/// a kill should take.
///
/// BOUNDED on purpose. v2's unbounded version was the dominant fitness-noise amplifier
/// in the 2026-07-09 noise study: stalling longer dug an ever-deeper hole. Saturating
/// per stock makes a fully farmed life cost a fixed, severe amount instead.
/// </summary>
public sealed class FarmPenaltyTerm : IFitnessTerm
{
    public const string TermId = "farmPenalty";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("punishStart", "PUNISH FROM", StandardFitnessV3.DefaultPunishStartDamage, 0f, 2000f,
            "Per-life damage at which punishment begins."),
        new FitnessTermParam("stockDamageCap", "SATURATE AT", StandardFitnessV3.DefaultStockDamageCap, 50f, 2000f,
            "Per-life damage at which the punishment stops growing."),
        new FitnessTermParam("slope", "PENALTY PER POINT", StandardFitnessV3.DefaultPunishSlope, 0f, 10f,
            "Fitness cost per point of excess damage."),
        FitnessTermParam.Flag("opponentOnly", "OPPONENT DAMAGE ONLY", false,
            "Flag: self-inflicted damage neither earns nor costs."),
    };

    private readonly float _start;
    private readonly float _cap;
    private readonly float _slope;
    private readonly bool _opponentOnly;

    public FarmPenaltyTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _start = TermValues.Read(values, Specs, "punishStart");
        _cap = TermValues.Read(values, Specs, "stockDamageCap");
        _slope = TermValues.Read(values, Specs, "slope");
        _opponentOnly = TermValues.Flag(values, Specs, "opponentOnly");
    }

    public FarmPenaltyTerm(float punishStart, float stockDamageCap, float slope, bool opponentOnly)
        : this(TermValues.Of(
            ("punishStart", punishStart),
            ("stockDamageCap", stockDamageCap),
            ("slope", slope),
            ("opponentOnly", opponentOnly ? 1f : 0f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("punishStart", _start), ("stockDamageCap", _cap), ("slope", _slope),
        ("opponentOnly", _opponentOnly ? 1f : 0f));

    public float Evaluate(FitnessInput input) =>
        -_slope * input.Sum(p => PlayerAggregates.Excess(p, _start, _cap, _opponentOnly));
}
