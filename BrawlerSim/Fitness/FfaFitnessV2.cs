using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// ffa-v2 (2026-09-01, designer-specified; docs/features/thin-platforms.md): ffa-v1's
/// EXACT terms plus the standard-v5 drop-through tiebreaker (+0.25 per crouch drop
/// summed over all players, capped at +1, never negative). Because ffa-v1 at N = 2
/// scores identically to standard-v4 and the added term is player-count-agnostic,
/// ffa-v2 at N = 2 scores identically to standard-v5 (regression-tested). The default
/// fitness for NEW 3/4-player runs; ffa-v1 remains frozen and selectable.
/// </summary>
public sealed class FfaFitnessV2 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    private readonly FitnessComposer _composed;

    public FfaFitnessV2(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float dropThroughReward = StandardFitnessV5.DefaultDropThroughReward,
        float dropThroughCap = StandardFitnessV5.DefaultDropThroughCap)
    {
        // Literally standard-v5's list — see FfaFitnessV1 on why the two names share one.
        _composed = new FitnessComposer("ffa-v2", ShippedTermLists.V5(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar,
            dropThroughReward, dropThroughCap));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
