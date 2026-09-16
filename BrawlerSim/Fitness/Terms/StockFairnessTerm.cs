namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// BALANCE, outcome side: a flat bonus reduced by the remaining-stock spread at match
/// end, so a 3-2 finish scores above a 3-0 sweep. Small by design — it ranks close
/// matches above blowouts without being able to outvote pacing or interaction.
///
/// The baseline was a bare <c>3f</c> literal in every shipped version (and carries the
/// implicit assumption of a three-stock match); it is a parameter from 2026-09-16.
/// </summary>
public sealed class StockFairnessTerm : IFitnessTerm
{
    public const string TermId = "stockFairness";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("baseline", "BASELINE", 3f, 0f, 20f,
            "Bonus for a dead-even finish, reduced by the stock spread."),
    };

    private readonly float _baseline;

    public StockFairnessTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _baseline = TermValues.Read(values, Specs, "baseline");
    }

    public StockFairnessTerm(float baseline)
        : this(TermValues.Of(("baseline", baseline)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(("baseline", _baseline));

    public float Evaluate(FitnessInput input) =>
        _baseline - input.Spread(p => p.RemainingStocks);
}
