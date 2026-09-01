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
            Color = UiPalette.OverlayDim,
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

        Label hint = UiWidgets.Hint("ESC resume · Q quit to menu");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
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
    private void OpenSettings() => SettingsPopup.Open(_root);
}
