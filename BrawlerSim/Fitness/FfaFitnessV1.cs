using BrawlerSim.Determinism;
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
public sealed class FfaFitnessV1 : IFitnessFunction
{
    private readonly ComposedFitness _composed;

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
        _composed = new ComposedFitness("ffa-v1", new ComposedFitness.Term[]
        {
            new("time", r =>
                -DetMath.Abs(targetLengthSeconds - r.LengthSeconds)
                + (r.LengthSeconds >= maxLengthSeconds ? StandardFitnessV3.OvertimePenalty : 0f)),
            new("damage", r => SumOverPlayers(r, p => CountedDamage(p, stockDamageCap)) / damageScalar),
            new("farmPenalty", r =>
                -punishSlope * SumOverPlayers(r, p => Excess(p, punishStartDamage, stockDamageCap))),
            new("collisions", r => collisionScalar * SumOverPlayers(r, p => p.TotalHitsReceived)),
            new("damageFairness", r =>
                -Spread(r, p => CountedDamage(p, stockDamageCap)) / damageScalar),
            new("stockFairness", r => 3f - Spread(r, p => p.RemainingStocks)),
            new("moveMix", r => moveMixWeight * SumOverPlayers(r, MoveEvenness)),
            new("stunLock", r =>
                -stunLockWeight * 100f * SumOverPlayers(r, p => StunExcess(p, r.Ticks))),
            new("jumps", r =>
            {
                float saturation = StandardFitnessV3.DefaultJumpSaturation * r.Players.Count / 2f;
                return jumpWeight * MathF.Min(SumOverPlayers(r, p => p.Jumps), saturation) / saturation;
            }),
            new("blocks", r => blockReward * SumOverPlayers(r, p => p.BlockedHits)),
            new("selfDestructs", r =>
                StandardFitnessV4.SelfDestructTerm(r, selfDestructPenalty, selfDestructCap)),
        });
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    private static float SumOverPlayers(MatchResult result, Func<PlayerStats, float> value)
    {
        float sum = 0f;
        foreach (PlayerStats player in result.Players)
        {
            sum += value(player);
        }
        return sum;
    }

    /// <summary>max − min over players — the N-player fairness generalization
    /// (identical to |a − b| for two players).</summary>
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

    // The per-player pieces below are v3-verbatim (kept private there — duplicated
    // rather than shared so the frozen class stays untouchable).

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

    private static float MoveEvenness(PlayerStats player)
    {
        if (player.MoveUses is null || player.MoveUses.Count == 0)
        {
            return 0f;
        }
        int total = 0, min = int.MaxValue;
        foreach (int uses in player.MoveUses)
        {
            total += uses;
            min = Math.Min(min, uses);
        }
        return total == 0 ? 0f : player.MoveUses.Count * min / (float)total;
    }

    private static float StunExcess(PlayerStats player, int ticks) =>
        ticks == 0
            ? 0f
            : MathF.Max(0f, player.StunTicks / (float)ticks - StandardFitnessV3.DefaultStunShareTolerance);

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
