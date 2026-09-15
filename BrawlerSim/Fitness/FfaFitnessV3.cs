using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// ffa-v3 (2026-09-14, designer-directed): the N-player name for the standard-v7
/// formula — opponent-only interaction inputs (the projectile self-hit reward fix)
/// plus the map-scaled time target, over ffa-v2's term structure. Built from the
/// same SelfBlindTerms.Build as standard-v7, so the two are identical BY
/// CONSTRUCTION at every player count; the split name keeps the registry's
/// 2P-default / N-player-default convention. The default fitness for NEW
/// 3/4-player runs; ffa-v2 remains frozen and selectable.
/// </summary>
public sealed class FfaFitnessV3 : IFitnessFunction, IFitnessBreakdown
{
    private readonly ComposedFitness _composed;

    public FfaFitnessV3(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar)
    {
        _composed = new ComposedFitness("ffa-v3", SelfBlindTerms.Build(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);
}
