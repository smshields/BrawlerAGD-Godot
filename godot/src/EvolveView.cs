using Godot;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// In-app evolution dashboard: configure a run, execute the EvolutionEngine on a
/// background thread (checkpointing every generation exactly like the CLI), watch the
/// fitness curves live, then jump straight to watching the best game.
/// Automation: BRAWLER_AUTOEVOLVE="name=x;pop=24;gens=20;seed=9" starts on load;
/// with BRAWLER_SHOT set it captures the finished dashboard and quits.
/// </summary>
public partial class EvolveView : Control
{
    private SpinBox _seed = null!;
    private SpinBox _population = null!;
    private SpinBox _generations = null!;
    private SpinBox _rounds = null!;
    private HSlider _mutation = null!;
    private HSlider _dropout = null!;
    private LineEdit _runName = null!;
    private Button _start = null!;
    private Button _watchBest = null!;
    private Label _status = null!;
    private FitnessChart _chart = null!;

    // Composition control + advanced ranges (2026-07-14,
    // docs/features/evolve-composition-and-ranges.md)
    private OptionButton _compositionMode = null!;
    private OptionButton _numPlayers = null!; // 2026-08-12 four-player
    private HBoxContainer _perButtonRow = null!;
    private readonly OptionButton[] _buttonSlots = new OptionButton[BrawlerSim.Sim.InputFrame.ActionCount];
    private Button _advancedToggle = null!;
    private ScrollContainer _advancedPanel = null!;
    private readonly System.Collections.Generic.List<RangeRow> _rangeRows = new();

    private sealed class RangeRow
    {
        public required string Schema;
        public required ParamSpec Stock;
        public required SpinBox Min;
        public required SpinBox Max;
        public required Label Warning;
    }

    private CancellationTokenSource? _cancel;
    private string _runDir = "";
    private ulong _startTimeMs;

    // Evolution Explorer (2026-07-27, designer): per-game chart points feed a live
    // match preview + the ADD TO GAMES favorites basket. Generations cross from the
    // engine thread through a queue (GameGenome is not a Variant, so no CallDeferred
    // args); genomes are immutable and survivors are shared refs across generations,
    // so retaining them is cheap.
    private readonly System.Collections.Concurrent.ConcurrentQueue<
        (GenerationStats Stats, float[] Scores, GameGenome[] Genomes)> _pendingGenerations = new();
    private int _lastBestIndex;
    private bool _autoFavorite;
    private VBoxContainer _previewPanel = null!;
    private Label _previewInfo = null!;
    private Label _previewSeed = null!;
    private Button _addToGames = null!;
    private Label _savedNote = null!;
    private MatchPreview _preview = null!;
    private (int Gen, int Index, float Score, GameGenome Genome)? _selection;

    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        BuildUi();
        string auto = OS.GetEnvironment("BRAWLER_AUTOEVOLVE");
        if (auto.Length > 0)
        {
            ApplyAutoConfig(auto);
            StartRun();
        }
    }

    public override void _ExitTree()
    {
        _cancel?.Cancel();
    }

    private void StartRun()
    {
        var config = new EvolutionConfig
        {
            Seed = (ulong)_seed.Value,
            PopulationSize = (int)_population.Value,
            RoundsPerIndividual = (int)_rounds.Value,
            MutationRate = (float)_mutation.Value,
            DropoutRate = (float)_dropout.Value,
            Generation = BuildGenerationConfig(),
        };
        int generations = (int)_generations.Value;
        _runDir = System.IO.Path.Combine(AppPaths.RunsRoot(), _runName.Text.Trim().Length > 0 ? _runName.Text.Trim() : "unnamed");

        _chart.Clear();
        ClearSelection();
        _start.Disabled = true;
        _watchBest.Disabled = true;
        _status.Text = $"running → {_runDir}";
        _startTimeMs = Time.GetTicksMsec();
        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;

        Task.Run(() =>
        {
            var engine = new EvolutionEngine(config);
            var history = new System.Collections.Generic.List<GenerationStats>();
            float bestSoFar = float.MinValue;
            while (engine.GenerationsCompleted < generations && !token.IsCancellationRequested)
            {
                GenerationStats stats = engine.Step();
                history.Add(stats);
                if (stats.TopFitness > bestSoFar)
                {
                    bestSoFar = stats.TopFitness;
                    (_, var trace) = engine.ReplayEvaluation(stats.BestIndex, stats.Generation);
                    RunStore.SaveBest(_runDir, engine.Population[stats.BestIndex], stats, trace);
                }
                RunStore.SaveCheckpoint(_runDir, engine, config, history);
                // Snapshot between Steps (the engine is idle): scores are copied
                // (the engine reuses its buffer), genome refs are immutable.
                _pendingGenerations.Enqueue((stats, engine.LastFitness.ToArray(), engine.Population.ToArray()));
                CallDeferred(nameof(DrainGenerations));
            }
            CallDeferred(nameof(OnRunFinished), engine.GenerationsCompleted, token.IsCancellationRequested);
        }, token);
    }

    private void DrainGenerations()
    {
        while (_pendingGenerations.TryDequeue(out var gen))
        {
            _chart.AddGeneration(gen.Stats.TopFitness, gen.Stats.AverageFitness, gen.Scores, gen.Genomes);
            _lastBestIndex = gen.Stats.BestIndex;
            float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
            _status.Text = $"gen {gen.Stats.Generation}   top {gen.Stats.TopFitness:F1}   " +
                $"avg {gen.Stats.AverageFitness:F1}   {elapsed:F1}s   → {_runDir}";
        }
    }

    private void OnRunFinished(int generations, bool cancelled)
    {
        DrainGenerations(); // anything still queued when the loop ended
        float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
        _status.Text = (cancelled ? "stopped" : "done") +
            $" — {generations} generations in {elapsed:F1}s — saved to {_runDir}";
        _start.Disabled = false;
        _watchBest.Disabled = !System.IO.File.Exists(System.IO.Path.Combine(_runDir, "best.json"));

        // Convenience: focus the final generation's best game so the preview is live
        // the moment a run ends (also what automation screenshots capture).
        if (!cancelled && generations > 0)
        {
            _chart.Select(generations - 1, _lastBestIndex);
            if (_autoFavorite)
            {
                AddSelectionToGames();
            }
        }

        string shot = OS.GetEnvironment("BRAWLER_SHOT");
        if (shot.Length > 0 && OS.GetEnvironment("BRAWLER_AUTOEVOLVE").Length > 0)
        {
            _ = CaptureAndQuit(shot);
        }
    }

    // ── Evolution Explorer: selection → preview → basket ──────────────────────────

    private void OnPointSelected(int gen, int index, float score, GameGenome genome)
    {
        string runName = _runName.Text.Trim().Length > 0 ? _runName.Text.Trim() : "unnamed";
        var record = new GameRecord(
            $"{runName}-g{gen}-game{index}",
            $"evolve-explorer:{runName} gen {gen} game {index} fitness {score:F1}",
            genome);
        _selection = (gen, index, score, genome);
        _previewInfo.Text = $"GEN {gen} · GAME {index} · FITNESS {score:F1}";
        _addToGames.Disabled = false;
        _savedNote.Text = "";
        _preview.ShowGame(record, firstSeed: (ulong)(gen * 1000 + index + 1));
    }

    private void ClearSelection()
    {
        _selection = null;
        _preview.Stop();
        _previewInfo.Text = "click a chart point to preview that game";
        _previewSeed.Text = "";
        _addToGames.Disabled = true;
        _savedNote.Text = "";
    }

    /// <summary>ADD TO GAMES (the basket): saves the selected genome as a game.json
    /// in the favorites library, which the game picker lists first.</summary>
    private void AddSelectionToGames()
    {
        if (_selection is not { } sel)
        {
            return;
        }
        string runName = _runName.Text.Trim().Length > 0 ? _runName.Text.Trim() : "unnamed";
        string baseName = Sanitize($"{runName}-g{sel.Gen}-game{sel.Index}");
        var record = new GameRecord(
            baseName,
            $"evolve-explorer:{runName} gen {sel.Gen} game {sel.Index} fitness {sel.Score:F1}",
            sel.Genome);
        string dir = AppPaths.FavoritesRoot();
        string path = System.IO.Path.Combine(dir, baseName + ".json");
        for (int n = 2; System.IO.File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(dir, $"{baseName}-{n}.json");
        }
        GameGenomeJson.Save(record, path);
        _savedNote.Text = $"ADDED ✓  {System.IO.Path.GetFileName(path)}";
    }

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '-');
        }
        return name;
    }

    private async Task CaptureAndQuit(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot saved: {path}");
        GetTree().Quit();
    }

    private void WatchBest()
    {
        MatchSession.Game = GameGenomeJson.Load(System.IO.Path.Combine(_runDir, "best.json"));
        MatchSession.Mode = MatchMode.Replay;
        MatchSession.Trace = BrawlerSim.Replay.InputTraceJson.Load(System.IO.Path.Combine(_runDir, "best.trace.json"));
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    /// <summary>Collects composition mode + advanced range rows into the run's
    /// GenerationConfig. PINNED with untouched rows = GenerationConfig.Default —
    /// the byte-identical legacy path.</summary>
    private GenerationConfig BuildGenerationConfig()
    {
        GenerationConfig generation = GenerationConfig.Default with
        {
            CharacterCount = _numPlayers.Selected + 2, // 2026-08-12 four-player
        };
        if (_compositionMode.Selected == 1)
        {
            generation = generation with { ButtonComposition = GenerationConfig.RandomComposition };
        }
        else if (_compositionMode.Selected == 2)
        {
            generation = generation with
            {
                ButtonComposition = _buttonSlots.Select(s => (SlotSpec)s.Selected).ToArray(),
            };
        }
        var overrides = new System.Collections.Generic.List<RangeOverride>();
        foreach (RangeRow row in _rangeRows)
        {
            float min = (float)row.Min.Value, max = (float)row.Max.Value;
            if (min != row.Stock.Min || max != row.Stock.Max)
            {
                overrides.Add(new RangeOverride(row.Schema, row.Stock.Key, System.MathF.Min(min, max), System.MathF.Max(min, max)));
            }
        }
        return overrides.Count > 0 ? generation.WithRangeOverrides(overrides) : generation;
    }

    private void ApplyAutoConfig(string spec)
    {
        foreach (string pair in spec.Split(';'))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "name": _runName.Text = kv[1]; break;
                case "pop": _population.Value = double.Parse(kv[1]); break;
                case "gens": _generations.Value = double.Parse(kv[1]); break;
                case "seed": _seed.Value = double.Parse(kv[1]); break;
                case "rounds": _rounds.Value = double.Parse(kv[1]); break;
                case "players": _numPlayers.Selected = int.Parse(kv[1]) - 2; break; // 2026-08-12
                case "composition": // pinned|random|perbutton (headless UI verification)
                    _compositionMode.Selected = kv[1] switch
                        { "random" => 1, "perbutton" => 2, _ => 0 };
                    OnCompositionModeChanged(_compositionMode.Selected);
                    break;
                case "advanced": // any value: open the advanced panel for screenshots
                    ToggleAdvanced();
                    break;
                case "favorite": // =1: ADD TO GAMES on the auto-selected best (automation)
                    _autoFavorite = kv[1] == "1";
                    break;
            }
        }
    }

    private void BuildUi()
    {
        var root = new HBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 24f, OffsetTop = 24f, OffsetRight = -24f, OffsetBottom = -24f,
        };
        root.AddThemeConstantOverride("separation", 24);
        AddChild(root);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(360f, 0f) };
        left.AddThemeConstantOverride("separation", 8);
        root.AddChild(left);

        var title = new Label { Text = "EVOLVE" };
        title.AddThemeFontSizeOverride("font_size", 34);
        left.AddChild(title);

        _runName = new LineEdit { Text = "run-1", PlaceholderText = "run name" };
        left.AddChild(Labeled("run name", _runName));
        _seed = Spin(1, 1, 999_999); left.AddChild(Labeled("seed", _seed));
        _population = Spin(100, 4, 500); left.AddChild(Labeled("population", _population));
        _generations = Spin(100, 1, 5000); left.AddChild(Labeled("generations", _generations));
        _rounds = Spin(1, 1, 9); left.AddChild(Labeled("rounds / individual", _rounds));
        _mutation = Slider(0.4f); left.AddChild(Labeled("mutation rate", _mutation));
        _dropout = Slider(0.5f); left.AddChild(Labeled("dropout rate", _dropout));

        // Four Player Support (2026-08-12): each game holds 2-4 characters; runs past
        // two players score under ffa-v1 automatically (run.json records both).
        _numPlayers = new OptionButton();
        _numPlayers.AddItem("2 PLAYERS", 0);
        _numPlayers.AddItem("3 PLAYERS", 1);
        _numPlayers.AddItem("4 PLAYERS", 2);
        _numPlayers.Selected = 0;
        left.AddChild(Labeled("num players", _numPlayers));

        // Composition (2026-07-14): PINNED = today's fixed attack/attack/shield/dash;
        // RANDOM = every button free; PER-BUTTON = pin some, free others.
        _compositionMode = new OptionButton();
        _compositionMode.AddItem("PINNED (ATTACK/ATTACK/SHIELD/DASH)", 0);
        _compositionMode.AddItem("RANDOMIZED (TYPES EVOLVE)", 1);
        _compositionMode.AddItem("PER-BUTTON", 2);
        _compositionMode.Selected = 0;
        _compositionMode.ItemSelected += i => OnCompositionModeChanged((int)i);
        left.AddChild(Labeled("composition", _compositionMode));

        _perButtonRow = new HBoxContainer { Visible = false };
        _perButtonRow.AddThemeConstantOverride("separation", 4);
        // 2026-07-20 five buttons: U (pad Y) is the new slot 3; L (R1) stays LAST.
        string[] buttonNames = { "I", "J", "K", "U", "L" };
        for (int b = 0; b < _buttonSlots.Length; b++)
        {
            var slot = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            // Item order mirrors SlotSpec numerically (BuildGenerationConfig casts).
            slot.AddItem("ATTACK", 0);
            slot.AddItem("SHIELD", 1);
            slot.AddItem("DASH", 2);
            slot.AddItem("PROJECTILE", 3); // 2026-07-14
            slot.AddItem("RANDOM", 4);
            // Seed from the pinned layout (attack/attack/shield/attack/dash).
            slot.Selected = b switch { 2 => 1, 4 => 2, _ => 0 };
            _buttonSlots[b] = slot;
            var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var name = new Label { Text = buttonNames[b], HorizontalAlignment = HorizontalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 12);
            cell.AddChild(name);
            cell.AddChild(slot);
            _perButtonRow.AddChild(cell);
        }
        left.AddChild(_perButtonRow);

        _advancedToggle = new Button { Text = "ADVANCED: PARAMETER RANGES", ToggleMode = true };
        _advancedToggle.Pressed += ToggleAdvanced;
        left.AddChild(_advancedToggle);

        _start = new Button { Text = "START RUN" };
        _start.Pressed += StartRun;
        left.AddChild(_start);
        var stop = new Button { Text = "STOP (keeps checkpoint)" };
        stop.Pressed += () => _cancel?.Cancel();
        left.AddChild(stop);
        _watchBest = new Button { Text = "WATCH BEST (graded match)", Disabled = true };
        _watchBest.Pressed += WatchBest;
        left.AddChild(_watchBest);
        var back = new Button { Text = "BACK" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        left.AddChild(back);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        root.AddChild(right);
        _chart = new FitnessChart { SizeFlagsVertical = SizeFlags.ExpandFill };
        _chart.PointSelected += OnPointSelected;
        right.AddChild(_chart);
        _advancedPanel = BuildAdvancedPanel();
        right.AddChild(_advancedPanel);
        // ClipText: the run-dir path is long — without clipping its min width pushes
        // the preview column off screen.
        _status = new Label
        {
            Text = "configure a run and press START",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _status.AddThemeFontSizeOverride("font_size", 14);
        right.AddChild(_status);

        _previewPanel = BuildPreviewPanel();
        root.AddChild(_previewPanel);
    }

    /// <summary>The Evolution Explorer column (2026-07-27): live match preview of the
    /// clicked chart point + the ADD TO GAMES basket.</summary>
    private VBoxContainer BuildPreviewPanel()
    {
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(392f, 0f) };
        panel.AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = "PREVIEW" };
        title.AddThemeFontSizeOverride("font_size", 22);
        panel.AddChild(title);

        _previewInfo = new Label
        {
            Text = "click a chart point to preview that game",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _previewInfo.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_previewInfo);

        var container = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(392f, 220f), // 16:9 mini arena
        };
        var viewport = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(viewport);
        _preview = new MatchPreview();
        _preview.MatchChanged += () =>
            _previewSeed.Text = $"AI vs AI · match seed {_preview.CurrentSeed} · new matches loop live";
        viewport.AddChild(_preview);
        panel.AddChild(container);

        _previewSeed = new Label { Text = "", Modulate = new Color(0.6f, 0.65f, 0.72f) };
        _previewSeed.AddThemeFontSizeOverride("font_size", 12);
        panel.AddChild(_previewSeed);

        _addToGames = new Button { Text = "ADD TO GAMES", Disabled = true };
        _addToGames.Pressed += AddSelectionToGames;
        panel.AddChild(_addToGames);

        _savedNote = new Label { Text = "", Modulate = new Color(0.5f, 0.9f, 0.6f) };
        _savedNote.AddThemeFontSizeOverride("font_size", 13);
        panel.AddChild(_savedNote);

        var hint = new Label
        {
            Text = "favorited games appear first in the PLAY/WATCH game picker",
            Modulate = new Color(0.5f, 0.55f, 0.65f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        panel.AddChild(hint);

        return panel;
    }

    private void OnCompositionModeChanged(int mode)
    {
        _perButtonRow.Visible = mode == 2;
    }

    /// <summary>The advanced panel swaps with the chart (same slot on the right) so
    /// the range grid gets full height; the run keeps drawing to the chart underneath
    /// and reappears when the panel is toggled off.</summary>
    private void ToggleAdvanced()
    {
        bool show = !_advancedPanel.Visible;
        _advancedPanel.Visible = show;
        _chart.Visible = !show;
        _advancedToggle.ButtonPressed = show;
    }

    private ScrollContainer BuildAdvancedPanel()
    {
        var scroll = new ScrollContainer
        {
            Visible = false,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);

        var heading = new Label { Text = "GENERATION RANGES — EDITS APPLY TO NEW RUNS AND ARE RECORDED IN RUN.JSON" };
        heading.AddThemeFontSizeOverride("font_size", 14);
        list.AddChild(heading);
        var note = new Label
        {
            Text = "CLAMP A PARAMETER BY SETTING MIN = MAX. AMBER = OUTSIDE THE TESTED DOMAIN.",
            Modulate = new Color(0.7f, 0.7f, 0.75f),
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        list.AddChild(note);

        var reset = new Button { Text = "RESET ALL TO DEFAULTS" };
        reset.Pressed += () =>
        {
            foreach (RangeRow row in _rangeRows)
            {
                row.Min.SetValueNoSignal(row.Stock.Min);
                row.Max.SetValueNoSignal(row.Stock.Max);
                UpdateRowWarning(row);
            }
        };
        list.AddChild(reset);

        foreach ((string name, ParamSchema schema) in new[]
        {
            ("character", DefaultSchemas.Character),
            ("move", DefaultSchemas.Move),
            ("shield", DefaultSchemas.Shield),
            ("dash", DefaultSchemas.Dash),
            ("projectile", DefaultSchemas.Projectile), // 2026-07-22 (designer)
            ("stage", DefaultSchemas.Stage), // 2026-07-21 Map Size
        })
        {
            var section = new Label { Text = name.ToUpperInvariant() };
            section.AddThemeFontSizeOverride("font_size", 16);
            list.AddChild(section);
            foreach (ParamSpec spec in schema.Specs)
            {
                list.AddChild(BuildRangeRow(name, spec));
            }
        }
        return scroll;
    }

    private Control BuildRangeRow(string schema, ParamSpec spec)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = spec.Key, CustomMinimumSize = new Vector2(230f, 0f) };
        label.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(label);
        SpinBox min = RangeSpin(spec.Min);
        SpinBox max = RangeSpin(spec.Max);
        row.AddChild(min);
        row.AddChild(max);
        var warning = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(200f, 0f),
            Modulate = new Color(1f, 0.75f, 0.25f),
        };
        warning.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(warning);

        var rangeRow = new RangeRow { Schema = schema, Stock = spec, Min = min, Max = max, Warning = warning };
        _rangeRows.Add(rangeRow);
        min.ValueChanged += _ => UpdateRowWarning(rangeRow);
        max.ValueChanged += _ => UpdateRowWarning(rangeRow);
        return row;
    }

    private static void UpdateRowWarning(RangeRow row)
    {
        float min = (float)row.Min.Value, max = (float)row.Max.Value;
        bool edited = min != row.Stock.Min || max != row.Stock.Max;
        bool outside = min < row.Stock.EffectiveValidMin || max > row.Stock.EffectiveValidMax;
        row.Warning.Text = outside ? "OUTSIDE TESTED DOMAIN" : edited ? (min == max ? "CLAMPED" : "EDITED") : "";
        row.Warning.Modulate = outside ? new Color(1f, 0.75f, 0.25f) : new Color(0.6f, 0.75f, 0.6f);
    }

    private static SpinBox RangeSpin(float value) => new()
    {
        MinValue = -10_000, MaxValue = 10_000, Step = 0.01, Value = value,
        AllowGreater = true, AllowLesser = true,
        CustomMinimumSize = new Vector2(110f, 0f),
    };

    private static SpinBox Spin(double value, double min, double max) =>
        new() { MinValue = min, MaxValue = max, Value = value, CustomMinimumSize = new Vector2(140f, 0f) };

    private static HSlider Slider(float value) =>
        new() { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(140f, 20f) };

    private static Control Labeled(string text, Control control)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(170f, 0f) };
        label.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(label);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);
        return row;
    }
}
