using BrawlerSim.Determinism;
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
public sealed class StandardFitnessV3 : IFitnessFunction
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

    private readonly ComposedFitness _composed;

    public StandardFitnessV3(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float punishStartDamage = DefaultPunishStartDamage,
        float stockDamageCap = DefaultStockDamageCap,
        float punishSlope = DefaultPunishSlope,
        float collisionScalar = DefaultCollisionScalar)
    {
        _composed = new ComposedFitness("standard-v3", new ComposedFitness.Term[]
        {
            new("time", r =>
                -DetMath.Abs(targetLengthSeconds - r.LengthSeconds)
                + (r.LengthSeconds >= maxLengthSeconds ? OvertimePenalty : 0f)),
            new("damage", r =>
                (CountedDamage(r.Players[0], stockDamageCap) + CountedDamage(r.Players[1], stockDamageCap))
                / damageScalar),
            new("farmPenalty", r =>
                -punishSlope * (Excess(r.Players[0], punishStartDamage, stockDamageCap)
                              + Excess(r.Players[1], punishStartDamage, stockDamageCap))),
            new("collisions", r =>
                collisionScalar * (r.Players[0].TotalHitsReceived + r.Players[1].TotalHitsReceived)),
            new("damageFairness", r =>
                -DetMath.Abs(CountedDamage(r.Players[0], stockDamageCap)
                           - CountedDamage(r.Players[1], stockDamageCap)) / damageScalar),
            new("stockFairness", r =>
                3f - DetMath.Abs(r.Players[0].RemainingStocks - r.Players[1].RemainingStocks)),
        });
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    /// <summary>Σ per-stock damage, each stock clipped at the cap. Falls back to the
    /// uncapped total for legacy fixtures without per-stock data.</summary>
    private static float CountedDamage(PlayerStats player, float cap)
    {
        if (player.DamagePerStock is null)
        {
            return player.TotalDamageTaken;
        }
        float sum = 0f;
        foreach (float d in player.DamagePerStock)
        {
            sum += MathF.Min(d, cap);
        }
        return sum;
    }

    /// <summary>Σ per-stock damage beyond the punishment threshold (each stock's excess
    /// saturates at cap − start).</summary>
    private static float Excess(PlayerStats player, float start, float cap)
    {
        if (player.DamagePerStock is null)
        {
            return MathF.Max(0f, MathF.Min(player.TotalDamageTaken, cap) - start);
        }
        float sum = 0f;
        foreach (float d in player.DamagePerStock)
        {
            sum += MathF.Max(0f, MathF.Min(d, cap) - start);
        }
        return sum;
    }
}
