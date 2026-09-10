using Godot;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// App shell, reworked 2026-09-10 (designer): a live AI match always plays behind the
/// menu (random rotation through favorites + demo games; a freshly generated game when
/// both are empty), the title sits top-left, and the root menu is six semi-transparent
/// rows — PLAY / BUILD / EVOLVE / SETTINGS / CREDITS / QUIT. Each of the first four
/// opens a translucent flyout panel to the right: PLAY lists complete built games into
/// the character select, BUILD lists built games into the builder (plus the builder
/// itself), EVOLVE offers the dashboard and a watchable generated-match list (with
/// deletes), SETTINGS shows the shared settings controls inline. Deletes go through
/// one confirm dialog and move files to the OS trash.
/// Automation: BRAWLER_PICKER=1 opens the EVOLVE flyout (the successor of the old
/// watch picker) on load.
/// </summary>
public partial class MainMenu : Control
{
    private const float ColumnLeft = 24f;
    private const float ColumnTop = 76f;
    private const float FlyoutLeft = 276f;

    private FileDialog _gameDialog = null!;
    private MatchPreview _backdrop = null!;
    private Control? _flyout;
    private string _flyoutKey = "";
    private OptionButton? _rulesOption; // lives in the EVOLVE flyout
    private ConfirmationDialog _confirmDelete = null!;
    private string? _pendingDelete;
    private string _pendingDeleteFlyout = "";

    // View-only randomness (backdrop rotation) — never touches sim/evolution streams.
    private readonly System.Random _rng = new();

    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        Standalone.ExitToDevMenu(); // the dev menu is never standalone mode
        BuildBackdrop();
        BuildRootMenu();

        _gameDialog = GameLibraryUi.JsonBrowser(OnGameBrowsed, "Choose a game.json to watch");
        AddChild(_gameDialog);

        _confirmDelete = new ConfirmationDialog { Theme = UiTheme.Buttons, OkButtonText = "DELETE" };
        _confirmDelete.Confirmed += DeleteConfirmed;
        AddChild(_confirmDelete);

        // Automation: BRAWLER_PICKER=1 opens the watchable-game list on load;
        // BRAWLER_FLYOUT=play|build|evolve|settings opens that flyout (screenshots).
        if (AutomationEnv.Picker)
        {
            CallDeferred(nameof(OpenFlyoutDeferred), "evolve");
        }
        else if (AutomationEnv.Flyout.Length > 0)
        {
            CallDeferred(nameof(OpenFlyoutDeferred), AutomationEnv.Flyout);
        }
    }

    private void OpenFlyoutDeferred(string key) => OpenFlyout(key);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_flyout is not null && @event.IsActionPressed("ui_cancel"))
        {
            CloseFlyout();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── Backdrop: a match is always playing ───────────────────────────────────────

    private void BuildBackdrop()
    {
        var container = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(container);
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var viewport = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(viewport);
        _backdrop = new MatchPreview();
        viewport.AddChild(_backdrop);
        _backdrop.NextGame = PickBackdropGame;
        if (PickBackdropGame() is { } record)
        {
            _backdrop.ShowGame(record, firstSeed: (ulong)_rng.Next(1, 1_000_000));
        }
    }

    /// <summary>A random saved game (favorites + demo, designer choice 2026-09-10);
    /// unreadable files fall out of the draw. Both empty ⇒ generate a random game
    /// (view-only RNG, nothing recorded anywhere).</summary>
    private GameRecord? PickBackdropGame()
    {
        var pool = new System.Collections.Generic.List<string>();
        foreach (string dir in new[] { AppPaths.FavoritesRoot(), AppPaths.DemoRoot() })
        {
            if (System.IO.Directory.Exists(dir))
            {
                pool.AddRange(System.IO.Directory.GetFiles(dir, "*.json"));
            }
        }
        while (pool.Count > 0)
        {
            int pick = _rng.Next(pool.Count);
            try
            {
                return GameGenomeJson.Load(pool[pick]);
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"backdrop game {pool[pick]}: {e.Message}");
                pool.RemoveAt(pick);
            }
        }
        GenerationConfig config = GenerationConfig.Default with
        {
            SpriteSelector = SpriteBank.Selector,
            StageThemeSelector = ThemeBank.Selector,
            BackgroundSelector = BackgroundBank.Selector,
        };
        var rng = new Pcg32((ulong)_rng.NextInt64());
        return new GameRecord("random game", "main-menu backdrop", GameGenome.Generate(config, rng));
    }

    // ── Root menu ──────────────────────────────────────────────────────────────────

    private void BuildRootMenu()
    {
        var title = new Label { Text = "BRAWLER AGD", Position = new Vector2(ColumnLeft, 12f) };
        title.AddThemeFontSizeOverride("font_size", 40);
        AddChild(title);

        PanelContainer panel = TranslucentPanel();
        panel.Position = new Vector2(ColumnLeft, ColumnTop);
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        AddRootButton(box, "PLAY", () => OpenFlyout("play"));
        AddRootButton(box, "BUILD", () => OpenFlyout("build"));
        AddRootButton(box, "EVOLVE", () => OpenFlyout("evolve"));
        AddRootButton(box, "SETTINGS", () => OpenFlyout("settings"));
        AddRootButton(box, "CREDITS", () => GetTree().ChangeSceneToFile(Scenes.Credits));
        AddRootButton(box, "QUIT", () => GetTree().Quit());
    }

    private static void AddRootButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(200f, 40f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
    }

    /// <summary>Semi-transparent panel chrome (designer: menus must keep the live
    /// match behind them readable).</summary>
    private static PanelContainer TranslucentPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiWidgets.PanelStyle(
            new Color(0.09f, 0.09f, 0.12f, 0.82f),
            new Color(UiPalette.PanelBorder, 0.6f),
            borderWidth: 1, cornerRadius: 6, marginX: 14f, marginY: 14f));
        return panel;
    }

    // ── Flyouts (the sub menus, opened to the right of the root column) ────────────

    private void OpenFlyout(string key)
    {
        bool wasOpen = _flyoutKey == key && _flyout is not null;
        CloseFlyout();
        if (wasOpen)
        {
            return; // same button again = toggle closed
        }
        _flyoutKey = key;

        PanelContainer panel = TranslucentPanel();
        panel.Position = new Vector2(FlyoutLeft, ColumnTop);
        _flyout = panel;
        AddChild(panel);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420f, 0f) };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        switch (key)
        {
            case "play": BuildPlayFlyout(box); break;
            case "build": BuildBuildFlyout(box); break;
            case "evolve": BuildEvolveFlyout(box); break;
            case "settings": BuildSettingsFlyout(box); break;
        }

        var close = new Button { Text = "CLOSE" };
        close.Pressed += CloseFlyout;
        box.AddChild(close);
    }

    private void CloseFlyout()
    {
        _flyout?.QueueFree();
        _flyout = null;
        _flyoutKey = "";
        _rulesOption = null;
    }

    private static Label FlyoutTitle(string text)
    {
        var title = new Label { Text = text };
        title.AddThemeFontSizeOverride("font_size", 22);
        return title;
    }

    /// <summary>PLAY: complete built games launch the character select (the designer's
    /// 2026-09-10 choice — generated single games are watched from EVOLVE instead).</summary>
    private void BuildPlayFlyout(VBoxContainer box)
    {
        box.AddChild(FlyoutTitle("PLAY GAME"));
        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 4);
        scroll.CustomMinimumSize = new Vector2(420f, 380f);
        box.AddChild(scroll);

        int rows = 0;
        foreach ((string path, BuiltGame game) in LoadBuiltGames())
        {
            rows++;
            bool complete = game.IsComplete;
            string text = complete
                ? $"{game.Name}   —   {game.Characters.Count} FIGHTERS · {game.Stages.Count} STAGES"
                : $"{game.Name}   —   IN PROGRESS ({GameLibraryUi.CompletionBadge(game)})";
            string gamePath = path;
            BuiltGame launch = game;
            list.AddChild(DeletableRow(text, !complete, () => OpenBuiltGame(launch, gamePath), gamePath));
        }
        if (rows == 0)
        {
            list.AddChild(WrapHint("no built games yet — assemble one under BUILD (8 characters + 4 stages)"));
        }
    }

    /// <summary>BUILD: the builder itself, plus the library — clicking a game opens
    /// the builder with it selected.</summary>
    private void BuildBuildFlyout(VBoxContainer box)
    {
        box.AddChild(FlyoutTitle("BUILD GAME"));
        var open = new Button { Text = "OPEN GAME BUILDER" };
        open.Pressed += () => GetTree().ChangeSceneToFile(Scenes.GameBuilder);
        box.AddChild(open);

        box.AddChild(UiWidgets.Heading("EDIT A GAME"));
        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 4);
        scroll.CustomMinimumSize = new Vector2(420f, 330f);
        box.AddChild(scroll);

        int rows = 0;
        foreach ((string path, BuiltGame game) in LoadBuiltGames())
        {
            rows++;
            string badge = game.IsComplete ? "COMPLETE" : GameLibraryUi.CompletionBadge(game);
            string gamePath = path;
            list.AddChild(DeletableRow($"{game.Name}   [{badge}]", disabled: false, () =>
            {
                GameBuilderView.OpenOnLoad = gamePath;
                GetTree().ChangeSceneToFile(Scenes.GameBuilder);
            }, gamePath));
        }
        if (rows == 0)
        {
            list.AddChild(WrapHint("no games yet — OPEN GAME BUILDER to start one"));
        }
    }

    /// <summary>EVOLVE: the dashboard, or watch a generated match (favorites + demo,
    /// AI vs AI under the chosen rules; per-row delete).</summary>
    private void BuildEvolveFlyout(VBoxContainer box)
    {
        box.AddChild(FlyoutTitle("EVOLVE"));
        var evolve = new Button { Text = "EVOLVE NEW MATCHES" };
        evolve.Pressed += () => GetTree().ChangeSceneToFile(Scenes.Evolve);
        box.AddChild(evolve);

        box.AddChild(UiWidgets.Heading("WATCH GENERATED MATCH"));

        // Match rules (2026-08-12, four-player.md): STOCK (legacy last-man-standing)
        // or TIMED (infinite stocks, ranked by KOs).
        _rulesOption = new OptionButton();
        _rulesOption.AddItem("RULES: STOCK (LAST ONE STANDING)", 0);
        _rulesOption.AddItem("RULES: TIMED 1:00 (MOST KOs)", 1);
        _rulesOption.AddItem("RULES: TIMED 2:00 (MOST KOs)", 2);
        _rulesOption.AddItem("RULES: TIMED 5:00 (MOST KOs)", 3);
        _rulesOption.Selected = 0;
        box.AddChild(_rulesOption);

        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 4);
        scroll.CustomMinimumSize = new Vector2(420f, 300f);
        box.AddChild(scroll);

        int favorites = AddWatchSection(list, "GENERATED MATCHES", AppPaths.FavoritesRoot());
        if (favorites == 0)
        {
            list.AddChild(WrapHint("no generated matches yet — save one from the EVOLVE screen's match preview"));
        }
        AddWatchSection(list, "DEMO GAMES", AppPaths.DemoRoot());

        var advanced = new Button { Text = "ADVANCED: BROWSE FILES…" };
        advanced.Pressed += () =>
        {
            CloseFlyout();
            _gameDialog.PopupCentered(new Vector2I(900, 600));
        };
        box.AddChild(advanced);
    }

    private void BuildSettingsFlyout(VBoxContainer box)
    {
        box.AddChild(FlyoutTitle("SETTINGS"));
        SettingsPopup.AddContent(box);
    }

    // ── Flyout building blocks ─────────────────────────────────────────────────────

    private static System.Collections.Generic.List<(string Path, BuiltGame Game)> LoadBuiltGames()
    {
        string[] files = System.IO.Directory.GetFiles(AppPaths.GamesRoot(), "*.json");
        System.Array.Sort(files);
        var games = new System.Collections.Generic.List<(string, BuiltGame)>();
        foreach (string file in files)
        {
            try
            {
                games.Add((file, BuiltGameJson.Load(file)));
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"built game {file}: {e.Message}");
            }
        }
        return games;
    }

    /// <summary>One watch section: a deletable row per game.json. Returns the count.</summary>
    private int AddWatchSection(VBoxContainer list, string heading, string dir)
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
        list.AddChild(UiWidgets.Heading(heading));
        foreach (string file in files)
        {
            string path = file;
            list.AddChild(DeletableRow(
                System.IO.Path.GetFileNameWithoutExtension(file).ToUpperInvariant(),
                disabled: false, () => WatchGame(path), path));
        }
        return files.Length;
    }

    /// <summary>A list row: left-aligned action button + trash button (confirm-gated
    /// delete to the OS trash — designer 2026-09-10).</summary>
    private Control DeletableRow(string text, bool disabled, System.Action onPick, string path)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        button.Pressed += () => onPick();
        row.AddChild(button);
        var delete = new Button
        {
            Icon = UiIcons.Trash(16),
            TooltipText = "DELETE",
            IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(34f, 0f),
        };
        delete.Pressed += () => RequestDelete(path);
        row.AddChild(delete);
        return row;
    }

    private Label WrapHint(string text)
    {
        Label hint = UiWidgets.Hint(text);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return hint;
    }

    private void RequestDelete(string path)
    {
        _pendingDelete = path;
        _pendingDeleteFlyout = _flyoutKey;
        _confirmDelete.DialogText =
            $"DELETE {System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant()}?";
        _confirmDelete.PopupCentered();
    }

    private void DeleteConfirmed()
    {
        if (_pendingDelete is null)
        {
            return;
        }
        Error err = OS.MoveToTrash(_pendingDelete);
        if (err != Error.Ok)
        {
            GD.PrintErr($"could not move to trash: {_pendingDelete} ({err})");
        }
        _pendingDelete = null;
        // Rebuild the list the row lived in (and let the backdrop rotation see the
        // shrunken pool on its next pick).
        string key = _pendingDeleteFlyout;
        CloseFlyout();
        OpenFlyout(key);
    }

    // ── Launch paths ───────────────────────────────────────────────────────────────

    /// <summary>PLAY: same open path as GameSelectView — presentation pass, then the
    /// character select.</summary>
    private void OpenBuiltGame(BuiltGame game, string path)
    {
        if (BuiltGamePresenter.EnsurePresented(game, path))
        {
            GD.Print($"presented built game elements: {path}");
        }
        BuiltGameSession.Game = game;
        BuiltGameSession.Path = path;
        GetTree().ChangeSceneToFile(Scenes.CharacterSelect);
    }

    /// <summary>Watch a generated match: AI vs AI in the real arena under the
    /// flyout's rules choice.</summary>
    private void WatchGame(string path)
    {
        MatchSession.Game = GameGenomeJson.Load(path);
        MatchSession.Mode = MatchMode.AiVsAi;
        (MatchSession.EndRule, MatchSession.TimedMatchSeconds) = (_rulesOption?.Selected ?? 0) switch
        {
            1 => (BrawlerSim.Sim.MatchEndRule.Timed, 60f),
            2 => (BrawlerSim.Sim.MatchEndRule.Timed, 120f),
            3 => (BrawlerSim.Sim.MatchEndRule.Timed, 300f),
            _ => (BrawlerSim.Sim.MatchEndRule.Stock, MatchSession.TimedMatchSeconds),
        };
        GetTree().ChangeSceneToFile(Scenes.Arena);
    }

    private void OnGameBrowsed(string path) => WatchGame(path);
}
