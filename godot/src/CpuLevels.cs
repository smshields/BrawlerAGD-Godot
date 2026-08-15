using BrawlerSim.Agents;

namespace BrawlerGodot;

/// <summary>
/// CPU difficulty for the Game Player's character select (2026-08-14, sketch
/// "LEVEL: 1-10"; docs/features/game-player.md). A level maps to the utility
/// agent's two humanization dials — selection randomness and the decision
/// commitment window (response delay). PLAY-ONLY: the evolution instrument
/// (randomness 0.15, 8 ticks) is untouched and sits exactly at LEVEL 7 (designer),
/// with headroom above it. Piecewise-linear between the anchors.
/// </summary>
public static class CpuLevels
{
    public const int Min = 1;
    public const int Max = 10;

    /// <summary>Default new-pane level: the research instrument.</summary>
    public const int Default = 7;

    private static readonly (int Level, float Randomness, int IntervalTicks)[] Anchors =
    {
        (1, 0.50f, 30),  // sloppy: half the decisions are dice rolls, ~0.5 s reactions
        (7, 0.15f, 8),   // the fitness instrument, verbatim (AgentConfig.Default)
        (10, 0.02f, 4),  // near-frame-tight
    };

    public static AgentConfig Config(int level)
    {
        int clamped = System.Math.Clamp(level, Min, Max);
        for (int i = 1; i < Anchors.Length; i++)
        {
            if (clamped > Anchors[i].Level)
            {
                continue;
            }
            (int l0, float r0, int t0) = Anchors[i - 1];
            (int l1, float r1, int t1) = Anchors[i];
            float t = (clamped - l0) / (float)(l1 - l0);
            return AgentConfig.Default with
            {
                Kind = AgentKind.Utility,
                Randomness = r0 + (r1 - r0) * t,
                DecisionIntervalTicks = (int)System.MathF.Round(t0 + (t1 - t0) * t),
            };
        }
        (_, float r, int ticks) = Anchors[^1];
        return AgentConfig.Default with
        {
            Kind = AgentKind.Utility, Randomness = r, DecisionIntervalTicks = ticks,
        };
    }
}
