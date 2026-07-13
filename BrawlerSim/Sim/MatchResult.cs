using BrawlerSim.Replay;

namespace BrawlerSim.Sim;

/// <summary>Per-player numbers the fitness function and research pipeline consume.
/// DamagePerStock (2026-07-10, standard-v3): damage taken within each life, in order —
/// entry i is the damage accumulated between spawn i and death i (or match end). Null
/// only in hand-built legacy fixtures; SimWorld always records it.</summary>
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
    int DIInfluencedHits = 0);

/// <summary>
/// Outcome of one simulated match. LoserIndex is -1 for a timeout draw. FinalHash
/// fingerprints the end state; two runs of the same genome/seed/inputs must match it,
/// and a replay of InputTrace must reproduce it exactly.
/// </summary>
public sealed record MatchResult(
    IReadOnlyList<PlayerStats> Players,
    int LoserIndex,
    int Ticks,
    float LengthSeconds,
    ulong FinalHash,
    InputTrace? Trace);
