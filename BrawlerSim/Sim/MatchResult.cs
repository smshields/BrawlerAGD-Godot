using BrawlerSim.Replay;

namespace BrawlerSim.Sim;

/// <summary>Per-player numbers the fitness function and research pipeline consume.</summary>
public sealed record PlayerStats(
    float TotalDamageTaken,
    int TotalHitsReceived,
    int RemainingStocks,
    int RecoveryTicks);

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
