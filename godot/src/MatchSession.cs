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

    /// <summary>One match participant configured by the Game Player's character
    /// select (2026-08-14): a human on a numbered action set (p1 = keyboard,
    /// p2-p4 = the pads bound at join time) or a CPU with its leveled config.</summary>
    public sealed record PlayerSpec(bool Human, int PlayerNumber, BrawlerSim.Agents.AgentConfig? Agent);

    /// <summary>Non-null = the match was launched from the Game Player: ArenaView
    /// builds one source per spec (index = player index) instead of the quick-match
    /// modes. Cleared when the arena returns to the menu.</summary>
    public static System.Collections.Generic.List<PlayerSpec>? PlayerSpecs;
}
