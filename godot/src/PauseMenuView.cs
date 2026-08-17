using Godot;

namespace BrawlerGodot;

/// <summary>
/// The pause menu (HUD polish, 2026-07-23 — replaces the old text-only overlay):
/// a real navigable menu — RESUME / DEBUG PANEL toggle / SETTINGS / QUIT TO MENU —
/// driven by mouse, keyboard (arrows + Enter/Space, W/S), or pad (d-pad + face
/// button via the built-in ui actions). The debug-panel toggle and the SETTINGS
/// popup (minimap options, shared with the main menu) persist via AppSettings.
/// ESC resumes; Q quits to the menu (legacy shortcut).
/// </summary>
public partial class PauseMenuView : CanvasLayer
{
    public System.Action? ResumeRequested;
    public System.Action? QuitRequested;

    private Control _root = null!;
    private Button _debugButton = null!;

    public override void _Ready()
    {
        _root = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f, Visible = false,
            Theme = UiTheme.Buttons, // app-wide button styling (2026-08-17)
        };
        AddChild(_root);

        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.02f, 0.04f, 0.6f),
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        _root.AddChild(dim);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        box.AddThemeConstantOverride("separation", 10);
        _root.AddChild(box);

        var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 8f) });

        AddButton(box, "RESUME", () => ResumeRequested?.Invoke());
        _debugButton = AddButton(box, DebugLabel(), () =>
        {
            AppSettings.DebugPanelEnabled = !AppSettings.DebugPanelEnabled;
            _debugButton.Text = DebugLabel();
        });
        AddButton(box, "SETTINGS", OpenSettings);
        AddButton(box, "QUIT TO MENU", () => QuitRequested?.Invoke());

        var hint = new Label
        {
            Text = "ESC resume · Q quit to menu",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.68f),
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(hint);
    }

    private static string DebugLabel() =>
        $"DEBUG PANEL: {(AppSettings.DebugPanelEnabled ? "ON" : "OFF")}";

    public bool IsOpen => _root.Visible;

    public void Open()
    {
        _debugButton.Text = DebugLabel();
        _root.Visible = true;
        // Keyboard/pad navigation starts on the first item.
        _firstButton?.GrabFocus();
    }

    public void Close()
    {
        _root.Visible = false;
    }

    private Button? _firstButton;

    private Button AddButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(320f, 42f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
        _firstButton ??= button;
        return button;
    }

    /// <summary>Same options as the main menu's SETTINGS popup (minimap), reachable
    /// mid-match per the designer's pause-menu decision.</summary>
    private void OpenSettings()
    {
        var popup = new PopupPanel { Theme = UiTheme.Buttons }; // popups don't inherit the scene theme
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380f, 0f) };
        box.AddThemeConstantOverride("separation", 10);
        popup.AddChild(box);

        var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);

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

        var close = new Button { Text = "CLOSE" };
        close.Pressed += () => popup.Hide();
        box.AddChild(close);

        popup.PopupHide += () => popup.QueueFree();
        _root.AddChild(popup);
        popup.PopupCentered();
    }
}
