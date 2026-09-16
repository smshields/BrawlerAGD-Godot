using BrawlerSim.Sim;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// The shared match battery behind the fitness characterization harness and the
/// per-term unit tests (2026-09-16, fitness term refactor). Every case is a
/// hand-built <see cref="MatchResult"/> chosen to exercise one branch of one term:
/// caps, saturations, null-list legacy fallbacks, the self-inflicted split, the
/// map-scale clamp ends, and the zero-tick divide guard.
///
/// These are FIXTURES, not goldens — the numbers in them are inputs. What is pinned
/// is the SCORE each shipped version produces from them (FitnessCharacterizationTests).
/// </summary>
internal static class FitnessFixtures
{
    /// <summary>A stock kit: four moves used unevenly, so moveMix is a live
    /// fraction rather than 0 or 1.</summary>
    private static readonly int[] KitA = { 12, 9, 4, 7 };
    private static readonly int[] KitB = { 8, 8, 8, 8 };
    private static readonly int[] KitStarved = { 14, 6, 0, 3 };

    internal static PlayerStats Player(
        float damage = 180f,
        int hits = 28,
        int stocks = 2,
        float[]? perStock = null,
        int[]? moves = null,
        int stunTicks = 90,
        int jumps = 14,
        int blocked = 3,
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
            RecoveryTicks: 40,
            DamagePerStock: perStock ?? new[] { 95f, 85f },
            MoveUses: moves ?? KitA,
            StunTicks: stunTicks,
            Jumps: jumps,
            ShieldActivations: 5,
            BlockedHits: blocked,
            ShieldBreaks: 1,
            ShieldTicks: 120,
            DashCount: 6,
            DashInvulnDodges: 2,
            FastFallTicks: 30,
            CrouchTicks: 22,
            DIInfluencedHits: 4,
            ProjectilesFired: 0,
            ProjectileHits: 0,
            ProjectilesReflected: 0,
            KOs: 1,
            DamageDealt: 150f,
            SelfDestructs: selfDestructs,
            DropThroughs: drops,
            SelfDamageTaken: selfDamage,
            SelfHitsReceived: selfHits,
            SelfBlockedHits: selfBlocked,
            ProjectileSelfHits: 0,
            SelfDamagePerStock: selfPerStock);

    /// <summary>A legacy-shaped player: no per-stock damage, no move uses — the
    /// null-list fallback path every counted-damage term carries.</summary>
    internal static PlayerStats LegacyPlayer(float damage = 210f, int hits = 31) =>
        new(TotalDamageTaken: damage, TotalHitsReceived: hits, RemainingStocks: 1, RecoveryTicks: 55);

    internal static MatchResult Match(
        PlayerStats[] players,
        float lengthSeconds = 45f,
        int? ticks = null,
        StageMetrics? stage = null) =>
        new(
            Players: players,
            LoserIndex: 0,
            Ticks: ticks ?? (int)(lengthSeconds * 60f),
            LengthSeconds: lengthSeconds,
            FinalHash: 0,
            Trace: null,
            Placements: null,
            Stage: stage);

    /// <summary>The two-player battery. Every shipped version scores all of these.</summary>
    internal static IReadOnlyList<(string Name, MatchResult Result)> TwoPlayer { get; } = new[]
    {
        ("healthy", Match(new[]
        {
            Player(),
            Player(damage: 165f, hits: 25, stocks: 3, perStock: new[] { 90f, 75f }, moves: KitB, stunTicks: 60, jumps: 11, blocked: 6),
        })),

        // Damage inside the punish window: counted in full, farmPenalty still 0.
        ("mid-damage", Match(new[]
        {
            Player(damage: 290f, perStock: new[] { 150f, 140f }),
            Player(damage: 260f, perStock: new[] { 130f, 130f }, moves: KitB),
        })),

        // One stock past punishStart (300) but under the cap (600).
        ("farm", Match(new[]
        {
            Player(damage: 620f, hits: 96, stocks: 1, perStock: new[] { 450f, 170f }),
            Player(damage: 180f, perStock: new[] { 95f, 85f }, moves: KitB),
        })),

        // A stock past the 600 cap: counted damage and the excess both saturate.
        ("farm-capped", Match(new[]
        {
            Player(damage: 940f, hits: 151, stocks: 0, perStock: new[] { 720f, 220f }),
            Player(damage: 175f, perStock: new[] { 90f, 85f }, moves: KitB),
        })),

        ("self-destructs", Match(new[]
        {
            Player(selfDestructs: 3),
            Player(moves: KitB, selfDestructs: 9),
        })),

        ("drop-throughs", Match(new[]
        {
            Player(drops: 2),
            Player(moves: KitB, drops: 7),
        })),

        // Past the overtime cliff (max 60 s in the harness's registry construction).
        ("overtime", Match(new[] { Player(), Player(moves: KitB) }, lengthSeconds: 65f)),

        // Well under target: the time term's other side.
        ("short", Match(new[] { Player(), Player(moves: KitB) }, lengthSeconds: 12f)),

        // Stun share above the 0.15 tolerance for both players.
        ("stun-locked", Match(new[]
        {
            Player(stunTicks: 1100),
            Player(moves: KitB, stunTicks: 780),
        })),

        // Shield-heavy: the blocks term carrying real weight.
        ("blocks", Match(new[]
        {
            Player(blocked: 22),
            Player(moves: KitB, blocked: 17),
        })),

        // A move never used: moveMix collapses to 0 for that player.
        ("starved-move", Match(new[]
        {
            Player(moves: KitStarved),
            Player(moves: KitB),
        })),

        // Jumps under the 40 saturation point, so the term is a live fraction.
        ("few-jumps", Match(new[]
        {
            Player(jumps: 4),
            Player(moves: KitB, jumps: 3),
        })),

        // Lopsided: damageFairness and stockFairness both bite.
        ("blowout", Match(new[]
        {
            Player(damage: 540f, hits: 88, stocks: 0, perStock: new[] { 280f, 260f }),
            Player(damage: 40f, hits: 7, stocks: 3, perStock: new[] { 40f }, moves: KitB),
        })),

        // Map-scale clamp, both ends (v6/v7 only read Stage; the rest ignore it).
        ("big-map", Match(new[] { Player(), Player(moves: KitB) }, stage: new StageMetrics(48f, 22f))),
        ("small-map", Match(new[] { Player(), Player(moves: KitB) }, stage: new StageMetrics(4f, 1.6f))),
        ("legacy-map", Match(new[] { Player(), Player(moves: KitB) },
            stage: new StageMetrics(BrawlerSim.Genome.StageRules.LegacyVisibleHalfWidth,
                                    BrawlerSim.Genome.StageRules.LegacyVisibleHalfHeight))),

        // The self-inflicted split (v7/ffa-v3 read it; v3-v6 are blind to it).
        ("self-inflicted", Match(new[]
        {
            Player(damage: 400f, hits: 60, perStock: new[] { 220f, 180f },
                   blocked: 9, selfDamage: 250f, selfHits: 34, selfBlocked: 5,
                   selfPerStock: new[] { 140f, 110f }),
            Player(moves: KitB),
        })),

        // Self damage exceeding the recorded total: the per-stock floor at 0.
        ("self-over", Match(new[]
        {
            Player(damage: 120f, hits: 10, perStock: new[] { 60f, 60f },
                   blocked: 2, selfDamage: 200f, selfHits: 25, selfBlocked: 6,
                   selfPerStock: new[] { 90f, 95f }),
            Player(moves: KitB),
        })),

        // No per-stock lists, no move uses: every null-fallback branch at once.
        ("legacy-stats", Match(new[] { LegacyPlayer(), LegacyPlayer(damage: 140f, hits: 19) })),

        // Zero ticks: the stun-share divide guard.
        ("zero-ticks", Match(new[] { Player(), Player(moves: KitB) }, lengthSeconds: 0f, ticks: 0)),
    };

    /// <summary>Three- and four-player cases — only the ffa versions score these
    /// (the 2P versions read exactly two players and the registry blocks them).</summary>
    internal static IReadOnlyList<(string Name, MatchResult Result)> MultiPlayer { get; } = new[]
    {
        ("3p-healthy", Match(new[]
        {
            Player(),
            Player(damage: 165f, stocks: 3, moves: KitB),
            Player(damage: 205f, stocks: 1, moves: KitStarved, jumps: 9),
        })),

        ("4p-healthy", Match(new[]
        {
            Player(),
            Player(damage: 165f, stocks: 3, moves: KitB),
            Player(damage: 205f, stocks: 1, moves: KitStarved, jumps: 9),
            Player(damage: 250f, stocks: 0, perStock: new[] { 130f, 120f }, moves: KitB, blocked: 11),
        })),

        ("4p-messy", Match(new[]
        {
            Player(damage: 700f, hits: 110, stocks: 0, perStock: new[] { 520f, 180f }, selfDestructs: 2, drops: 3),
            Player(damage: 165f, stocks: 3, moves: KitB, drops: 1),
            Player(damage: 205f, stocks: 1, moves: KitStarved, stunTicks: 900),
            Player(damage: 250f, stocks: 2, moves: KitB, selfDamage: 120f, selfHits: 15,
                   selfPerStock: new[] { 70f, 50f }),
        }, lengthSeconds: 78f, stage: new StageMetrics(30f, 14f))),
    };
}
