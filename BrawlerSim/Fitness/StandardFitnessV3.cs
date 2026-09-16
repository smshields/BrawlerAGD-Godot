using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v3 (2026-07-10, designer-specified): replaces v2's match-level damage cap
/// with PER-STOCK damage shaping. Rationale: knockback grows with damage, so past
/// ~300 damage a hit should almost always kill (the Smash-Bros 200–300% intuition) —
/// a stock that absorbs more than that is evidence the game cannot kill, i.e. a "farm".
///
/// Per player, per life with damage d:
///   counted  = min(d, StockDamageCap)                    — damage past 600 counts for NOTHING
///   reward   = counted / damageScalar                    — replaces v2's raw-total reward
///   penalty  = −PunishSlope × max(0, counted − PunishStartDamage)
///              — punishment starts at 300, saturating at −300 per fully-farmed stock:
///                severe (≈ 3× a good match's total fitness) but BOUNDED, so stalling
///                longer cannot dig an ever-deeper hole (the unbounded v2 penalty was
///                the dominant fitness-noise amplifier — see the 2026-07-09 noise study).
/// Damage fairness also uses counted damage so farming can't distort it. Time, hit
/// count, and stock-fairness terms are v2-verbatim.
///
/// v2 remains frozen and selectable; run manifests record which version scored a run.
/// </summary>
public sealed class StandardFitnessV3 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    public const float OvertimePenalty = -35f;
    public const float DefaultPunishStartDamage = 300f;
    public const float DefaultStockDamageCap = 600f;
    public const float DefaultPunishSlope = 1f;

    /// <summary>
    /// Per-hit reward weight (2026-07-10 designer tuning, same-day pre-gate amendment
    /// of v3). At v2's implicit 1.0, a 191-hit farmed stock recouped 65% of its −300
    /// farm penalty through the collisions term, and healthy matches double-counted
    /// hits (collisions ≈ 1.5× the damage term, since a hit averages ~6.5 damage).
    /// 0.5 is the measured "even" point: in healthy champion rounds collisions ≈ the
    /// damage term (29≈34, 46≈52, 37≈38 across the tuning battery), and farm recoup
    /// falls to 32% (farmed GameC round: −148.6 → −245.6 net).
    /// </summary>
    public const float DefaultCollisionScalar = 0.5f;

    /// <summary>
    /// Weight of the move-mix evenness term (2026-07-10 second-move amendment):
    /// per player, weight × (moveCount × minUse / totalUses) — 1.0 when every move is
    /// used equally, 0 when any move is never used. Deliberately a NUDGE (max +10 vs
    /// the ±100 fitness scale): the designer wants even usage rewarded but NOT
    /// opinionated builds ruled out.
    /// </summary>
    public const float DefaultMoveMixWeight = 5f;

    /// <summary>
    /// Stun-lock penalty (2026-07-10 designer amendment: "stun locks cannot or at
    /// least very rarely exist"). Per player, punish the SHARE of the match spent
    /// stunned above a tolerance: −weight × max(0, stunShare − tolerance) × 100.
    /// Chains are what the per-hit cap cannot reach — this term prices them directly.
    /// At the defaults, a 26% stunned player costs −55, a 46% one −155.
    /// </summary>
    public const float DefaultStunLockWeight = 5f;
    public const float DefaultStunShareTolerance = 0.15f;

    /// <summary>
    /// Jump reward (same amendment: "reward games ... if there are a decent amount of
    /// jumps"). weight × min(totalJumps, saturation) / saturation — SATURATING, so
    /// jumping matters but jump-spam earns nothing extra. Max +10: nudge-scale.
    /// </summary>
    public const float DefaultJumpWeight = 10f;
    public const float DefaultJumpSaturation = 40f;

    /// <summary>
    /// Per-blocked-hit reward (2026-07-12 shield amendment). Rationale: a blocked hit
    /// SUPPRESSES ~1.15 fitness of rewarded interaction (the hit's ~0.65 damage term +
    /// 0.5 collision term), so under a blind fitness shields were selected AGAINST
    /// (measured: champions with 3 activations / 0 blocks). 2.0 per block makes a
    /// block a net-positive interaction event, comparable to landing a hit. Naturally
    /// bounded — each block costs shield health, so block counts can't run away.
    /// </summary>
    public const float DefaultBlockReward = 2f;

    private readonly FitnessComposer _composed;

    public StandardFitnessV3(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float punishStartDamage = DefaultPunishStartDamage,
        float stockDamageCap = DefaultStockDamageCap,
        float punishSlope = DefaultPunishSlope,
        float collisionScalar = DefaultCollisionScalar,
        float moveMixWeight = DefaultMoveMixWeight,
        float stunLockWeight = DefaultStunLockWeight,
        float jumpWeight = DefaultJumpWeight,
        float blockReward = DefaultBlockReward)
    {
        _composed = new FitnessComposer("standard-v3", ShippedTermLists.V3(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar,
            punishStartDamage, stockDamageCap, punishSlope,
            moveMixWeight, stunLockWeight, jumpWeight, blockReward));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    /// <summary>The assembled terms, for a builder UI or a diagnostic. 2026-09-16: the
    /// formula moved to ShippedTermLists.V3 as term objects — same floats, pinned.</summary>
    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
