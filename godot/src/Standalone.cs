using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Standalone packaged-game mode (2026-08-15, docs/features/packaged-games.md).
/// res://standalone_game.json — written by tools/package-game.sh — carries the one
/// built game a package contains; it is pre-named and completeness-gated by
/// `BrawlerRunner prep-game`. In EXPORTED builds its presence IS the switch: Boot
/// routes to the title screen and every back-to-menu path returns there, the dev
/// menus unreachable. In the EDITOR (2026-08-17, designer: dev testing happens in
/// the main game) a leftover dev copy of the file never hijacks boot — the dev
/// menu keeps all evolution tools and match-start paths, and standalone mode is
/// ENTERED per session via the title scene (main menu's TEST STANDALONE GAME, a
/// direct scene run, or BRAWLER_TITLE) and LEFT via the title's DEV MENU button.
/// </summary>
public static class Standalone
{
    private const string EmbeddedPath = "res://standalone_game.json";

    private static bool? _hasEmbeddedGame;
    private static BuiltGame? _game;

    /// <summary>Whether an embedded game document exists at all.</summary>
    public static bool HasEmbeddedGame => _hasEmbeddedGame ??= FileAccess.FileExists(EmbeddedPath);

    /// <summary>Whether the app is CURRENTLY in the packaged-game flow (title-screen
    /// navigation, dev menus hidden). Always true in an exported package; toggled by
    /// Enter/ExitToDevMenu when testing from the editor.</summary>
    public static bool Active { get; private set; }

    /// <summary>Engage standalone navigation (no-op without an embedded game).
    /// The title scene calls this so every way of reaching it — exported boot, the
    /// dev menu's test button, running the scene directly — behaves identically.</summary>
    public static void Enter() => Active = HasEmbeddedGame;

    /// <summary>Back to dev-mode navigation (editor testing only).</summary>
    public static void ExitToDevMenu() => Active = false;

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
