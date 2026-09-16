using BrawlerSim.Fitness.Terms;

namespace BrawlerSim.Fitness;

/// <summary>
/// The term list behind each shipped fitness version (2026-09-16, fitness term
/// refactor). One method per version, each reading as the formula it is, and each
/// taking exactly the constants its version's constructor has always exposed.
///
/// Two facts this file makes structural rather than incidental:
///
/// 1. THE WRAPPER CHAIN IS GONE. v4/v5/v6 and ffa-v2 shipped as decorators that held
///    an instance of the previous version and added one number to its result. They are
///    flat lists here. The scores are unchanged bit-for-bit because appending a term to
///    a left-to-right sum is the same float sequence as adding it to the inner sum's
///    result — pinned by FitnessCharacterizationTests.
///
/// 2. ffa-v1 IS standard-v4, AND ffa-v2 IS standard-v5. Every N-player generalization
///    (Σ over all players, spread for the fairness terms, jump saturation × N/2) is
///    EXACT at N = 2, which the suite already asserted end to end. They are literally
///    the same list here, under two names. The two-player versions keep their names
///    because run manifests record them and resume must reconstruct the recorded one.
///
/// EDGE CASE, deliberate: a 2P version handed a 3/4-player result now scores ALL
/// players, where the old hand-indexed terms silently read players 0 and 1 only.
/// FitnessRegistry rejects that combination before it can happen, and nothing
/// constructs a 2P version for an N-player match — but the difference is real, so it is
/// written down rather than discovered.
///
/// standard-v2 is NOT here. It predates term composition (it has no breakdown by
/// design), its damage cap is match-level rather than per-stock, and its collision
/// weight is an implicit 1.0 — expressing it would mint v2-only term classes that no
/// designer-built fitness would ever want. It stays frozen as its own class.
/// </summary>
internal static class ShippedTermLists
{
    /// <summary>The ten terms common to every version from v3 on, in their shipped
    /// order. <paramref name="opponentOnly"/> and <paramref name="scaledTime"/> are
    /// what used to separate the v3 family from the v7 family; they are parameters
    /// now, not different code.</summary>
    private static List<IFitnessTerm> Core(
        float target,
        float max,
        float damageScalar,
        float collisionScalar,
        bool opponentOnly,
        bool scaledTime,
        float punishStartDamage,
        float stockDamageCap,
        float punishSlope,
        float moveMixWeight,
        float stunLockWeight,
        float jumpWeight,
        float blockReward) => new()
    {
        new TimeTerm(target, max, scaleWithMap: scaledTime),
        new DamageTerm(stockDamageCap, damageScalar, opponentOnly),
        new FarmPenaltyTerm(punishStartDamage, stockDamageCap, punishSlope, opponentOnly),
        new CollisionsTerm(collisionScalar, opponentOnly),
        new DamageFairnessTerm(damageScalar, stockDamageCap, opponentOnly),
        new StockFairnessTerm(),
        new MoveMixTerm(moveMixWeight),
        new StunLockTerm(stunLockWeight),
        new JumpsTerm(jumpWeight),
        new BlocksTerm(blockReward, opponentOnly),
    };

    /// <summary>standard-v3: the ten shaping terms, everything counted, flat time target.</summary>
    internal static IFitnessTerm[] V3(
        float target, float max, float damageScalar, float collisionScalar,
        float punishStartDamage, float stockDamageCap, float punishSlope,
        float moveMixWeight, float stunLockWeight, float jumpWeight, float blockReward) =>
        Core(target, max, damageScalar, collisionScalar, opponentOnly: false, scaledTime: false,
            punishStartDamage, stockDamageCap, punishSlope,
            moveMixWeight, stunLockWeight, jumpWeight, blockReward).ToArray();

    /// <summary>standard-v4 == ffa-v1: v3 plus the self-destruct punishment.</summary>
    internal static IFitnessTerm[] V4(
        float target, float max, float damageScalar, float collisionScalar,
        float selfDestructPenalty, float selfDestructCap,
        float punishStartDamage, float stockDamageCap, float punishSlope,
        float moveMixWeight, float stunLockWeight, float jumpWeight, float blockReward)
    {
        List<IFitnessTerm> terms = Core(target, max, damageScalar, collisionScalar, false, false,
            punishStartDamage, stockDamageCap, punishSlope,
            moveMixWeight, stunLockWeight, jumpWeight, blockReward);
        terms.Add(new SelfDestructsTerm(selfDestructPenalty, selfDestructCap));
        return terms.ToArray();
    }

    /// <summary>standard-v5 == ffa-v2: v4 plus the drop-through tiebreaker.</summary>
    internal static IFitnessTerm[] V5(
        float target, float max, float damageScalar, float collisionScalar,
        float dropThroughReward, float dropThroughCap)
    {
        List<IFitnessTerm> terms = Core(target, max, damageScalar, collisionScalar, false, false,
            StandardFitnessV3.DefaultPunishStartDamage,
            StandardFitnessV3.DefaultStockDamageCap,
            StandardFitnessV3.DefaultPunishSlope,
            StandardFitnessV3.DefaultMoveMixWeight,
            StandardFitnessV3.DefaultStunLockWeight,
            StandardFitnessV3.DefaultJumpWeight,
            StandardFitnessV3.DefaultBlockReward);
        terms.Add(new SelfDestructsTerm(
            StandardFitnessV4.DefaultSelfDestructPenalty, StandardFitnessV4.DefaultSelfDestructCap));
        terms.Add(new DropThroughsTerm(dropThroughReward, dropThroughCap));
        return terms.ToArray();
    }

    /// <summary>standard-v6: v5 plus the map-scale CORRECTION term. The correction is
    /// how a decorator expressed "a different time term" — see TimeRescaleTerm.</summary>
    internal static IFitnessTerm[] V6(float target, float max, float damageScalar, float collisionScalar)
    {
        var terms = new List<IFitnessTerm>(V5(target, max, damageScalar, collisionScalar,
            StandardFitnessV5.DefaultDropThroughReward, StandardFitnessV5.DefaultDropThroughCap))
        {
            new TimeRescaleTerm(target),
        };
        return terms.ToArray();
    }

    /// <summary>standard-v7 == ffa-v3: opponent-only interaction inputs and the
    /// map-scaled time target as the LIVE term, so no correction is needed.</summary>
    internal static IFitnessTerm[] V7(float target, float max, float damageScalar, float collisionScalar)
    {
        List<IFitnessTerm> terms = Core(target, max, damageScalar, collisionScalar,
            opponentOnly: true, scaledTime: true,
            StandardFitnessV3.DefaultPunishStartDamage,
            StandardFitnessV3.DefaultStockDamageCap,
            StandardFitnessV3.DefaultPunishSlope,
            StandardFitnessV3.DefaultMoveMixWeight,
            StandardFitnessV3.DefaultStunLockWeight,
            StandardFitnessV3.DefaultJumpWeight,
            StandardFitnessV3.DefaultBlockReward);
        terms.Add(new SelfDestructsTerm(
            StandardFitnessV4.DefaultSelfDestructPenalty, StandardFitnessV4.DefaultSelfDestructCap));
        terms.Add(new DropThroughsTerm(
            StandardFitnessV5.DefaultDropThroughReward, StandardFitnessV5.DefaultDropThroughCap));
        return terms.ToArray();
    }
}
