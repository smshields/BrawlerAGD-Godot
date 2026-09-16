namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// DEFENSIVE INTERACTION: a reward per blocked hit.
///
/// Exists because a blocked hit SUPPRESSES roughly 1.15 fitness of rewarded
/// interaction (the hit's damage contribution plus its collision contribution) — so
/// under a shield-blind fitness, shields were selected AGAINST, measurably: champions
/// with 3 activations and 0 blocks. Paying 2.0 per block makes blocking a net-positive
/// event comparable to landing a hit. Naturally bounded, since every block costs
/// shield health.
/// </summary>
public sealed class BlocksTerm : IFitnessTerm
{
    public const string TermId = "blocks";

    public static IReadOnlyList<FitnessTermParam> Specs { get; } = new[]
    {
        new FitnessTermParam("reward", "REWARD PER BLOCK", StandardFitnessV3.DefaultBlockReward, 0f, 20f,
            "Fitness per hit blocked across all players."),
        FitnessTermParam.Flag("opponentOnly", "OPPONENT HITS ONLY", false,
            "Flag: blocking your own bolt earns nothing."),
    };

    private readonly float _reward;
    private readonly bool _opponentOnly;

    public BlocksTerm(IReadOnlyDictionary<string, float>? values = null)
    {
        _reward = TermValues.Read(values, Specs, "reward");
        _opponentOnly = TermValues.Flag(values, Specs, "opponentOnly");
    }

    public BlocksTerm(float reward, bool opponentOnly)
        : this(TermValues.Of(("reward", reward), ("opponentOnly", opponentOnly ? 1f : 0f)))
    {
    }

    public string Id => TermId;

    public IReadOnlyDictionary<string, float> Values => TermValues.Of(
        ("reward", _reward), ("opponentOnly", _opponentOnly ? 1f : 0f));

    public float Evaluate(FitnessInput input) =>
        _reward * input.Sum(p => PlayerAggregates.BlockedHits(p, _opponentOnly));
}
