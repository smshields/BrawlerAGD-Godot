using BrawlerSim.Replay;

namespace BrawlerSim.Sim;

/// <summary>Per-player numbers the fitness function and research pipeline consume.
/// DamagePerStock (2026-07-10, standard-v3): damage taken within each life, in order —
/// entry i is the damage accumulated between spawn i and death i (or match end). Null
/// only in hand-built legacy fixtures; SimWorld always records it.
/// KOs/DamageDealt/SelfDestructs (2026-08-12, four-player.md): last-influencer KO
/// attribution — a death with no live enemy influence (hit or real push, cleared by
/// continuous grounding) is a SelfDestruct; standard-v4/ffa-v1 punish those. KOs and
/// DamageDealt also rank TIMED matches.</summary>
public sealed record PlayerStats(
    float TotalDamageTaken,
    int TotalHitsReceived,
    int RemainingStocks,
    int RecoveryTicks,
    IReadOnlyList<float>? DamagePerStock = null,
    IReadOnlyList<int>? MoveUses = null,
    int StunTicks = 0,
    int Jumps = 0,
    int ShieldActivations = 0,
    int BlockedHits = 0,
    int ShieldBreaks = 0,
    int ShieldTicks = 0,
    int DashCount = 0,
    int DashInvulnDodges = 0,
    int FastFallTicks = 0,
    int CrouchTicks = 0,
    int DIInfluencedHits = 0,
    int ProjectilesFired = 0,
    int ProjectileHits = 0,
    int ProjectilesReflected = 0,
    int KOs = 0,
    float DamageDealt = 0f,
    int SelfDestructs = 0);

/// <summary>
/// Outcome of one simulated match. LoserIndex is -1 for a 2P timeout draw (under
/// STOCK with 3-4 players it is the first eliminated player; under TIMED, last
/// place). FinalHash fingerprints the end state; two runs of the same
/// genome/seed/inputs must match it, and a replay of InputTrace must reproduce it
/// exactly. Placements (2026-08-12): 1-based total ranking per player — see
/// SimWorld.ComputePlacements for the STOCK/TIMED keys. Null only in hand-built
/// legacy fixtures.
/// </summary>
public sealed record MatchResult(
    IReadOnlyList<PlayerStats> Players,
    int LoserIndex,
    int Ticks,
    float LengthSeconds,
    ulong FinalHash,
    InputTrace? Trace,
    IReadOnlyList<int>? Placements = null);
