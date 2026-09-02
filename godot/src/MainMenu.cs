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
    private OptionButton? _rulesOption; // 2026-08-12 STOCK/TIMED picker row
    private Label _hint = null!;
    private Button _twoPlayerButton = null!;
    private PadPresence _padWatch = null!;
    private Control? _picker;

    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        Standalone.ExitToDevMenu(); // the dev menu is never standalone mode
        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
        };
        // 12-13 rows since CREDITS (2026-09-02): tighter spacing + shorter buttons
        // keep the centered column clear of the bottom key-layout hint at 720 px.
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);

        var title = new Label { Text = "BRAWLER AGD", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 52);
        box.AddChild(title);
        var subtitle = new Label
        {
            Text = "automated brawler game designer — godot edition",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiPalette.Heading,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(subtitle);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        _twoPlayerButton = AddButton(box, "PLAY — 2 PLAYERS", () => PickGame(MatchMode.HumanVsHuman));
        AddButton(box, "PLAY — VS CPU", () => PickGame(MatchMode.HumanVsCpu));
        AddButton(box, "WATCH AI MATCH", () => PickGame(MatchMode.AiVsAi));
        AddButton(box, "WATCH REPLAY", () => PickGame(MatchMode.Replay));
        AddButton(box, "PLAY GAME", () => GetTree().ChangeSceneToFile(Scenes.GameSelect));
        AddButton(box, "BUILD GAME", () => GetTree().ChangeSceneToFile(Scenes.GameBuilder));
        AddButton(box, "EVOLVE", () => GetTree().ChangeSceneToFile(Scenes.Evolve));
        AddButton(box, "MANAGE GAMES", () => GetTree().ChangeSceneToFile(Scenes.Manage));
        // Standalone testing (2026-08-17, designer): a dev copy of a packaged game
        // (godot/standalone_game.json, gitignored) no longer hijacks boot — this
        // button is the way into the packaged title flow from the dev menu.
        if (Standalone.HasEmbeddedGame)
        {
            AddButton(box, "TEST STANDALONE GAME",
                () => GetTree().ChangeSceneToFile(Scenes.Title));
        }
        AddButton(box, "SETTINGS", OpenSettings);
        AddButton(box, "CREDITS", () => GetTree().ChangeSceneToFile(Scenes.Credits));
        AddButton(box, "QUIT", () => GetTree().Quit());

        _hint = new Label
        {
            Text = "P1: WASD move · SPACE jump · I/J/K/U/L attacks        P2: gamepad\n" +
                   "gamepad: stick/dpad move · B jump · L1/X/A/Y/R1 attacks",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiPalette.Hint,
            AnchorTop = 1f, AnchorBottom = 1f, AnchorRight = 1f,
            OffsetTop = -64f,
        };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_hint);

        _gameDialog = MakeDialog("Choose a game.json", OnGamePicked);
        _traceDialog = MakeDialog("Choose the matching trace.json", OnTracePicked);

        // 2-player needs a controller (the keyboard is entirely P1's now).
        _padWatch = PadPresence.Watch(hasPad =>
        {
            _twoPlayerButton.Disabled = !hasPad;
            _twoPlayerButton.Text = hasPad
                ? "PLAY — 2 PLAYERS"
                : "PLAY — 2 PLAYERS · CONNECT A CONTROLLER";
        });

        // Automation: BRAWLER_PICKER=1 opens the game picker on load (screenshots).
        if (AutomationEnv.Picker)
        {
            CallDeferred(nameof(OpenPickerForAutomation));
        }
    }

    private void OpenPickerForAutomation() => PickGame(MatchMode.AiVsAi);

    public override void _ExitTree()
    {
        _padWatch.Detach();
    }

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
            Color = UiPalette.OverlayDim,
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

        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 4);
        scroll.CustomMinimumSize = new Vector2(480f, 380f);
        box.AddChild(scroll);

        int favorites = AddGameSection(list, "FAVORITES", AppPaths.FavoritesRoot());
        if (favorites == 0)
        {
            Label empty = UiWidgets.Hint("no favorites yet — ADD TO GAMES from the EVOLVE screen");
            list.AddChild(empty);
        }
        AddGameSection(list, "DEMO GAMES", AppPaths.DemoRoot());

        // Match rules (2026-08-12, four-player.md): STOCK (legacy last-man-standing)
        // or TIMED (infinite stocks, ranked by KOs). Hidden for replays — a trace
        // only replays bit-exactly under the rule it was recorded with (STOCK).
        _rulesOption = null;
        if (mode != MatchMode.Replay)
        {
            _rulesOption = new OptionButton();
            _rulesOption.AddItem("RULES: STOCK (LAST ONE STANDING)", 0);
            _rulesOption.AddItem("RULES: TIMED 1:00 (MOST KOs)", 1);
            _rulesOption.AddItem("RULES: TIMED 2:00 (MOST KOs)", 2);
            _rulesOption.AddItem("RULES: TIMED 5:00 (MOST KOs)", 3);
            _rulesOption.Selected = 0;
            box.AddChild(_rulesOption);
        }

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
        => GameLibraryUi.AddSection(list, heading, dir, path =>
        {
            ClosePicker();
            OnGamePicked(path);
        });

    private void OnGamePicked(string path)
    {
        MatchSession.Game = GameGenomeJson.Load(path);
        MatchSession.Mode = _pendingMode;
        // Match rules (2026-08-12): apply the picker's choice; replays always run
        // STOCK (the rule their traces were recorded under).
        (MatchSession.EndRule, MatchSession.TimedMatchSeconds) = (_rulesOption?.Selected ?? 0) switch
        {
            1 => (BrawlerSim.Sim.MatchEndRule.Timed, 60f),
            2 => (BrawlerSim.Sim.MatchEndRule.Timed, 120f),
            3 => (BrawlerSim.Sim.MatchEndRule.Timed, 300f),
            _ => (BrawlerSim.Sim.MatchEndRule.Stock, MatchSession.TimedMatchSeconds),
        };
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
        GetTree().ChangeSceneToFile(Scenes.Arena);
    }

    private void OpenSettings() => SettingsPopup.Open(this);

    private FileDialog MakeDialog(string title, System.Action<string> onSelected)
    {
        FileDialog dialog = GameLibraryUi.JsonBrowser(onSelected, title);
        AddChild(dialog);
        return dialog;
    }

    private static Button AddButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(340f, 34f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
        return button;
    }
}
