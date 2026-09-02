using Godot;
using BrawlerSim.Lighting;

namespace BrawlerGodot;

/// <summary>The light rig's tuning source (backgrounds Phase 4), mirroring the
/// other banks: lighting.json via FileAccess (pck-safe).</summary>
public static class LightBank
{
    private static LightingConfig? _config;

    public static LightingConfig Config => _config ??= LightingConfig.Parse(
        FileAccess.GetFileAsString("res://assets/lighting.json"));
}
