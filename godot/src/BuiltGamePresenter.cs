using Godot;
using BrawlerSim.Serialization;
using NG = NameGen;

namespace BrawlerGodot;

/// <summary>
/// The presentation pass over a built game, app side (was BuiltGameNamer — Game Player
/// item 6, 2026-08-14; widened by sprite selection 2026-08-22, sprite-selection.md).
/// Runs when a game is OPENED for play: every element that still needs a name gets one
/// AND every character that lacks a sprite gets one, negotiated together on a shared
/// content-derived seed and register, then the doc is PERSISTED once. Manual renames
/// and previously settled presentations are left alone. The pass itself is engine-side
/// (BuiltGamePresentation — shared with BrawlerRunner prep-game); this class only
/// supplies the app's generator/selector and the save.
/// </summary>
public static class BuiltGamePresenter
{
    private static NG.NameGenerator? _generator;

    private static NG.NameGenerator Generator => _generator ??= NG.NameGenerator.CreateDefault();

    /// <summary>Presents what needs presenting; saves and returns true when anything
    /// changed. A null path (embedded standalone game) never saves — prep-game
    /// already presented it at packaging time.</summary>
    public static bool EnsurePresented(BuiltGame game, string? path)
    {
        int changed = BuiltGamePresentation.EnsurePresented(game, Generator, SpriteBank.Selector);
        if (changed > 0 && path is not null)
        {
            BuiltGameJson.Save(game, path);
        }
        return changed > 0;
    }
}
