using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>
/// Geometry predicates shared VERBATIM by the UtilityAgent and the archived
/// DecisionTreeAgent. Both agents are fitness instruments — any change here
/// changes what fitness measures (CHANGE_LOG.md) and moves the agent goldens.
/// </summary>
internal static class AgentGeometry
{
    /// <summary>No platform anywhere below the sample point (the DT's original
    /// pit test, ported unchanged into the utility agent).</summary>
    public static bool OverPit(SimWorld world, SimPlayer self, float xOffset)
    {
        float x = self.Position.X + xOffset;
        foreach (Aabb platform in world.Platforms)
        {
            if (x >= platform.Left && x <= platform.Right && platform.Top <= self.Position.Y)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The platform sense box: Unity's fixed 20×15 extents, scaled up with
    /// map size (2026-07-21, CHANGE_LOG #27) — exactly the legacy box on legacy maps.</summary>
    public static Aabb SenseBox(SimWorld world, SimPlayer self) =>
        new(self.Position, world.PlatformSenseHalf);
}
