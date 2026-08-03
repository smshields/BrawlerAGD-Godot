using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// App shell: Play (2P / vs CPU), Watch AI, Watch Replay (file dialogs for any
/// game.json), plus the Evolve dashboard and the Manage library browser.
/// </summary>
public partial class MainMenu : Control
{
    private FileDialog _gameDialog = null!;
    private FileDialog _traceDialog = null!;
    private MatchMode _pendingMode;
    private Label _hint = null!;
    private Button _twoPlayerButton = null!;

    public override void _Ready()
    {
        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var title = new Label { Text = "BRAWLER AGD", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 52);
        box.AddChild(title);
        var subtitle = new Label
        {
            Text = "automated brawler game designer — godot edition",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.65f, 0.7f, 0.78f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(subtitle);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        _twoPlayerButton = AddButton(box, "PLAY — 2 PLAYERS", () => PickGame(MatchMode.HumanVsHuman));
        AddButton(box, "PLAY — VS CPU", () => PickGame(MatchMode.HumanVsCpu));
        AddButton(box, "WATCH AI MATCH", () => PickGame(MatchMode.AiVsAi));
        AddButton(box, "WATCH REPLAY", () => PickGame(MatchMode.Replay));
        AddButton(box, "EVOLVE", () => GetTree().ChangeSceneToFile("res://scenes/evolve.tscn"));
        AddButton(box, "MANAGE GAMES", () => GetTree().ChangeSceneToFile("res://scenes/manage.tscn"));
        AddButton(box, "SETTINGS", OpenSettings);
        AddButton(box, "QUIT", () => GetTree().Quit());

        _hint = new Label
        {
            Text = "P1: A/D move · SPACE jump · I/J/K/L attacks        P2: gamepad\n" +
                   "gamepad: stick/dpad move · Y/B jump · X/A/L1/R1 attacks",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.68f),
            AnchorTop = 1f, AnchorBottom = 1f, AnchorRight = 1f,
            OffsetTop = -64f,
        };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_hint);

        _gameDialog = MakeDialog("Choose a game.json", OnGamePicked);
        _traceDialog = MakeDialog("Choose the matching trace.json", OnTracePicked);

        // 2-player needs a controller (the keyboard is entirely P1's now).
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        UpdateTwoPlayerAvailability();

        // Automation: BRAWLER_PICKER=1 opens the game picker on load (screenshots).
        if (OS.GetEnvironment("BRAWLER_PICKER") == "1")
        {
            CallDeferred(nameof(OpenPickerForAutomation));
        }
    }

    private void OpenPickerForAutomation() => PickGame(MatchMode.AiVsAi);

    public override void _ExitTree()
    {
        Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected) => UpdateTwoPlayerAvailability();

    private void UpdateTwoPlayerAvailability()
    {
        bool hasPad = Input.GetConnectedJoypads().Count > 0;
        _twoPlayerButton.Disabled = !hasPad;
        _twoPlayerButton.Text = hasPad
            ? "PLAY — 2 PLAYERS"
            : "PLAY — 2 PLAYERS · CONNECT A CONTROLLER";
    }

    private Control? _picker;

    /// <summary>The game picker (Evolution Explorer, 2026-07-27, designer): a simple
    /// list — FAVORITES (the ADD TO GAMES basket) first, then the curated DEMO games —
    /// instead of dumping users into a file explorer. The explorer survives as the
    /// hidden-by-default ADVANCED option. An in-scene overlay (like the pause menu),
    /// not a native popup window.</summary>
    private void PickGame(MatchMode mode)
    {
        _pendingMode = mode;
        _picker?.QueueFree();

        var overlay = new Control { AnchorRight = 1f, AnchorBottom = 1f };
        _picker = overlay;
        AddChild(overlay);
        var dim = new ColorRect
        {
            Color = new Color(0.02f, 0.02f, 0.04f, 0.6f),
            AnchorRight = 1f,
            AnchorBottom = 1f,
        };
        overlay.AddChild(dim);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both,
        };
        overlay.AddChild(panel);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480f, 0f) };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var title = new Label { Text = "CHOOSE A GAME", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(480f, 380f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        box.AddChild(scroll);

        int favorites = AddGameSection(list, "FAVORITES", AppPaths.FavoritesRoot());
        if (favorites == 0)
        {
            var empty = new Label
            {
                Text = "no favorites yet — ADD TO GAMES from the EVOLVE screen",
                Modulate = new Color(0.55f, 0.6f, 0.68f),
            };
            empty.AddThemeFontSizeOverride("font_size", 13);
            list.AddChild(empty);
        }
        AddGameSection(list, "DEMO GAMES", AppPaths.DemoRoot());

        var advanced = new Button { Text = "ADVANCED: BROWSE FILES…" };
        advanced.Pressed += () =>
        {
            ClosePicker();
            _gameDialog.PopupCentered(new Vector2I(900, 600));
        };
        box.AddChild(advanced);

        var cancel = new Button { Text = "CANCEL" };
        cancel.Pressed += ClosePicker;
        box.AddChild(cancel);
    }

    private void ClosePicker()
    {
        _picker?.QueueFree();
        _picker = null;
    }

    /// <summary>One picker section: a button per game.json in the directory (name from
    /// the filename — records are only parsed on selection). Returns the entry count.</summary>
    private int AddGameSection(VBoxContainer list, string heading, string dir)
    {
        if (!System.IO.Directory.Exists(dir))
        {
            return 0;
        }
        string[] files = System.IO.Directory.GetFiles(dir, "*.json");
        System.Array.Sort(files);
        if (files.Length == 0)
        {
            return 0;
        }
        var section = new Label { Text = heading, Modulate = new Color(0.65f, 0.7f, 0.78f) };
        section.AddThemeFontSizeOverride("font_size", 14);
        list.AddChild(section);
        foreach (string file in files)
        {
            string path = file;
            var button = new Button
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(file).ToUpperInvariant(),
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () =>
            {
                ClosePicker();
                OnGamePicked(path);
            };
            list.AddChild(button);
        }
        return files.Length;
    }

    private void OnGamePicked(string path)
    {
        MatchSession.Game = GameGenomeJson.Load(path);
        MatchSession.Mode = _pendingMode;
        if (_pendingMode == MatchMode.Replay)
        {
            _traceDialog.CurrentDir = _gameDialog.CurrentDir;
            _traceDialog.PopupCentered(new Vector2I(900, 600));
            return;
        }
        StartMatch();
    }

    private void OnTracePicked(string path)
    {
        MatchSession.Trace = BrawlerSim.Replay.InputTraceJson.Load(path);
        StartMatch();
    }

    private void StartMatch()
    {
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    /// <summary>SETTINGS popup (2026-07-21, Map Size): the minimap options — enabled,
    /// corner, size, transparency — persisted via AppSettings (user://settings.cfg).</summary>
    private void OpenSettings()
    {
        var popup = new PopupPanel();
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
        AddChild(popup);
        popup.PopupCentered();
    }

    private FileDialog MakeDialog(string title, System.Action<string> onSelected)
    {
        var dialog = new FileDialog
        {
            Title = title,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = new[] { "*.json" },
            // Open where evolution runs and imported games live.
            CurrentDir = AppPaths.RunsRoot(),
        };
        dialog.FileSelected += path => onSelected(path);
        AddChild(dialog);
        return dialog;
    }

    private static Button AddButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(340f, 44f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
        return button;
    }
}
