using Godot;
using BrawlerSim.Sprites;

namespace BrawlerGodot;

/// <summary>
/// Stage tile theme sources (M4d, 2026-09-01 — docs/features/stage-tile-selection.md):
/// the tiles_v2 atlas + library + the app's shared theme selector, mirroring
/// SpriteBank. Themed stages (ThemeId gene / presented theme) render through this;
/// null-gene (pre-v13) stages keep the v1 Kenney path in StageView.
/// </summary>
public static class ThemeBank
{
    private static StageThemeLibrary? _library;
    private static StageThemeSelector? _selector;
    private static Texture2D? _tiles;

    /// <summary>The v2 theme library (rects + tags), FileAccess-loaded so it works
    /// inside exported .pck files too.</summary>
    public static StageThemeLibrary Library =>
        _library ??= StageThemeLibrary.Parse(FileAccess.GetFileAsString("res://assets/tiles_v2_slices.json"));

    /// <summary>The app's shared selector, on the shipped hot-editable tuning file
    /// (tile_selection.json — sibling of sprite_selection.json, designer 2026-09-01).</summary>
    public static StageThemeSelector Selector =>
        _selector ??= new StageThemeSelector(Library,
            StageThemeConfig.Parse(FileAccess.GetFileAsString("res://assets/tile_selection.json")));

    /// <summary>The tiles_v2 atlas texture.</summary>
    public static Texture2D Tiles => _tiles ??= GD.Load<Texture2D>("res://assets/tiles_v2.png");
}
