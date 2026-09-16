using BrawlerSim.Sim;

namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// The per-player pieces the terms are built from — counted damage, farm excess, move
/// evenness, stun share, and the opponent-only variants of each interaction input.
///
/// History: these bodies were duplicated verbatim in StandardFitnessV3 and
/// FfaFitnessV1 until the 2026-09-01 dedupe (as FitnessTerms), then gained the
/// opponent-side variants with the 2026-09-14 self-hit reward fix. Renamed to
/// PlayerAggregates on 2026-09-16 when "term" became a type name
/// (<see cref="IFitnessTerm"/>) — same bodies, no formula changed.
///
/// The <c>opponentOnly</c> overloads are pure dispatch to the two existing bodies:
/// what used to be the difference between the v3 and v7 FAMILIES is now one parameter
/// on one term.
/// </summary>
internal static class PlayerAggregates
{
    // ---------------------------------------------------------------- counted damage

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

    /// <summary>CountedDamage over opponent-inflicted damage only.</summary>
    public static float OpponentCountedDamage(PlayerStats player, float cap)
    {
        if (player.DamagePerStock is null)
        {
            return MathF.Max(0f, player.TotalDamageTaken - player.SelfDamageTaken);
        }
        float sum = 0f;
        for (int stock = 0; stock < player.DamagePerStock.Count; stock++)
        {
            sum += MathF.Min(OpponentStockDamage(player, stock), cap);
        }
        return sum;
    }

    public static float CountedDamage(PlayerStats player, float cap, bool opponentOnly) =>
        opponentOnly ? OpponentCountedDamage(player, cap) : CountedDamage(player, cap);

    // ------------------------------------------------------------------- farm excess

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

    /// <summary>Excess over opponent-inflicted damage only.</summary>
    public static float OpponentExcess(PlayerStats player, float start, float cap)
    {
        if (player.DamagePerStock is null)
        {
            float opponentTotal = MathF.Max(0f, player.TotalDamageTaken - player.SelfDamageTaken);
            return MathF.Max(0f, MathF.Min(opponentTotal, cap) - start);
        }
        float sum = 0f;
        for (int stock = 0; stock < player.DamagePerStock.Count; stock++)
        {
            sum += MathF.Max(0f, MathF.Min(OpponentStockDamage(player, stock), cap) - start);
        }
        return sum;
    }

    public static float Excess(PlayerStats player, float start, float cap, bool opponentOnly) =>
        opponentOnly ? OpponentExcess(player, start, cap) : Excess(player, start, cap);

    // -------------------------------------------------------------------- interaction

    /// <summary>Hits received from OPPONENTS (self-hits excluded).</summary>
    public static int OpponentHitsReceived(PlayerStats player) =>
        Math.Max(0, player.TotalHitsReceived - player.SelfHitsReceived);

    public static int HitsReceived(PlayerStats player, bool opponentOnly) =>
        opponentOnly ? OpponentHitsReceived(player) : player.TotalHitsReceived;

    /// <summary>Blocks of OPPONENT hits (blocking your own bolt earns nothing).</summary>
    public static int OpponentBlockedHits(PlayerStats player) =>
        Math.Max(0, player.BlockedHits - player.SelfBlockedHits);

    public static int BlockedHits(PlayerStats player, bool opponentOnly) =>
        opponentOnly ? OpponentBlockedHits(player) : player.BlockedHits;

    // ------------------------------------------------------------------------ shaping

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

    /// <summary>Stun share above the tolerance, 0 for healthy matches. The tolerance
    /// was a const read from inside this helper until 2026-09-16 — it is now the
    /// caller's parameter (StunLockTerm), which is why it is the one knob in here.</summary>
    public static float StunExcess(PlayerStats player, int ticks, float tolerance) =>
        ticks == 0
            ? 0f
            : MathF.Max(0f, player.StunTicks / (float)ticks - tolerance);

    // ------------------------------------------------------------------------ private

    /// <summary>Per-stock OPPONENT damage: DamagePerStock minus the index-parallel
    /// SelfDamagePerStock, floored at 0 per stock.</summary>
    private static float OpponentStockDamage(PlayerStats player, int stock)
    {
        float damage = player.DamagePerStock![stock];
        if (player.SelfDamagePerStock is not null && stock < player.SelfDamagePerStock.Count)
        {
            damage -= player.SelfDamagePerStock[stock];
        }
        return MathF.Max(0f, damage);
    }
}
