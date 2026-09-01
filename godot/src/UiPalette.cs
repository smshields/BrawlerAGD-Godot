using Godot;

namespace BrawlerGodot;

/// <summary>
/// The recurring UI colors, named once. Only values that repeat EXACTLY across
/// files live here — near-miss backgrounds that drifted per-screen (e.g.
/// FitnessChart/TitleView 0.05/0.05/0.08, StageThumb 0.07/0.07/0.1) stay as
/// local literals so this file never silently re-tints a screen.
/// </summary>
public static class UiPalette
{
    /// <summary>Section-heading grey (labels above lists, readouts).</summary>
    public static readonly Color Heading = new Color(0.65f, 0.7f, 0.78f);

    /// <summary>Dimmer hint grey (footnotes, control hints, empty-state text).</summary>
    public static readonly Color Hint = new Color(0.55f, 0.6f, 0.68f);

    /// <summary>Full-screen dim behind overlays (settings popup, pause menu).</summary>
    public static readonly Color OverlayDim = new Color(0.02f, 0.02f, 0.04f, 0.6f);

    /// <summary>Solid panel background (HUD slots, builder cards, joined panes).</summary>
    public static readonly Color PanelBg = new Color(0.13f, 0.13f, 0.17f);

    /// <summary>Neutral panel/card border.</summary>
    public static readonly Color PanelBorder = new Color(0.3f, 0.32f, 0.4f);

    /// <summary>Slightly darker card background (stage cards, empty grid cells).</summary>
    public static readonly Color CardBg = new Color(0.11f, 0.11f, 0.15f);

    /// <summary>The game's dark background (the aesthetics-rule 0.09/0.09/0.12).</summary>
    public static readonly Color Background = new Color(0.09f, 0.09f, 0.12f);
}
