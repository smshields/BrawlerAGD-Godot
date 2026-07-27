using Godot;

namespace BrawlerGodot;

/// <summary>
/// Assigned per-player identity colors (HUD polish, 2026-07-23): the designer chose
/// the state-tint-AVOIDING set — rose / sky / gold / teal — so an identity color
/// never matches a body state tint (red attack, blue cooldown, green air, …).
/// Known soft collision: P3's gold sits near the projectile gold; P3/P4 are unused
/// until the sim grows past two players, so flagged rather than solved.
/// </summary>
public static class PlayerPalette
{
    private static readonly Color[] Colors =
    {
        new(0.96f, 0.42f, 0.55f), // P1 rose
        new(0.40f, 0.74f, 0.97f), // P2 sky
        new(0.97f, 0.78f, 0.33f), // P3 gold
        new(0.30f, 0.86f, 0.76f), // P4 teal
    };

    public static Color Of(int playerIndex) => Colors[playerIndex % Colors.Length];
}
