using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

using static FitnessTerms;

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
/// this formula under its N-player name — both are built from SelfBlindTerms.Build.
/// On a match with no self-inflicted interaction and a legacy-size stage, v7 scores
/// as v5 (regression-tested); v5/v6 remain frozen and selectable.
///
/// The default for NEW two-player runs; old checkpoints resume under their
/// recorded name.
/// </summary>
public sealed class StandardFitnessV7 : IFitnessFunction, IFitnessBreakdown
{
    private readonly ComposedFitness _composed;

    public StandardFitnessV7(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar)
    {
        _composed = new ComposedFitness("standard-v7", SelfBlindTerms.Build(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);
}

/// <summary>The ONE term list behind standard-v7 and ffa-v3 (identical formula,
/// two registry names — the 2P default and the N-player default). Structure is
/// ffa-v1's N-player generalization; inputs are the opponent-only variants; the
/// time term is v6's map-scaled target.</summary>
internal static class SelfBlindTerms
{
    internal static ComposedFitness.Term[] Build(
        float targetLengthSeconds,
        float maxLengthSeconds,
        float damageScalar,
        float collisionScalar,
        float punishStartDamage = StandardFitnessV3.DefaultPunishStartDamage,
        float stockDamageCap = StandardFitnessV3.DefaultStockDamageCap,
        float punishSlope = StandardFitnessV3.DefaultPunishSlope,
        float moveMixWeight = StandardFitnessV3.DefaultMoveMixWeight,
        float stunLockWeight = StandardFitnessV3.DefaultStunLockWeight,
        float jumpWeight = StandardFitnessV3.DefaultJumpWeight,
        float blockReward = StandardFitnessV3.DefaultBlockReward,
        float selfDestructPenalty = StandardFitnessV4.DefaultSelfDestructPenalty,
        float selfDestructCap = StandardFitnessV4.DefaultSelfDestructCap,
        float dropThroughReward = StandardFitnessV5.DefaultDropThroughReward,
        float dropThroughCap = StandardFitnessV5.DefaultDropThroughCap) => new ComposedFitness.Term[]
    {
        // v6's scaled target as the live term: −|s·target − length|, overtime cliff
        // at the UNSCALED max (it prices the real match timeout).
        new("time", r =>
            -DetMath.Abs(StandardFitnessV6.TimeScale(r.Stage) * targetLengthSeconds - r.LengthSeconds)
            + (r.LengthSeconds >= maxLengthSeconds ? StandardFitnessV3.OvertimePenalty : 0f)),
        new("damage", r => Sum(r, p => OpponentCountedDamage(p, stockDamageCap)) / damageScalar),
        new("farmPenalty", r =>
            -punishSlope * Sum(r, p => OpponentExcess(p, punishStartDamage, stockDamageCap))),
        new("collisions", r => collisionScalar * Sum(r, p => OpponentHitsReceived(p))),
        new("damageFairness", r =>
            -Spread(r, p => OpponentCountedDamage(p, stockDamageCap)) / damageScalar),
        new("stockFairness", r => 3f - Spread(r, p => p.RemainingStocks)),
        new("moveMix", r => moveMixWeight * Sum(r, MoveEvenness)),
        new("stunLock", r =>
            -stunLockWeight * 100f * Sum(r, p => StunExcess(p, r.Ticks))),
        new("jumps", r =>
        {
            float saturation = StandardFitnessV3.DefaultJumpSaturation * r.Players.Count / 2f;
            return jumpWeight * MathF.Min(Sum(r, p => p.Jumps), saturation) / saturation;
        }),
        new("blocks", r => blockReward * Sum(r, p => OpponentBlockedHits(p))),
        new("selfDestructs", r =>
            StandardFitnessV4.SelfDestructTerm(r, selfDestructPenalty, selfDestructCap)),
        new("dropThroughs", r =>
            StandardFitnessV5.DropThroughTerm(r, dropThroughReward, dropThroughCap)),
    };

    private static float Sum(MatchResult result, Func<PlayerStats, float> value)
    {
        float sum = 0f;
        foreach (PlayerStats player in result.Players)
        {
            sum += value(player);
        }
        return sum;
    }

    /// <summary>max − min over players (== |a − b| at N = 2, the ffa-v1 reduction).</summary>
    private static float Spread(MatchResult result, Func<PlayerStats, float> value)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (PlayerStats player in result.Players)
        {
            float v = value(player);
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
        }
        return max - min;
    }
}
