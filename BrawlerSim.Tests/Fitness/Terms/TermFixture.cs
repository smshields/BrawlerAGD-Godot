using BrawlerSim.Fitness.Terms;
using BrawlerSim.Genome;
using BrawlerSim.Sim;

namespace BrawlerSim.Tests.Fitness.Terms;

/// <summary>
/// Minimal inputs for the per-term unit tests (2026-09-16). Each term test builds a
/// player carrying ONLY the stat that term reads, so an expected value can be computed
/// by hand from the formula in the term's doc comment and nothing else can contaminate
/// it. The broad cross-term battery lives in FitnessFixtures.
/// </summary>
internal static class TermFixture
{
    /// <summary>A player with everything at zero — override exactly what a term reads.</summary>
    internal static PlayerStats P(
        float damage = 0f,
        int hits = 0,
        int stocks = 0,
        float[]? perStock = null,
        int[]? moves = null,
        int stunTicks = 0,
        int jumps = 0,
        int blocked = 0,
        int selfDestructs = 0,
        int drops = 0,
        float selfDamage = 0f,
        int selfHits = 0,
        int selfBlocked = 0,
        float[]? selfPerStock = null) =>
        new(
            TotalDamageTaken: damage,
            TotalHitsReceived: hits,
            RemainingStocks: stocks,
            RecoveryTicks: 0,
            DamagePerStock: perStock,
            MoveUses: moves,
            StunTicks: stunTicks,
            Jumps: jumps,
            BlockedHits: blocked,
            SelfDestructs: selfDestructs,
            DropThroughs: drops,
            SelfDamageTaken: selfDamage,
            SelfHitsReceived: selfHits,
            SelfBlockedHits: selfBlocked,
            SelfDamagePerStock: selfPerStock);

    internal static FitnessInput In(params PlayerStats[] players) =>
        In(45f, 2700, null, players);

    internal static FitnessInput In(int ticks, params PlayerStats[] players) =>
        In(ticks / 60f, ticks, null, players);

    internal static FitnessInput In(
        float lengthSeconds, int ticks, StageMetrics? stage, params PlayerStats[] players) =>
        new(new MatchResult(
            Players: players,
            LoserIndex: 0,
            Ticks: ticks,
            LengthSeconds: lengthSeconds,
            FinalHash: 0,
            Trace: null,
            Placements: null,
            Stage: stage));

    /// <summary>A stage whose half extents are the given MULTIPLES of the legacy map,
    /// so the map scale is sqrt(wx · hx) — e.g. (2, 2) gives exactly 2.</summary>
    internal static StageMetrics Stage(float widthMultiple, float heightMultiple) =>
        new(StageRules.LegacyVisibleHalfWidth * widthMultiple,
            StageRules.LegacyVisibleHalfHeight * heightMultiple);

    internal static StageMetrics LegacyStage => Stage(1f, 1f);
}
