using BrawlerSim.Fitness.Terms;
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
public sealed class StandardFitnessV5 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    public const float DefaultDropThroughReward = 0.25f;
    public const float DefaultDropThroughCap = 1f;

    private readonly FitnessComposer _composed;

    public StandardFitnessV5(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float dropThroughReward = DefaultDropThroughReward,
        float dropThroughCap = DefaultDropThroughCap)
    {
        _composed = new FitnessComposer("standard-v5", ShippedTermLists.V5(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar,
            dropThroughReward, dropThroughCap));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
