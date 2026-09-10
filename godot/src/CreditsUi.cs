using Godot;
using BrawlerSim.Backgrounds;

namespace BrawlerGodot;

/// <summary>
/// The shared art-credits section builders (backgrounds track Phase 1, 2026-09-02).
/// The backgrounds block is generated AT RUNTIME from the same bg-v1 index the
/// renderer uses (BackgroundCredits), so credits can never drift from shipped
/// content — attribution-bearing licenses (CC-BY / OGA-BY) make this a legal
/// requirement, not courtesy. Rendered by the dev main menu's CREDITS scene and the
/// packaged TitleView credits; the pause menu shows the single line for the current
/// match's backdrop instead.
/// </summary>
public static class CreditsUi
{
    /// <summary>The courtesy block (engine + sprite/tile/name sources). Corrected
    /// 2026-09-10 (designer attribution audit): the v2 STAGE TILES are DCSS-derived
    /// too — Kenney's 1-bit pack now covers only the legacy v1 sheets. The stated
    /// facts (all three v2 libraries CC0, DCSS-sourced) are pinned by
    /// CreditsAssumptionsTests so this text cannot silently drift.</summary>
    public const string CourtesyBlock =
        "Engine: Godot. Character, attack, and stage-tile sprites: Dungeon Crawl "
        + "Stone Soup tiles (CC0 — thank you to the DCSS artists; "
        + "github.com/crawl/tiles). Legacy 1-bit sprites: Kenney (kenney.nl). "
        + "Fighter and stage names: namegen.";

    /// <summary>Appends the backgrounds credits to a credits column: required
    /// attributions grouped by author, then the CC0 courtesy line.</summary>
    public static void AddBackgroundsSection(VBoxContainer text)
    {
        BackgroundCreditsModel model = BackgroundCredits.Build(BackgroundBank.Library);
        text.AddChild(UiWidgets.Heading("BACKGROUND ART", 18));
        foreach (BackgroundCreditsModel.AuthorCredits author in model.Required)
        {
            foreach (string attribution in author.Attributions)
            {
                text.AddChild(Line(attribution, 13));
            }
        }
        if (model.CourtesyAuthors.Count > 0)
        {
            text.AddChild(Line(
                "Public-domain and CC0 background art courtesy of "
                + string.Join(", ", model.CourtesyAuthors) + ".", 12));
        }
    }

    public static Label Line(string s, int size)
    {
        var label = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
