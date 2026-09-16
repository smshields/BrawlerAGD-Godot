namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// CHAIN PUNISHMENT: prices the SHARE of the match each player spent stunned, above a
/// tolerance. This is what the per-hit stun cap cannot reach — a chain is many legal
/// hits, each individually fine.
///
/// At the defaults a player stunned 26% of the match costs −55 and one at 46% costs
/// −155, which makes this one of the sharpest terms in the function. The tolerance was
/// unreachable before 2026-09-16 (a <c>const</c> read from inside the shared stun
/// helper), and the ×100 was an inline literal folded into the weight.
/// </summary>
public sealed class StunLockTerm : IFitnessTerm
{
    public const string TermId = "stunLock";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("weight", "WEIGHT", StandardFitnessV3.DefaultStunLockWeight, 0f, 50f,
            "Penalty scale on the excess stun share."),
        new FitnessTermParam("tolerance", "TOLERATED SHARE", StandardFitnessV3.DefaultStunShareTolerance, 0f, 1f,
            "Share of the match a player may spend stunned for free."),
        new FitnessTermParam("percentScale", "PERCENT SCALE", 100f, 1f, 1000f,
            "Converts the share into percentage points before weighting."),
    };

    private readonly float _weight;
    private readonly float _tolerance;
    private readonly float _percentScale;

    public StunLockTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _weight = TermValues.Read(values, Specs, "weight");
        _tolerance = TermValues.Read(values, Specs, "tolerance");
        _percentScale = TermValues.Read(values, Specs, "percentScale");
    }

    public StunLockTerm(float weight)
        : this(TermValues.Of(
            ("weight", weight),
            ("tolerance", StandardFitnessV3.DefaultStunShareTolerance),
            ("percentScale", 100f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("weight", _weight), ("tolerance", _tolerance), ("percentScale", _percentScale));

    public float Evaluate(FitnessInput input) =>
        -_weight * _percentScale * input.Sum(p => PlayerAggregates.StunExcess(p, input.Ticks, _tolerance));
}
