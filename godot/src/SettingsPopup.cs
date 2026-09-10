using Godot;

namespace BrawlerGodot;

/// <summary>
/// The SETTINGS popup (2026-07-21, Map Size): the minimap options — enabled,
/// corner, size, transparency — persisted via AppSettings (user://settings.cfg).
/// Shared verbatim by the main menu and the pause menu (the pause menu reaches it
/// mid-match per the designer's pause-menu decision). TitleView's reduced
/// standalone variant (minimap + debug toggle only, different metrics) is a
/// deliberately different popup and stays in TitleView.
/// </summary>
public static class SettingsPopup
{
    public static void Open(Node parent)
    {
        var popup = new PopupPanel { Theme = UiTheme.Buttons }; // popups don't inherit the scene theme
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380f, 0f) };
        box.AddThemeConstantOverride("separation", 10);
        popup.AddChild(box);

        var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);

        AddContent(box);

        var close = new Button { Text = "CLOSE" };
        close.Pressed += () => popup.Hide();
        box.AddChild(close);

        popup.PopupHide += () => popup.QueueFree();
        parent.AddChild(popup);
        popup.PopupCentered();
    }

    /// <summary>The settings controls without popup chrome — shared between the
    /// pause-menu popup above and the main menu's SETTINGS flyout (2026-09-10).</summary>
    public static void AddContent(VBoxContainer box)
    {
        var enabled = new CheckButton { Text = "MINIMAP", ButtonPressed = AppSettings.MinimapEnabled };
        enabled.Toggled += on => AppSettings.MinimapEnabled = on;
        box.AddChild(enabled);

        box.AddChild(new Label { Text = "MINIMAP CORNER" });
        var corner = new OptionButton();
        foreach (string name in new[] { "UPPER LEFT", "UPPER RIGHT", "LOWER LEFT", "LOWER RIGHT" })
        {
            corner.AddItem(name);
        }
        corner.Selected = (int)AppSettings.MinimapCorner;
        corner.ItemSelected += index => AppSettings.MinimapCorner = (AppSettings.Corner)index;
        box.AddChild(corner);

        box.AddChild(new Label { Text = "MINIMAP SIZE" });
        var size = new HSlider { MinValue = 0.1, MaxValue = 0.4, Step = 0.01, Value = AppSettings.MinimapSize };
        size.ValueChanged += value => AppSettings.MinimapSize = (float)value;
        box.AddChild(size);

        box.AddChild(new Label { Text = "MINIMAP OPACITY" });
        var opacity = new HSlider { MinValue = 0.1, MaxValue = 1.0, Step = 0.05, Value = AppSettings.MinimapOpacity };
        opacity.ValueChanged += value => AppSettings.MinimapOpacity = (float)value;
        box.AddChild(opacity);
    }
}
