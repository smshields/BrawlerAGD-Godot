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
    int Jumps = 0);

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
