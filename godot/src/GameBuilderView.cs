using System.Linq;
using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// The Game Builder (2026-08-13, FEATURES.md §Game Menu / Game Builder;
/// docs/features/game-builder.md). Three columns, game-menu styled per AESTHETICS:
/// the GAMES library (create / open / rename / delete built games under runs/games/),
/// the open game's roster (exactly 8 characters + 4 stages = COMPLETE; strict slots,
/// content-duplicate rejection; per-element rename + remove), and the ADD FROM source
/// browser (FAVORITES / DEMO GAMES lists like the game picker, file explorer demoted
/// to ADVANCED) with character/move and stage-layout previews. Every change autosaves.
/// View-layer only — the compiled document (BuiltGameJson) lives in BrawlerSim.
/// Automation: BRAWLER_SCENE=game_builder; BRAWLER_AUTOBUILD=1 assembles a sample
/// game from the demo library on load (screenshot verification).
/// </summary>
public partial class GameBuilderView : Control
{
    private VBoxContainer _libraryList = null!;
    private VBoxContainer _rosterCharacters = null!;
    private VBoxContainer _rosterStages = null!;
    private VBoxContainer _sourceList = null!;
    private VBoxContainer _sourceElements = null!;
    private LineEdit _gameName = null!;
    private Label _rosterHeading = null!;
    private Label _charHeading = null!;
    private Label _stageHeading = null!;
    private Label _status = null!;
    private Button _deleteButton = null!;
    private ConfirmationDialog _confirmDelete = null!;
    private FileDialog _sourceDialog = null!;

    private BuiltGame? _game;       // the open game
    private string? _gamePath;      // its file under runs/games/
    private GameRecord? _source;    // the open source game.json
    private string _sourceLabel = "";

    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        BuildUi();
        RefreshLibrary();
        RefreshRoster();
        RefreshSourceElements();

        if (OS.GetEnvironment("BRAWLER_AUTOBUILD") == "1")
        {
            AutoBuildSample();
        }
    }

    // ── Library (left column) ─────────────────────────────────────────────────

    private void RefreshLibrary()
    {
        foreach (Node child in _libraryList.GetChildren())
        {
            child.QueueFree();
        }
        string[] files = System.IO.Directory.GetFiles(AppPaths.GamesRoot(), "*.json");
        System.Array.Sort(files);
        if (files.Length == 0)
        {
            var empty = new Label
            {
                Text = "no games yet — NEW GAME to start one",
                Modulate = new Color(0.55f, 0.6f, 0.68f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            empty.AddThemeFontSizeOverride("font_size", 13);
            _libraryList.AddChild(empty);
        }
        foreach (string file in files)
        {
            string path = file;
            string badge;
            string name;
            try
            {
                BuiltGame game = BuiltGameJson.Load(path);
                name = game.Name;
                badge = game.IsComplete
                    ? "COMPLETE"
                    : $"{game.Characters.Count}/{BuiltGame.RequiredCharacters} · "
                      + $"{game.Stages.Count}/{BuiltGame.RequiredStages}";
            }
            catch (System.Exception e)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
                badge = "UNREADABLE";
                GD.PrintErr($"built game {path}: {e.Message}");
            }
            var button = new Button
            {
                Text = $"{name}   [{badge}]",
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                ButtonPressed = path == _gamePath,
            };
            button.Pressed += () => OpenGame(path);
            _libraryList.AddChild(button);
        }
    }

    private void OpenGame(string path)
    {
        try
        {
            _game = BuiltGameJson.Load(path);
            _gamePath = path;
        }
        catch (System.Exception e)
        {
            Status($"could not open: {e.Message}");
            return;
        }
        _gameName.Text = _game.Name;
        RefreshLibrary();
        RefreshRoster();
        RefreshSourceElements(); // ADD button states depend on the open game
    }

    private void NewGame()
    {
        var game = new BuiltGame { Name = "UNTITLED" };
        string path;
        int n = 0;
        do
        {
            n++;
            path = System.IO.Path.Combine(AppPaths.GamesRoot(), $"game-{n:D2}.json");
        }
        while (System.IO.File.Exists(path));
        BuiltGameJson.Save(game, path);
        OpenGame(path);
        Status($"created {System.IO.Path.GetFileName(path)}");
    }

    private void DeleteOpenGame()
    {
        if (_gamePath is null)
        {
            return;
        }
        System.IO.File.Delete(_gamePath);
        Status($"deleted {System.IO.Path.GetFileName(_gamePath)}");
        _game = null;
        _gamePath = null;
        _gameName.Text = "";
        RefreshLibrary();
        RefreshRoster();
        RefreshSourceElements();
    }

    private void SaveOpenGame()
    {
        if (_game is not null && _gamePath is not null)
        {
            BuiltGameJson.Save(_game, _gamePath);
        }
    }

    // ── Roster (middle column) ────────────────────────────────────────────────

    private void RefreshRoster()
    {
        _rosterHeading.Text = _game is null
            ? "THIS GAME — open or create one"
            : _game.IsComplete ? "THIS GAME — COMPLETE" : "THIS GAME — in progress";
        _gameName.Editable = _game is not null;
        _deleteButton.Disabled = _game is null;
        _charHeading.Text =
            $"CHARACTERS {_game?.Characters.Count ?? 0}/{BuiltGame.RequiredCharacters}";
        _stageHeading.Text = $"STAGES {_game?.Stages.Count ?? 0}/{BuiltGame.RequiredStages}";

        foreach (Node child in _rosterCharacters.GetChildren())
        {
            child.QueueFree();
        }
        foreach (Node child in _rosterStages.GetChildren())
        {
            child.QueueFree();
        }
        if (_game is null)
        {
            return;
        }
        for (int i = 0; i < _game.Characters.Count; i++)
        {
            int index = i;
            BuiltCharacter entry = _game.Characters[i];
            _rosterCharacters.AddChild(CharacterCard(
                entry.Character, entry.DisplayName, entry.Origin,
                rename: newName =>
                {
                    _game.Characters[index] = _game.Characters[index] with { DisplayName = newName };
                    SaveOpenGame();
                    RefreshLibrary();
                },
                action: ("REMOVE", () =>
                {
                    _game.Characters.RemoveAt(index);
                    SaveOpenGame();
                    RefreshRoster();
                    RefreshLibrary();
                    RefreshSourceElements();
                })));
        }
        for (int i = 0; i < _game.Stages.Count; i++)
        {
            int index = i;
            BuiltStage entry = _game.Stages[i];
            _rosterStages.AddChild(StageCard(
                entry.Stage, entry.DisplayName, entry.Origin,
                rename: newName =>
                {
                    _game.Stages[index] = _game.Stages[index] with { DisplayName = newName };
                    SaveOpenGame();
                    RefreshLibrary();
                },
                action: ("REMOVE", () =>
                {
                    _game.Stages.RemoveAt(index);
                    SaveOpenGame();
                    RefreshRoster();
                    RefreshLibrary();
                    RefreshSourceElements();
                })));
        }
    }

    // ── Sources (right column) ────────────────────────────────────────────────

    private void RefreshSourceList()
    {
        foreach (Node child in _sourceList.GetChildren())
        {
            child.QueueFree();
        }
        AddSourceSection("FAVORITES", AppPaths.FavoritesRoot());
        AddSourceSection("DEMO GAMES", AppPaths.DemoRoot());
    }

    private void AddSourceSection(string heading, string dir)
    {
        if (!System.IO.Directory.Exists(dir))
        {
            return;
        }
        string[] files = System.IO.Directory.GetFiles(dir, "*.json");
        System.Array.Sort(files);
        if (files.Length == 0)
        {
            return;
        }
        var section = new Label { Text = heading, Modulate = new Color(0.65f, 0.7f, 0.78f) };
        section.AddThemeFontSizeOverride("font_size", 14);
        _sourceList.AddChild(section);
        foreach (string file in files)
        {
            string path = file;
            var button = new Button
            {
                Text = System.IO.Path.GetFileNameWithoutExtension(file).ToUpperInvariant(),
                Alignment = HorizontalAlignment.Left,
            };
            button.Pressed += () => OpenSource(path);
            _sourceList.AddChild(button);
        }
    }

    private void OpenSource(string path)
    {
        try
        {
            _source = GameGenomeJson.Load(path);
            _sourceLabel = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
        }
        catch (System.Exception e)
        {
            Status($"could not open source: {e.Message}");
            return;
        }
        RefreshSourceElements();
    }

    private void RefreshSourceElements()
    {
        foreach (Node child in _sourceElements.GetChildren())
        {
            child.QueueFree();
        }
        if (_source is null)
        {
            var hint = new Label
            {
                Text = "pick a game above to see its characters and stage",
                Modulate = new Color(0.55f, 0.6f, 0.68f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            hint.AddThemeFontSizeOverride("font_size", 13);
            _sourceElements.AddChild(hint);
            return;
        }

        var title = new Label { Text = _sourceLabel, Modulate = new Color(0.9f, 0.92f, 0.98f) };
        title.AddThemeFontSizeOverride("font_size", 16);
        _sourceElements.AddChild(title);

        for (int i = 0; i < _source.Genome.Characters.Count; i++)
        {
            CharacterGenome character = _source.Genome.Characters[i];
            string defaultName = $"{_sourceLabel} P{i + 1}";
            string origin = $"{_source.Origin ?? _sourceLabel}/char{i}";
            bool inGame = _game is not null
                && _game.Characters.Any(c =>
                    BuiltGame.ContentKey(c.Character) == BuiltGame.ContentKey(character));
            _sourceElements.AddChild(CharacterCard(
                character, defaultName, origin, rename: null,
                action: (_game is null ? "OPEN A GAME" : inGame ? "IN GAME" : "ADD", () =>
                {
                    if (_game is null)
                    {
                        return;
                    }
                    if (_game.TryAddCharacter(
                            new BuiltCharacter(defaultName, origin, character), out string reason))
                    {
                        SaveOpenGame();
                        Status($"added {defaultName}");
                        RefreshRoster();
                        RefreshLibrary();
                        RefreshSourceElements();
                    }
                    else
                    {
                        Status($"not added: {reason}");
                    }
                }),
                actionEnabled: _game is not null && !inGame));
        }

        StageGenome stage = _source.Genome.Stage;
        string stageName = $"{_sourceLabel} STAGE";
        string stageOrigin = $"{_source.Origin ?? _sourceLabel}/stage";
        bool stageInGame = _game is not null
            && _game.Stages.Any(s => BuiltGame.ContentKey(s.Stage) == BuiltGame.ContentKey(stage));
        _sourceElements.AddChild(StageCard(
            stage, stageName, stageOrigin, rename: null,
            action: (_game is null ? "OPEN A GAME" : stageInGame ? "IN GAME" : "ADD", () =>
            {
                if (_game is null)
                {
                    return;
                }
                if (_game.TryAddStage(new BuiltStage(stageName, stageOrigin, stage), out string reason))
                {
                    SaveOpenGame();
                    Status($"added {stageName}");
                    RefreshRoster();
                    RefreshLibrary();
                    RefreshSourceElements();
                }
                else
                {
                    Status($"not added: {reason}");
                }
            }),
            actionEnabled: _game is not null && !stageInGame));
    }

    // ── Cards (shared by roster + sources) ────────────────────────────────────

    /// <summary>Portrait + (editable) name + a chip per move (icon, type, damage) +
    /// one action button. rename == null renders the name as a plain label.</summary>
    private static Control CharacterCard(
        CharacterGenome character, string name, string? origin,
        System.Action<string>? rename, (string Label, System.Action Press) action,
        bool actionEnabled = true)
    {
        PanelContainer panel = CardPanel();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        row.AddChild(new TextureRect
        {
            Texture = SpriteBank.Player(character.SpriteIndex),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            CustomMinimumSize = new Vector2(44f, 44f),
        });

        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(mid);
        mid.AddChild(NameControl(name, rename));

        // Move chips: one per move — sprite icon, type, and the damage gene for
        // attack-family moves (the builder preview the spec asks for).
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 8);
        mid.AddChild(chips);
        foreach (MoveGenome move in character.Moves)
        {
            var chip = new HBoxContainer();
            chip.AddThemeConstantOverride("separation", 2);
            chip.AddChild(new TextureRect
            {
                Texture = SpriteBank.Move(move.SpriteIndex),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(16f, 16f),
            });
            var text = new Label { Text = MoveChipText(move) };
            text.AddThemeFontSizeOverride("font_size", 10);
            text.Modulate = new Color(0.65f, 0.7f, 0.78f);
            chip.AddChild(text);
            chips.AddChild(chip);
        }

        row.AddChild(ActionButton(action, actionEnabled));
        if (origin is not null)
        {
            panel.TooltipText = origin;
        }
        return panel;
    }

    private static Control StageCard(
        StageGenome stage, string name, string? origin,
        System.Action<string>? rename, (string Label, System.Action Press) action,
        bool actionEnabled = true)
    {
        PanelContainer panel = CardPanel();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        var thumb = new StageThumb(stage) { CustomMinimumSize = new Vector2(96f, 54f) };
        row.AddChild(thumb);

        var mid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(mid);
        mid.AddChild(NameControl(name, rename));
        var info = new Label
        {
            Text = $"{stage.Platforms.Count} PLATFORMS · "
                + $"{stage.Params.Get(StageParams.VisibleHalfWidth) * 2f:F0}×"
                + $"{stage.Params.Get(StageParams.VisibleHalfHeight) * 2f:F0} UNITS",
        };
        info.AddThemeFontSizeOverride("font_size", 10);
        info.Modulate = new Color(0.65f, 0.7f, 0.78f);
        mid.AddChild(info);

        row.AddChild(ActionButton(action, actionEnabled));
        if (origin is not null)
        {
            panel.TooltipText = origin;
        }
        return panel;
    }

    /// <summary>One-line move summary: type + the damage gene for attack-family
    /// moves (defensive moves read by type alone).</summary>
    private static string MoveChipText(MoveGenome move) => move.Type switch
    {
        MoveType.Attack => $"ATK {move.Params.Get(MoveParams.DamageFactor):F1}",
        MoveType.Projectile => $"PROJ {move.Params.Get(ProjectileParams.DamageFactor):F1}",
        MoveType.Shield => "SHLD",
        MoveType.Dash => "DASH",
        _ => move.Type.ToString().ToUpperInvariant(),
    };

    private static PanelContainer CardPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.13f, 0.17f),
            BorderColor = new Color(0.3f, 0.32f, 0.4f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 8f, ContentMarginRight = 8f,
            ContentMarginTop = 6f, ContentMarginBottom = 6f,
        });
        return panel;
    }

    private static Control NameControl(string name, System.Action<string>? rename)
    {
        if (rename is null)
        {
            var label = new Label { Text = name };
            label.AddThemeFontSizeOverride("font_size", 14);
            return label;
        }
        var edit = new LineEdit { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        edit.AddThemeFontSizeOverride("font_size", 14);
        void Commit()
        {
            string trimmed = edit.Text.Trim().ToUpperInvariant();
            if (trimmed.Length > 0)
            {
                rename(trimmed);
            }
        }
        edit.TextSubmitted += _ => Commit();
        edit.FocusExited += Commit;
        return edit;
    }

    private static Button ActionButton((string Label, System.Action Press) action, bool enabled)
    {
        var button = new Button
        {
            Text = action.Label,
            Disabled = !enabled,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        button.Pressed += action.Press;
        return button;
    }

    // ── UI scaffold ───────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var root = new HBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 24f, OffsetTop = 24f, OffsetRight = -24f, OffsetBottom = -24f,
        };
        root.AddThemeConstantOverride("separation", 24);
        AddChild(root);

        // LEFT — the games library.
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(280f, 0f) };
        left.AddThemeConstantOverride("separation", 8);
        root.AddChild(left);
        var title = new Label { Text = "GAME BUILDER" };
        title.AddThemeFontSizeOverride("font_size", 34);
        left.AddChild(title);
        left.AddChild(Heading("GAMES"));
        var libraryScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _libraryList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _libraryList.AddThemeConstantOverride("separation", 4);
        libraryScroll.AddChild(_libraryList);
        left.AddChild(libraryScroll);
        var newButton = new Button { Text = "NEW GAME" };
        newButton.Pressed += NewGame;
        left.AddChild(newButton);
        _deleteButton = new Button { Text = "DELETE GAME", Disabled = true };
        _deleteButton.Pressed += () => _confirmDelete.PopupCentered();
        left.AddChild(_deleteButton);
        var back = new Button { Text = "BACK" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        left.AddChild(back);

        // MIDDLE — the open game's roster.
        var mid = new VBoxContainer { CustomMinimumSize = new Vector2(400f, 0f) };
        mid.AddThemeConstantOverride("separation", 8);
        root.AddChild(mid);
        _rosterHeading = Heading("THIS GAME — open or create one");
        mid.AddChild(_rosterHeading);
        _gameName = new LineEdit { PlaceholderText = "game name", Editable = false };
        void CommitGameName()
        {
            if (_game is not null && _gameName.Text.Trim().Length > 0)
            {
                _game.Name = _gameName.Text.Trim().ToUpperInvariant();
                SaveOpenGame();
                RefreshLibrary();
            }
        }
        _gameName.TextSubmitted += _ => CommitGameName();
        _gameName.FocusExited += CommitGameName;
        mid.AddChild(_gameName);
        _charHeading = Heading("CHARACTERS 0/8");
        mid.AddChild(_charHeading);
        var charScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsStretchRatio = 2f,
        };
        _rosterCharacters = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterCharacters.AddThemeConstantOverride("separation", 6);
        charScroll.AddChild(_rosterCharacters);
        mid.AddChild(charScroll);
        _stageHeading = Heading("STAGES 0/4");
        mid.AddChild(_stageHeading);
        var stageScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rosterStages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterStages.AddThemeConstantOverride("separation", 6);
        stageScroll.AddChild(_rosterStages);
        mid.AddChild(stageScroll);

        // RIGHT — sources.
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        root.AddChild(right);
        right.AddChild(Heading("ADD FROM"));
        var sourceScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _sourceList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sourceList.AddThemeConstantOverride("separation", 4);
        sourceScroll.AddChild(_sourceList);
        right.AddChild(sourceScroll);
        var advanced = new Button { Text = "ADVANCED: BROWSE FILES…" };
        advanced.Pressed += () => _sourceDialog.PopupCentered(new Vector2I(900, 600));
        right.AddChild(advanced);
        right.AddChild(Heading("ELEMENTS"));
        var elementScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsStretchRatio = 2f,
        };
        _sourceElements = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sourceElements.AddThemeConstantOverride("separation", 6);
        elementScroll.AddChild(_sourceElements);
        right.AddChild(elementScroll);
        _status = new Label { Modulate = new Color(1f, 0.9f, 0.6f) };
        _status.AddThemeFontSizeOverride("font_size", 13);
        right.AddChild(_status);

        _confirmDelete = new ConfirmationDialog
        {
            DialogText = "Delete this game? The compiled document is removed from disk.",
        };
        _confirmDelete.Confirmed += DeleteOpenGame;
        AddChild(_confirmDelete);

        _sourceDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.json ; evolved game" },
            CurrentDir = AppPaths.RunsRoot(),
        };
        _sourceDialog.FileSelected += OpenSource;
        AddChild(_sourceDialog);

        RefreshSourceList();
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text, Modulate = new Color(0.65f, 0.7f, 0.78f) };
        label.AddThemeFontSizeOverride("font_size", 15);
        return label;
    }

    private void Status(string text) => _status.Text = text.ToUpperInvariant();

    /// <summary>Automation (screenshot verification): create a game and fill it from
    /// the demo + favorites libraries, leaving the first source open.</summary>
    private void AutoBuildSample()
    {
        NewGame();
        _game!.Name = "AUTOBUILD SAMPLE";
        _gameName.Text = _game.Name;
        string[] sources =
            System.IO.Directory.GetFiles(AppPaths.DemoRoot(), "*.json")
                .Concat(System.IO.Directory.GetFiles(AppPaths.FavoritesRoot(), "*.json"))
                .OrderBy(p => p).ToArray();
        foreach (string path in sources)
        {
            GameRecord record;
            try
            {
                record = GameGenomeJson.Load(path);
            }
            catch
            {
                continue;
            }
            string label = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            for (int i = 0; i < record.Genome.Characters.Count; i++)
            {
                _game.TryAddCharacter(new BuiltCharacter(
                    $"{label} P{i + 1}", $"{record.Origin ?? label}/char{i}",
                    record.Genome.Characters[i]), out _);
            }
            _game.TryAddStage(new BuiltStage(
                $"{label} STAGE", $"{record.Origin ?? label}/stage", record.Genome.Stage), out _);
        }
        SaveOpenGame();
        if (sources.Length > 0)
        {
            OpenSource(sources[0]);
        }
        RefreshRoster();
        RefreshLibrary();
    }
}
