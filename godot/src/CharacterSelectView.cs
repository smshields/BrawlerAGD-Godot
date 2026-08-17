using System.Collections.Generic;
using System.Linq;
using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// The Game Player's Smash-style character select (2026-08-14, FEATURES.md §Game
/// Menu / Game Player; layout per game_interface.jpg; docs/features/game-player.md).
/// Four player panes flank a center column of character grid, stage row, and stage
/// preview, under a header of BACK | MODE + STOCKS/MINUTES | START.
///
/// FULL PAD PARITY (designer): every interactive element is a registered HOTSPOT.
/// The mouse activates hotspots natively; each joined pad steers its own colored
/// virtual cursor (stick/dpad moves, A activates the hotspot under it, B cancels),
/// so pads can operate everything — join, picks, the rename keyboard, CPU config,
/// rules, stage, and START. Pane color == cursor color == PlayerPalette slot, and
/// pane index == match player index, so HUD identities carry into the arena.
/// </summary>
public partial class CharacterSelectView : Control
{
    private const int PaneCount = 4;
    private const float CursorSpeed = 640f; // px/s at design resolution

    private enum PaneMode
    {
        Off,
        Human,
        Cpu,
    }

    private sealed class Pane
    {
        public PaneMode Mode = PaneMode.Off;
        public int CharacterIndex = -1;
        public int CpuLevel = CpuLevels.Default;
        public int Device = -2;       // -2 none, -1 keyboard/mouse, >=0 pad device id
        public int PlayerNumber;      // action set 1-4 (humans), assigned at join
        public bool Renaming;
        public string NameOverride = ""; // starts as the fighter's display name on pick
        public PanelContainer Root = null!;
        public Control Body = null!;
    }

    /// <summary>An interactive region: pads hit-test these; the mouse clicks them
    /// through the same handler (one interaction path for every device).</summary>
    private sealed record Hotspot(Control Area, System.Action<int> Activate, System.Func<bool>? Enabled = null)
    {
        public bool IsEnabled => Enabled?.Invoke() ?? true;
    }

    private readonly List<Hotspot> _hotspots = new();
    private readonly Pane[] _panes = new Pane[PaneCount];
    private readonly Dictionary<int, int> _padPane = new();   // pad device -> pane index
    private readonly Dictionary<int, Vector2> _padCursor = new(); // pad device -> position
    private readonly Dictionary<int, int> _retarget = new();  // actor pane -> pane picking for

    private BuiltGame _game = null!;
    private string _path = "";
    private int _stageIndex = -1;
    private MatchEndRule _mode = MatchEndRule.Stock;
    private int _stocks = 4;
    private int _minutes = 4;
    private int _mousePane = -1; // the keyboard/mouse player's pane, -1 until joined

    private Label _modeLabel = null!;
    private Label _valueLabel = null!;
    private Button _startButton = null!;
    private StageThumb _stagePreview = null!;
    private Label _stagePreviewName = null!;
    private readonly List<PanelContainer> _stageCards = new();
    private readonly List<PanelContainer> _gridCells = new();
    private CursorLayer _cursors = null!;

    public override void _Ready()
    {
        if (BuiltGameSession.Game is null)
        {
            GetTree().ChangeSceneToFile(Standalone.MenuScene());
            return;
        }
        // The root must NOT consume clicks (2026-08-17, designer: "selection is
        // completely broken"): a Control's default MouseFilter is STOP, which ate
        // every click that wasn't on a Button before _UnhandledInput could hit-test
        // the hotspots. Real mice never reached the grid/stage cards; only the
        // automation path (which calls handlers directly) ever "worked".
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = BuildButtonTheme();
        _game = BuiltGameSession.Game;
        _path = BuiltGameSession.Path ?? "";
        for (int i = 0; i < PaneCount; i++)
        {
            _panes[i] = new Pane();
        }
        BuildUi();
        RefreshAll();

        // Automation (screenshot verification): BRAWLER_AUTOSELECT="p1=0;cpu2=3;
        // cpu2level=9;stage=1;mode=timed;rename1=1" arranges a lobby on load.
        string auto = OS.GetEnvironment("BRAWLER_AUTOSELECT");
        if (auto.Length > 0)
        {
            ApplyAutoSelect(auto);
        }
    }

    // ── Input routing: one path for mouse and pads ─────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                ActivateAt(click.Position, _mousePane);
                break;
            case InputEventJoypadButton { Pressed: true } pad:
                OnPadButton(pad.Device, pad.ButtonIndex);
                break;
        }
    }

    private void OnPadButton(int device, JoyButton button)
    {
        if (button == JoyButton.A)
        {
            if (_padPane.TryGetValue(device, out int pane))
            {
                ActivateAt(_padCursor[device], pane);
            }
            else
            {
                JoinPad(device);
            }
            return;
        }
        if (button == JoyButton.B && _padPane.TryGetValue(device, out int owner))
        {
            CancelFor(owner);
        }
    }

    public override void _Process(double delta)
    {
        // Pad cursors: left stick / dpad, polled per frame.
        foreach ((int device, int _) in _padPane)
        {
            var move = new Vector2(
                Input.GetJoyAxis(device, JoyAxis.LeftX), Input.GetJoyAxis(device, JoyAxis.LeftY));
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadLeft)) move.X -= 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadRight)) move.X += 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadUp)) move.Y -= 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadDown)) move.Y += 1f;
            if (move.LengthSquared() < 0.04f)
            {
                continue;
            }
            Vector2 next = _padCursor[device] + move.LimitLength(1f) * CursorSpeed * (float)delta;
            _padCursor[device] = next.Clamp(Vector2.Zero, GetViewportRect().Size);
        }
        _cursors.QueueRedraw();
    }

    private void ActivateAt(Vector2 position, int actor)
    {
        // Later registrations sit visually on top (keyboards, overlays) — scan last-first.
        for (int i = _hotspots.Count - 1; i >= 0; i--)
        {
            Hotspot spot = _hotspots[i];
            if (!IsInstanceValid(spot.Area) || !spot.Area.IsVisibleInTree())
            {
                continue;
            }
            if (spot.Area.GetGlobalRect().HasPoint(position))
            {
                if (spot.IsEnabled)
                {
                    spot.Activate(actor);
                }
                return;
            }
        }
    }

    private void CancelFor(int pane)
    {
        Pane p = _panes[pane];
        if (p.Renaming)
        {
            p.Renaming = false;
        }
        else if (_retarget.Remove(pane))
        {
            // dropped the pending pick-for-CPU
        }
        else if (p.CharacterIndex >= 0)
        {
            p.CharacterIndex = -1;
        }
        else
        {
            LeavePane(pane);
        }
        RefreshAll();
    }

    // ── Join / leave / pane state ──────────────────────────────────────────────

    private void JoinPane(int actor)
    {
        // Mouse click on JOIN: first join is the keyboard human; afterwards the
        // mouse is adding OPPONENTS, so further joins are CPUs.
        int free = FreePane();
        if (free < 0)
        {
            return;
        }
        Pane p = _panes[free];
        if (actor == -1 && _mousePane < 0)
        {
            p.Mode = PaneMode.Human;
            p.Device = -1;
            p.PlayerNumber = 1; // the keyboard action set
            _mousePane = free;
        }
        else
        {
            p.Mode = PaneMode.Cpu;
            p.Device = -2;
        }
        RefreshAll();
    }

    private void JoinPad(int device)
    {
        int free = FreePane();
        if (free < 0)
        {
            return;
        }
        int playerNumber = NextFreeActionSet();
        if (playerNumber < 0)
        {
            return;
        }
        Pane p = _panes[free];
        p.Mode = PaneMode.Human;
        p.Device = device;
        p.PlayerNumber = playerNumber;
        Boot.BindPadToPlayer(playerNumber, device); // in-match inputs follow the join
        _padPane[device] = free;
        _padCursor[device] = GetViewportRect().Size / 2f;
        RefreshAll();
    }

    private void LeavePane(int index)
    {
        Pane p = _panes[index];
        if (p.Device >= 0)
        {
            _padPane.Remove(p.Device);
            _padCursor.Remove(p.Device);
        }
        if (index == _mousePane)
        {
            _mousePane = -1;
        }
        _retarget.Remove(index);
        _panes[index] = new Pane { Root = p.Root, Body = p.Body };
    }

    private int FreePane()
    {
        for (int i = 0; i < PaneCount; i++)
        {
            if (_panes[i].Mode == PaneMode.Off)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Action sets: 1 = keyboard; pads take the lowest free of 2-4 (or 1 if
    /// the keyboard never joins — its pad events are rebound at join anyway).</summary>
    private int NextFreeActionSet()
    {
        var used = _panes.Where(p => p.Mode == PaneMode.Human).Select(p => p.PlayerNumber).ToHashSet();
        if (_mousePane >= 0 || used.Contains(1))
        {
            used.Add(1);
        }
        for (int n = 2; n <= 4; n++)
        {
            if (!used.Contains(n))
            {
                return n;
            }
        }
        return used.Contains(1) ? -1 : 1;
    }

    /// <summary>The human/CPU icon cycle (spec: clicking through can turn the panel
    /// off): HUMAN → CPU → OFF; a CPU pane clicked by an actor with no pane of its
    /// own becomes that actor's... it stays the simple cycle — CPU → OFF.</summary>
    private void CycleMode(int index)
    {
        Pane p = _panes[index];
        switch (p.Mode)
        {
            case PaneMode.Human:
                if (p.Device >= 0)
                {
                    _padPane.Remove(p.Device);
                    _padCursor.Remove(p.Device);
                }
                if (index == _mousePane)
                {
                    _mousePane = -1;
                }
                p.Mode = PaneMode.Cpu;
                p.Device = -2;
                break;
            case PaneMode.Cpu:
                LeavePane(index);
                break;
        }
        RefreshAll();
    }

    // ── Character / stage assignment ───────────────────────────────────────────

    private void PickCharacter(int cell, int actor)
    {
        int target = actor >= 0 && _retarget.TryGetValue(actor, out int t) ? t : actor;
        if (actor >= 0)
        {
            _retarget.Remove(actor);
        }
        if (target < 0 || _panes[target].Mode == PaneMode.Off)
        {
            return; // an unjoined mouse admin with no retarget has nobody to pick for
        }
        Pane p = _panes[target];
        p.CharacterIndex = cell;
        RefreshAll();
    }

    private void PickStage(int index)
    {
        _stageIndex = index;
        RefreshAll();
    }

    // ── START / launch (Phase D) ───────────────────────────────────────────────

    private bool CanStart()
    {
        var active = _panes.Where(p => p.Mode != PaneMode.Off).ToList();
        return _stageIndex >= 0
            && active.Count >= 2
            && active.All(p => p.CharacterIndex >= 0);
    }

    private void StartMatch()
    {
        if (!CanStart())
        {
            return;
        }
        var active = _panes.Where(p => p.Mode != PaneMode.Off).ToList();

        // Fighters in pane order (pane index == player index == palette slot):
        // display names carried into the genome so the HUD and tags show them,
        // stocks overridden per the header rule (match-only; docs untouched).
        var fighters = new List<CharacterGenome>();
        var specs = new List<MatchSession.PlayerSpec>();
        foreach (Pane p in active)
        {
            BuiltCharacter entry = _game.Characters[p.CharacterIndex];
            CharacterGenome c = entry.Character;
            fighters.Add(new CharacterGenome(
                p.NameOverride.Trim().Length > 0 ? p.NameOverride.Trim() : entry.DisplayName,
                _stocks, c.SpriteIndex, c.Params, c.Moves, c.ButtonMoves));
            // (player rename wins; otherwise the fighter's generated name shows)
            specs.Add(p.Mode == PaneMode.Human
                ? new MatchSession.PlayerSpec(true, p.PlayerNumber, null)
                : new MatchSession.PlayerSpec(false, 0, CpuLevels.Config(p.CpuLevel)));
        }
        BuiltStage stage = _game.Stages[_stageIndex];

        MatchSession.Game = new GameRecord(
            _game.Name,
            $"built:{System.IO.Path.GetFileName(_path)}/stage{_stageIndex}",
            new GameGenome(fighters, stage.Stage));
        MatchSession.PlayerSpecs = specs;
        MatchSession.Mode = specs.Any(s => s.Human) ? MatchMode.HumanVsCpu : MatchMode.AiVsAi;
        MatchSession.EndRule = _mode;
        MatchSession.TimedMatchSeconds = _minutes * 60f;
        MatchSession.Trace = null;
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    // ── UI construction ────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var header = new HBoxContainer
        {
            AnchorRight = 1f, OffsetLeft = 16f, OffsetTop = 10f, OffsetRight = -16f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddThemeConstantOverride("separation", 12);
        AddChild(header);

        string backScene = Standalone.Active ? "res://scenes/title.tscn" : "res://scenes/game_select.tscn";
        Button back = HeaderButton("BACK");
        back.Pressed += () => GetTree().ChangeSceneToFile(backScene);
        Register(back, _ => GetTree().ChangeSceneToFile(backScene));
        header.AddChild(back);

        var gameName = new Label
        {
            Text = _game.Name,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.65f, 0.7f, 0.78f),
        };
        gameName.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(gameName);

        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        Button mode = HeaderButton("MODE: STOCK");
        _modeLabel = mode.GetNode<Label>("Label"); // HeaderButton stores its label
        void ToggleMode(int _)
        {
            _mode = _mode == MatchEndRule.Stock ? MatchEndRule.Timed : MatchEndRule.Stock;
            RefreshAll();
        }
        mode.Pressed += () => ToggleMode(-1);
        Register(mode, ToggleMode);
        header.AddChild(mode);

        Button minus = HeaderButton("−");
        void Minus(int _) { Step(-1); }
        minus.Pressed += () => Minus(-1);
        Register(minus, Minus);
        header.AddChild(minus);

        _valueLabel = new Label
        {
            Text = "STOCKS 4",
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(120f, 0f),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _valueLabel.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(_valueLabel);

        Button plus = HeaderButton("+");
        void Plus(int _) { Step(1); }
        plus.Pressed += () => Plus(-1);
        Register(plus, Plus);
        header.AddChild(plus);

        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _startButton = HeaderButton("START");
        _startButton.Pressed += StartMatch;
        Register(_startButton, _ => StartMatch(), () => CanStart());
        header.AddChild(_startButton);

        // Body: pane column | center column | pane column.
        var body = new HBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetTop = 64f, OffsetRight = -16f, OffsetBottom = -12f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        body.AddThemeConstantOverride("separation", 14);
        AddChild(body);

        var left = PaneColumn(0, 1);
        body.AddChild(left);
        body.AddChild(CenterColumn());
        var right = PaneColumn(2, 3);
        body.AddChild(right);

        _cursors = new CursorLayer(this);
        AddChild(_cursors);
    }

    private VBoxContainer PaneColumn(int a, int b)
    {
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(300f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 12);
        foreach (int index in new[] { a, b })
        {
            var pane = new PanelContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(300f, 0f),
            };
            _panes[index].Root = pane;
            var content = new VBoxContainer();
            content.AddThemeConstantOverride("separation", 6);
            pane.AddChild(content);
            _panes[index].Body = content;
            column.AddChild(pane);
        }
        return column;
    }

    private Control CenterColumn()
    {
        var center = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        center.AddThemeConstantOverride("separation", 10);

        // Character grid: 2 rows × 4 — sprite with the name below (sketch).
        var grid = new GridContainer { Columns = 4, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        center.AddChild(grid);
        for (int i = 0; i < _game.Characters.Count; i++)
        {
            int cell = i;
            var card = new PanelContainer
            {
                CustomMinimumSize = new Vector2(120f, 112f),
                // Equal columns: every cell expands identically, so a long fighter
                // name can't widen its column and knock the grid off-center.
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                // Hotspot cards must NOT consume mouse clicks — ActivateAt hit-tests
                // them from _UnhandledInput (one path for mouse and pad cursors).
                MouseFilter = MouseFilterEnum.Ignore,
            };
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            card.AddChild(v);
            v.AddChild(new TextureRect
            {
                Texture = SpriteBank.Player(_game.Characters[i].Character.SpriteIndex),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = new Vector2(0f, 64f),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                // TextureRect's default MouseFilter is STOP — it would eat the click.
                MouseFilter = MouseFilterEnum.Ignore,
            });
            var name = new Label
            {
                Text = _game.Characters[i].DisplayName.ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeFontSizeOverride("font_size", 11);
            v.AddChild(name);
            Register(card, actor => PickCharacter(cell, actor));
            _gridCells.Add(card);
            grid.AddChild(card);
        }

        // Stage row: 4 thumbs, thick white border on the selected one.
        var stageRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stageRow.AddThemeConstantOverride("separation", 8);
        center.AddChild(stageRow);
        for (int i = 0; i < _game.Stages.Count; i++)
        {
            int index = i;
            var card = new PanelContainer
            {
                CustomMinimumSize = new Vector2(128f, 84f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore, // hit-tested, not gui-clicked
            };
            var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            card.AddChild(v);
            var thumb = new StageThumb(_game.Stages[i].Stage)
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, 56f),
            };
            v.AddChild(thumb);
            var name = new Label
            {
                Text = _game.Stages[i].DisplayName.ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            name.AddThemeFontSizeOverride("font_size", 10);
            v.AddChild(name);
            Register(card, _ => PickStage(index));
            _stageCards.Add(card);
            stageRow.AddChild(card);
        }

        // Stage preview: the selected stage zoomed out (sketch window).
        _stagePreview = new StageThumb
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 150f),
        };
        center.AddChild(_stagePreview);
        _stagePreviewName = new Label
        {
            Text = "SELECT A STAGE",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.65f, 0.7f, 0.78f),
        };
        _stagePreviewName.AddThemeFontSizeOverride("font_size", 13);
        center.AddChild(_stagePreviewName);

        return center;
    }

    private static Button HeaderButton(string text)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0f, 40f) };
        var label = new Label { Name = "Label", Visible = false }; // text mirror (mode)
        button.AddChild(label);
        return button;
    }

    /// <summary>Scene-wide button styling (2026-08-17, designer: buttons must read
    /// as buttons): bordered dark boxes with hover/pressed/disabled states, applied
    /// as the root theme so every Button in the scene inherits it.</summary>
    private static Theme BuildButtonTheme()
    {
        static StyleBoxFlat Box(Color bg, Color border) => new()
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12f, ContentMarginRight = 12f,
            ContentMarginTop = 5f, ContentMarginBottom = 5f,
        };
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

    /// <summary>Rename-keyboard keys are too dense for the themed margins.</summary>
    private static void CompactKey(Button key)
    {
        static StyleBoxFlat Box(Color bg, Color border) => new()
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 2f, ContentMarginRight = 2f,
            ContentMarginTop = 2f, ContentMarginBottom = 2f,
        };
        key.AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.17f, 0.22f), new Color(0.38f, 0.4f, 0.48f)));
        key.AddThemeStyleboxOverride("hover", Box(new Color(0.21f, 0.22f, 0.28f), new Color(0.58f, 0.61f, 0.7f)));
        key.AddThemeStyleboxOverride("pressed", Box(new Color(0.28f, 0.29f, 0.36f), Colors.White));
    }

    private void Register(Control area, System.Action<int> activate, System.Func<bool>? enabled = null)
        => _hotspots.Add(new Hotspot(area, activate, enabled));

    private void Step(int direction)
    {
        if (_mode == MatchEndRule.Stock)
        {
            _stocks = Mathf.Clamp(_stocks + direction, 1, 100);
        }
        else
        {
            _minutes = Mathf.Clamp(_minutes + direction, 1, 100);
        }
        RefreshAll();
    }

    // ── Refresh ────────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        _modeLabel.GetParent<Button>().Text = _mode == MatchEndRule.Stock ? "MODE: STOCK" : "MODE: TIMED";
        _valueLabel.Text = _mode == MatchEndRule.Stock ? $"STOCKS {_stocks}" : $"TIME {_minutes} MIN";
        _startButton.Disabled = !CanStart();

        for (int i = 0; i < _stageCards.Count; i++)
        {
            _stageCards[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.11f, 0.11f, 0.15f),
                BorderColor = i == _stageIndex ? Colors.White : new Color(0.3f, 0.32f, 0.4f),
                BorderWidthTop = i == _stageIndex ? 4 : 1,
                BorderWidthBottom = i == _stageIndex ? 4 : 1,
                BorderWidthLeft = i == _stageIndex ? 4 : 1,
                BorderWidthRight = i == _stageIndex ? 4 : 1,
                ContentMarginLeft = 4f, ContentMarginRight = 4f,
                ContentMarginTop = 4f, ContentMarginBottom = 4f,
            });
        }
        if (_stageIndex >= 0)
        {
            _stagePreview.SetStage(_game.Stages[_stageIndex].Stage);
            _stagePreviewName.Text = _game.Stages[_stageIndex].DisplayName.ToUpperInvariant();
        }

        // Grid glow: single owner = their color; shared = the blend (spec item 4).
        for (int cell = 0; cell < _gridCells.Count; cell++)
        {
            var owners = new List<int>();
            for (int i = 0; i < PaneCount; i++)
            {
                if (_panes[i].Mode != PaneMode.Off && _panes[i].CharacterIndex == cell)
                {
                    owners.Add(i);
                }
            }
            Color border = owners.Count == 0
                ? new Color(0.3f, 0.32f, 0.4f)
                : owners.Select(PlayerPalette.Of)
                    .Aggregate(new Color(0, 0, 0, 0), (acc, c) => acc + c / owners.Count);
            _gridCells[cell].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = owners.Count == 0 ? new Color(0.11f, 0.11f, 0.15f) : new Color(0.15f, 0.15f, 0.2f),
                BorderColor = border with { A = 1f },
                BorderWidthTop = owners.Count == 0 ? 1 : 3,
                BorderWidthBottom = owners.Count == 0 ? 1 : 3,
                BorderWidthLeft = owners.Count == 0 ? 1 : 3,
                BorderWidthRight = owners.Count == 0 ? 1 : 3,
                ContentMarginLeft = 4f, ContentMarginRight = 4f,
                ContentMarginTop = 4f, ContentMarginBottom = 4f,
            });
        }

        for (int i = 0; i < PaneCount; i++)
        {
            RefreshPane(i);
        }
    }

    private void RefreshPane(int index)
    {
        Pane p = _panes[index];
        Color color = PlayerPalette.Of(index);
        bool off = p.Mode == PaneMode.Off;
        p.Root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = off ? new Color(0.09f, 0.09f, 0.12f) : new Color(0.13f, 0.13f, 0.17f),
            BorderColor = off ? new Color(0.22f, 0.24f, 0.3f) : color,
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 10f, ContentMarginRight = 10f,
            ContentMarginTop = 8f, ContentMarginBottom = 8f,
        });

        foreach (Node child in p.Body.GetChildren())
        {
            child.QueueFree();
        }

        if (off)
        {
            var join = new Button
            {
                Text = "JOIN",
                CustomMinimumSize = new Vector2(120f, 48f),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter | SizeFlags.ExpandFill,
            };
            join.Pressed += () => JoinPane(_mousePane >= 0 ? -2 : -1);
            Register(join, JoinPane);
            p.Body.AddChild(join);
            var hint = new Label
            {
                Text = "CLICK OR PRESS Ⓐ",
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.45f, 0.5f, 0.58f),
            };
            hint.AddThemeFontSizeOverride("font_size", 11);
            p.Body.AddChild(hint);
            return;
        }

        // Title bar: editable name + human/CPU icon (cycles HUMAN → CPU → OFF).
        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 6);
        p.Body.AddChild(title);
        string playerName = p.NameOverride.Trim().Length > 0
            ? p.NameOverride
            : $"PLAYER {index + 1}";
        var nameButton = new Button
        {
            Text = p.Renaming ? playerName + "_" : playerName + " ✎",
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        nameButton.AddThemeColorOverride("font_color", color);
        void OpenRename(int _)
        {
            p.Renaming = !p.Renaming;
            RefreshAll();
        }
        nameButton.Pressed += () => OpenRename(-1);
        Register(nameButton, OpenRename);
        title.AddChild(nameButton);

        var icon = new Button
        {
            Text = p.Mode == PaneMode.Human ? "◉ P" + p.PlayerNumber : "▣ CPU",
            TooltipText = "cycle: HUMAN → CPU → OFF",
        };
        void Cycle(int _) => CycleMode(index);
        icon.Pressed += () => Cycle(-1);
        Register(icon, Cycle);
        title.AddChild(icon);

        if (p.Renaming)
        {
            BuildRenameKeyboard(p);
            return;
        }

        // Character area: live moveset preview once picked (sketch: "once character
        // is selected, area can be used to see moveset").
        if (p.CharacterIndex >= 0)
        {
            BuiltCharacter entry = _game.Characters[p.CharacterIndex];
            var preview = new MovesetPreview
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0f, 130f),
            };
            preview.Setup(entry.Character, entry.DisplayName);
            p.Body.AddChild(preview);
            var charName = new Label
            {
                Text = entry.DisplayName.ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            charName.AddThemeFontSizeOverride("font_size", 13);
            p.Body.AddChild(charName);
        }
        else
        {
            var hint = new Label
            {
                Text = p.Mode == PaneMode.Human
                    ? "PICK A CHARACTER FROM THE GRID"
                    : "PICK A CHARACTER (BUTTON BELOW)",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                Modulate = color with { A = 0.8f },
            };
            hint.AddThemeFontSizeOverride("font_size", 13);
            p.Body.AddChild(hint);
        }

        if (p.Mode == PaneMode.Cpu)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            p.Body.AddChild(row);

            Button down = new() { Text = "◀" };
            void LevelDown(int _)
            {
                p.CpuLevel = Mathf.Clamp(p.CpuLevel - 1, CpuLevels.Min, CpuLevels.Max);
                RefreshAll();
            }
            down.Pressed += () => LevelDown(-1);
            Register(down, LevelDown);
            row.AddChild(down);

            var level = new Label
            {
                Text = $"LEVEL {p.CpuLevel}",
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            level.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(level);

            Button up = new() { Text = "▶" };
            void LevelUp(int _)
            {
                p.CpuLevel = Mathf.Clamp(p.CpuLevel + 1, CpuLevels.Min, CpuLevels.Max);
                RefreshAll();
            }
            up.Pressed += () => LevelUp(-1);
            Register(up, LevelUp);
            row.AddChild(up);

            var pick = new Button { Text = "PICK CHARACTER" };
            void Retarget(int actor)
            {
                // The next grid pick by this actor assigns to THIS CPU pane.
                _retarget[actor >= 0 ? actor : -1] = index;
                if (actor < 0)
                {
                    _retarget[-1] = index; // mouse admin
                }
            }
            pick.Pressed += () => { _retarget[_mousePane >= 0 ? _mousePane : -1] = index; };
            Register(pick, Retarget);
            p.Body.AddChild(pick);
        }
    }

    /// <summary>The in-pane rename keyboard (sketch: digit row + letter rows).</summary>
    private void BuildRenameKeyboard(Pane p)
    {
        var grid = new GridContainer { Columns = 10, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        p.Body.AddChild(grid);
        foreach (char ch in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            char c = ch;
            var key = new Button { Text = c.ToString(), CustomMinimumSize = new Vector2(24f, 24f) };
            CompactKey(key);
            void Type(int _)
            {
                if (p.NameOverride.Length < 14)
                {
                    p.NameOverride += c;
                }
                RefreshAll();
            }
            key.Pressed += () => Type(-1);
            Register(key, Type);
            grid.AddChild(key);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        p.Body.AddChild(row);
        var space = new Button { Text = "SPACE", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        void TypeSpace(int _)
        {
            if (p.NameOverride.Length < 14)
            {
                p.NameOverride += " ";
            }
            RefreshAll();
        }
        space.Pressed += () => TypeSpace(-1);
        Register(space, TypeSpace);
        row.AddChild(space);
        var del = new Button { Text = "DEL", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        void Delete(int _)
        {
            if (p.NameOverride.Length > 0)
            {
                p.NameOverride = p.NameOverride[..^1];
            }
            RefreshAll();
        }
        del.Pressed += () => Delete(-1);
        Register(del, Delete);
        row.AddChild(del);
        var ok = new Button { Text = "OK", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        void Done(int _)
        {
            p.Renaming = false;
            RefreshAll();
        }
        ok.Pressed += () => Done(-1);
        Register(ok, Done);
        row.AddChild(ok);
    }

    // ── Pad cursors ────────────────────────────────────────────────────────────

    /// <summary>Draws each joined pad's cursor arrow in its pane color, topmost.</summary>
    private sealed partial class CursorLayer : Control
    {
        private readonly CharacterSelectView _view;

        public CursorLayer(CharacterSelectView view)
        {
            _view = view;
            AnchorRight = 1f;
            AnchorBottom = 1f;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            foreach ((int device, Vector2 pos) in _view._padCursor)
            {
                Color color = PlayerPalette.Of(_view._padPane[device]);
                var points = new[]
                {
                    pos, pos + new Vector2(18f, 7f), pos + new Vector2(11f, 11f),
                    pos + new Vector2(7f, 18f),
                };
                DrawColoredPolygon(points, color);
                DrawPolyline(points.Append(pos).ToArray(), Colors.White, 1.5f);
            }
        }
    }

    // ── Automation ─────────────────────────────────────────────────────────────

    private void ApplyAutoSelect(string spec)
    {
        foreach (string pair in spec.Split(';'))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2)
            {
                continue;
            }
            switch (kv[0])
            {
                case "p1": // join the mouse/keyboard human and pick a character
                    JoinPane(-1);
                    _panes[_mousePane].CharacterIndex = int.Parse(kv[1]);
                    break;
                case var s when s.StartsWith("cpu", System.StringComparison.Ordinal)
                    && !s.EndsWith("level", System.StringComparison.Ordinal):
                {
                    int pane = int.Parse(s[3..]) - 1;
                    _panes[pane].Mode = PaneMode.Cpu;
                    _panes[pane].CharacterIndex = int.Parse(kv[1]);
                    break;
                }
                case var s when s.EndsWith("level", System.StringComparison.Ordinal):
                    _panes[int.Parse(s[3..^5]) - 1].CpuLevel = int.Parse(kv[1]);
                    break;
                case "stage":
                    _stageIndex = int.Parse(kv[1]);
                    break;
                case "mode":
                    _mode = kv[1] == "timed" ? MatchEndRule.Timed : MatchEndRule.Stock;
                    break;
                case "rename1":
                    _panes[0].Renaming = kv[1] == "1";
                    break;
                case "start":
                    CallDeferred(nameof(StartMatch));
                    break;
                case "click": // "pane0" / "grid3" / "stage2" — REAL mouse clicks
                    _autoClicks.Add(kv[1]);
                    break;
            }
        }
        RefreshAll();
        if (_autoClicks.Count > 0)
        {
            ScheduleAutoClicks();
        }
    }

    // Unlike the direct tokens above, click= targets go through Input.ParseInputEvent —
    // the full input pipeline including GUI consumption — so they verify what a real
    // mouse does (2026-08-17: the direct tokens masked a root MouseFilter bug).
    private readonly List<string> _autoClicks = new();

    private void ScheduleAutoClicks()
    {
        double at = 0.2; // after first-frame layout; all clicks land before the 1 s shot
        foreach (string target in _autoClicks)
        {
            string t = target;
            GetTree().CreateTimer(at).Timeout += () => InjectClick(t);
            at += 0.2;
        }
    }

    private void InjectClick(string target)
    {
        Control? area = target switch
        {
            _ when target.StartsWith("grid", System.StringComparison.Ordinal)
                => _gridCells[int.Parse(target[4..])],
            _ when target.StartsWith("stage", System.StringComparison.Ordinal)
                => _stageCards[int.Parse(target[5..])],
            _ when target.StartsWith("pane", System.StringComparison.Ordinal)
                => _panes[int.Parse(target[4..])].Root,
            _ => null,
        };
        if (area is null)
        {
            return;
        }
        // PushInput(local) delivers in canvas coords through the viewport's full
        // pipeline (GUI consumption first, unhandled after) — ParseInputEvent would
        // treat Position as SCREEN coords and land the click in the wrong control.
        Vector2 pos = area.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = true,
        }, inLocalCoords: true);
        GetViewport().PushInput(new InputEventMouseButton
        {
            Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = false,
        }, inLocalCoords: true);
        GD.Print($"autoclick: {target} @ {pos}");
    }
}
