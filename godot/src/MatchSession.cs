using BrawlerSim.Replay;
using BrawlerSim.Serialization;

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
/// the InputFrames the arena feeds to SimWorld.Tick.
/// </summary>
public static class MatchSession
{
    public static GameRecord? Game;
    public static MatchMode Mode = MatchMode.HumanVsHuman;
    public static ulong AiSeed = 7;
    public static InputTrace? Trace;
}
