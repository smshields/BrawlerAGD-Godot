using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// ffa-v1 (2026-08-12, designer-specified; docs/features/four-player.md): standard-v3's
/// terms generalized to N players — the default fitness for 3/4-player evolution runs.
/// Generalizations (each reduces to the v3 formula at N = 2):
///   damage / farmPenalty / collisions / moveMix / stunLock / blocks — summed over ALL
///     players instead of the fixed pair;
///   damageFairness — pairwise |d0 − d1| becomes the counted-damage SPREAD (max − min);
///   stockFairness — |s0 − s1| becomes the remaining-stock spread (max − min);
///   jumps — the saturation point scales with player count (40 per pair → 20/player),
///     so per-player jump expectations stay what v3 rewarded;
///   selfDestructs — the v4 punishment, identical spec: −1 per SD, capped at −4/match.
/// Because every generalization is exact at N = 2, ffa-v1 on a two-player match scores
/// identically to standard-v4 (regression-tested) — but N-player scores are NOT
/// comparable to any 2P run (different game, different instrument dynamics).
/// </summary>
public sealed class FfaFitnessV1 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    private readonly FitnessComposer _composed;

    public FfaFitnessV1(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float punishStartDamage = StandardFitnessV3.DefaultPunishStartDamage,
        float stockDamageCap = StandardFitnessV3.DefaultStockDamageCap,
        float punishSlope = StandardFitnessV3.DefaultPunishSlope,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float moveMixWeight = StandardFitnessV3.DefaultMoveMixWeight,
        float stunLockWeight = StandardFitnessV3.DefaultStunLockWeight,
        float jumpWeight = StandardFitnessV3.DefaultJumpWeight,
        float blockReward = StandardFitnessV3.DefaultBlockReward,
        float selfDestructPenalty = StandardFitnessV4.DefaultSelfDestructPenalty,
        float selfDestructCap = StandardFitnessV4.DefaultSelfDestructCap)
    {
        // Literally standard-v4's list: every generalization below is exact at N = 2,
        // which is why the two names share one term list (ShippedTermLists.V4).
        _composed = new FitnessComposer("ffa-v1", ShippedTermLists.V4(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar,
            selfDestructPenalty, selfDestructCap,
            punishStartDamage, stockDamageCap, punishSlope,
            moveMixWeight, stunLockWeight, jumpWeight, blockReward));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
