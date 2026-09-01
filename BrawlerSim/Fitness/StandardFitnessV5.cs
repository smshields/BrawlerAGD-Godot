using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v5 (2026-09-01, designer-specified; docs/features/thin-platforms.md):
/// standard-v4's EXACT terms plus a DROP-THROUGH tiebreaker — "add a minor boost to
/// fitness if increase is observed, but don't punish. This should only be a minor
/// tiebreaker." The term: +0.25 per crouch drop through a thin platform summed over
/// all players, CAPPED at +1 per match and never negative — an order of magnitude
/// below the shaping terms, so it can only break ties between otherwise-equal games.
/// Matches on all-solid stages score identically to v4.
///
/// v4 remains frozen and selectable; v5 is the default for NEW two-player runs.
/// </summary>
public sealed class StandardFitnessV5 : IFitnessFunction, IFitnessBreakdown
{
    public const float DefaultDropThroughReward = 0.25f;
    public const float DefaultDropThroughCap = 1f;

    private readonly StandardFitnessV4 _v4;
    private readonly float _dropReward;
    private readonly float _dropCap;

    public StandardFitnessV5(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float dropThroughReward = DefaultDropThroughReward,
        float dropThroughCap = DefaultDropThroughCap)
    {
        _v4 = new StandardFitnessV4(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar);
        _dropReward = dropThroughReward;
        _dropCap = dropThroughCap;
    }

    public string Name => "standard-v5";

    public float Evaluate(MatchResult result) =>
        _v4.Evaluate(result) + DropThroughTerm(result, _dropReward, _dropCap);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result)
    {
        var terms = new List<(string, float)>(_v4.Breakdown(result))
        {
            ("dropThroughs", DropThroughTerm(result, _dropReward, _dropCap)),
        };
        return terms;
    }

    /// <summary>+reward × ΣDropThroughs over all players, capped, never negative.
    /// Shared with ffa-v2 (identical designer spec for both).</summary>
    internal static float DropThroughTerm(MatchResult result, float reward, float cap)
    {
        int total = 0;
        foreach (PlayerStats player in result.Players)
        {
            total += player.DropThroughs;
        }
        return MathF.Min(reward * total, cap);
    }
}
