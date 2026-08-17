using Godot;

namespace BrawlerGodot;

/// <summary>
/// Shared UI styling (2026-08-17, designer: the character select's button styling
/// applies APP-WIDE). Every screen sets <see cref="Buttons"/> as its root theme so
/// each Button — and Button-derived widgets like OptionButton — inherits bordered
/// dark boxes with hover/pressed/disabled states. Popup windows do not inherit a
/// scene control's theme automatically, so popups set it explicitly too.
/// </summary>
public static class UiTheme
{
    private static Theme? _buttons;

    /// <summary>Bordered dark boxes with hover/pressed/disabled states (moved out
    /// of CharacterSelectView, where the style was born).</summary>
    public static Theme Buttons => _buttons ??= Build();

    private static Theme Build()
    {
        var theme = new Theme();
        theme.SetStylebox("normal", "Button", Box(new Color(0.16f, 0.17f, 0.22f), new Color(0.38f, 0.4f, 0.48f)));
        theme.SetStylebox("hover", "Button", Box(new Color(0.21f, 0.22f, 0.28f), new Color(0.58f, 0.61f, 0.7f)));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.28f, 0.29f, 0.36f), Colors.White));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.1f, 0.1f, 0.13f), new Color(0.2f, 0.22f, 0.28f)));
        theme.SetColor("font_color", "Button", new Color(0.92f, 0.93f, 0.96f));
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", new Color(0.4f, 0.42f, 0.5f));
        return theme;
    }

    private static StyleBoxFlat Box(Color bg, Color border, int corner = 6, float marginX = 12f, float marginY = 5f) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = corner, CornerRadiusTopRight = corner,
        CornerRadiusBottomLeft = corner, CornerRadiusBottomRight = corner,
        ContentMarginLeft = marginX, ContentMarginRight = marginX,
        ContentMarginTop = marginY, ContentMarginBottom = marginY,
    };

    /// <summary>Dense grids (the rename keyboard) are too tight for the themed
    /// margins: same palette, 2 px margins, small corners.</summary>
    public static void CompactKey(Button key)
    {
        key.AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.17f, 0.22f), new Color(0.38f, 0.4f, 0.48f), corner: 3, marginX: 2f, marginY: 2f));
        key.AddThemeStyleboxOverride("hover", Box(new Color(0.21f, 0.22f, 0.28f), new Color(0.58f, 0.61f, 0.7f), corner: 3, marginX: 2f, marginY: 2f));
        key.AddThemeStyleboxOverride("pressed", Box(new Color(0.28f, 0.29f, 0.36f), Colors.White, corner: 3, marginX: 2f, marginY: 2f));
    }
}
