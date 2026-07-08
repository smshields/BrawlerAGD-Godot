using Godot;
using System.Threading;
using System.Threading.Tasks;
using BrawlerSim.Evolution;
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

    private CancellationTokenSource? _cancel;
    private string _runDir = "";
    private ulong _startTimeMs;

    public override void _Ready()
    {
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
        };
        int generations = (int)_generations.Value;
        _runDir = System.IO.Path.Combine(AppPaths.RunsRoot(), _runName.Text.Trim().Length > 0 ? _runName.Text.Trim() : "unnamed");

        _chart.Clear();
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
                CallDeferred(nameof(OnGeneration), stats.Generation, stats.TopFitness, stats.AverageFitness);
            }
            CallDeferred(nameof(OnRunFinished), engine.GenerationsCompleted, token.IsCancellationRequested);
        }, token);
    }

    private void OnGeneration(int generation, float top, float average)
    {
        _chart.AddPoint(top, average);
        float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
        _status.Text = $"gen {generation}   top {top:F1}   avg {average:F1}   {elapsed:F1}s   → {_runDir}";
    }

    private void OnRunFinished(int generations, bool cancelled)
    {
        float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
        _status.Text = (cancelled ? "stopped" : "done") +
            $" — {generations} generations in {elapsed:F1}s — saved to {_runDir}";
        _start.Disabled = false;
        _watchBest.Disabled = !System.IO.File.Exists(System.IO.Path.Combine(_runDir, "best.json"));

        string shot = OS.GetEnvironment("BRAWLER_SHOT");
        if (shot.Length > 0 && OS.GetEnvironment("BRAWLER_AUTOEVOLVE").Length > 0)
        {
            _ = CaptureAndQuit(shot);
        }
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
        right.AddChild(_chart);
        _status = new Label { Text = "configure a run and press START" };
        _status.AddThemeFontSizeOverride("font_size", 14);
        right.AddChild(_status);
    }

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
