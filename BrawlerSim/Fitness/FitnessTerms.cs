using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// The shared per-player term pieces used by StandardFitnessV3 (frozen there since
/// 2026-07-10) and FfaFitnessV1's N-player generalization — previously duplicated
/// verbatim in both classes. Moving the bodies here changes no formula: the frozen
/// versions stay frozen (identical float ops, pinned by the "ffa-v1 == v4 at N=2"
/// regression and the fitness golden values). Do not edit these in place — a new
/// research fitness is a new versioned class (FitnessRegistry).
/// </summary>
internal static class FitnessTerms
{
    /// <summary>Σ per-stock damage, each stock clipped at the cap. Falls back to the
    /// uncapped total for legacy fixtures without per-stock data.</summary>
    public static float CountedDamage(PlayerStats player, float cap)
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

    /// <summary>moveCount × minUse / totalUses ∈ [0,1]: 1 = perfectly even usage of
    /// every available move, 0 = some move never used (or no attacks at all). Legacy
    /// fixtures without MoveUses score 0 — the term is inert for them.</summary>
    public static float MoveEvenness(PlayerStats player)
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

    /// <summary>Stun share above the tolerance, 0 for healthy matches.</summary>
    public static float StunExcess(PlayerStats player, int ticks) =>
        ticks == 0
            ? 0f
            : MathF.Max(0f, player.StunTicks / (float)ticks - StandardFitnessV3.DefaultStunShareTolerance);

    /// <summary>Σ per-stock damage beyond the punishment threshold (each stock's excess
    /// saturates at cap − start).</summary>
    public static float Excess(PlayerStats player, float start, float cap)
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
