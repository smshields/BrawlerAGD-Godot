using BrawlerSim.Replay;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerGodot;

public enum MatchMode
{
    HumanVsHuman,
    HumanVsCpu,
    AiVsAi,
    Replay,
}

/// <summary>
/// Hand-off between scenes: the menu configures it, the arena consumes it.
/// View-layer state only — nothing here can influence sim outcomes except through
/// the InputFrames the arena feeds to SimWorld.Tick (and the MatchConfig the arena
/// builds the world with — mode + duration are part of what the user launched).
/// </summary>
public static class MatchSession
{
    public static GameRecord? Game;
    public static MatchMode Mode = MatchMode.HumanVsHuman;
    public static ulong AiSeed = 7;
    public static InputTrace? Trace;

    /// <summary>Match-end rule (2026-08-12, four-player.md): STOCK (legacy default)
    /// or TIMED (infinite stocks, KO-ranked). Play-menu configurable.</summary>
    public static MatchEndRule EndRule = MatchEndRule.Stock;

    /// <summary>TIMED match duration (designer default: 2 minutes).</summary>
    public static float TimedMatchSeconds = 120f;

    public static MatchConfig BuildMatchConfig() => EndRule == MatchEndRule.Timed
        ? MatchConfig.Default with { EndRule = MatchEndRule.Timed, MaxMatchSeconds = TimedMatchSeconds }
        : MatchConfig.Default;
}
