using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Standalone packaged-game mode (2026-08-15, docs/features/packaged-games.md).
/// The presence of res://standalone_game.json — written by tools/package-game.sh,
/// never present in the dev project — IS the switch: it carries the one built game
/// the package contains, Boot routes to the title screen, and every back-to-menu
/// path returns there instead of the dev main menu. The embedded document is
/// pre-named and completeness-gated by `BrawlerRunner prep-game`.
/// </summary>
public static class Standalone
{
    private const string EmbeddedPath = "res://standalone_game.json";

    private static bool? _active;
    private static BuiltGame? _game;

    public static bool Active => _active ??= FileAccess.FileExists(EmbeddedPath);

    /// <summary>The packaged game (cached; standalone mode only).</summary>
    public static BuiltGame Game
    {
        get
        {
            if (_game is null)
            {
                using var file = FileAccess.Open(EmbeddedPath, FileAccess.ModeFlags.Read);
                _game = BuiltGameJson.Deserialize(file.GetAsText());
            }
            return _game;
        }
    }

    /// <summary>Where "back to the menu" goes: the packaged title screen in
    /// standalone mode, the dev main menu otherwise.</summary>
    public static string MenuScene() =>
        Active ? "res://scenes/title.tscn" : "res://scenes/main_menu.tscn";

    /// <summary>First-run defaults for packaged games (called once from Boot):
    /// the research debug strip starts OFF for players; their pause menu can
    /// still turn it on (the choice persists like any setting).</summary>
    public static void ApplyFirstRunDefaults()
    {
        var file = new ConfigFile();
        if (file.Load("user://settings.cfg") != Error.Ok)
        {
            AppSettings.DebugPanelEnabled = false;
        }
    }
}
