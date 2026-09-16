using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v7 (2026-09-14, designer-directed; the projectile self-hit reward fix +
/// the scaled-time graduation): the ffa-v1 term generalization with
///   (a) every interaction input OPPONENT-ONLY — damage, farmPenalty, collisions,
///       damageFairness, and blocks read the self-inflicted split out of the stats
///       (a hitsSelf bolt clipping its own shooter earns and costs NOTHING; the
///       measured exploit: EVOLUTION 8 champions took 85-97% of all match damage
///       from their own bolts, scored as interaction by v3-v6);
///   (b) standard-v6's map-scaled time target as the LIVE time term (the designer
///       graduated the scaled variant to default in the same decision) — the
///       overtime cliff stays anchored to the UNSCALED max;
///   (c) the v4 self-destruct punishment and the v5 drop-through tiebreaker,
///       unchanged.
/// Every generalized term is exact at N = 2 (the ffa-v1 reductions), so ffa-v3 IS
/// this formula under its N-player name — both are built from ShippedTermLists.V7.
/// On a match with no self-inflicted interaction and a legacy-size stage, v7 scores
/// as v5 (regression-tested); v5/v6 remain frozen and selectable.
///
/// The default for NEW two-player runs; old checkpoints resume under their
/// recorded name.
/// </summary>
public sealed class StandardFitnessV7 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    private readonly FitnessComposer _composed;

    public StandardFitnessV7(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar)
    {
        _composed = new FitnessComposer("standard-v7", ShippedTermLists.V7(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
