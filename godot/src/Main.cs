using Godot;

namespace BrawlerGodot;

/// <summary>
/// Placeholder root node proving the Godot layer compiles against BrawlerSim.
/// Replaced by the real app shell (Evolve / Play / Manage) in Phase 4.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        GD.Print($"BrawlerSim core loaded: v{BrawlerSim.SimInfo.Version}, " +
                 $"{BrawlerSim.SimInfo.TicksPerSecond} ticks/s");
    }
}
