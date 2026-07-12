using System.Linq;
using Godot;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Replay;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// The match scene. THE determinism-contract boundary: this class owns a SimWorld,
/// advances it exactly one Tick per physics frame with InputFrames from the sources,
/// and mirrors the resulting state into nodes. It never mutates gameplay state itself.
/// Every match records an InputTrace, saved on completion — human games become replays
/// automatically.
/// </summary>
public partial class ArenaView : Node2D
{
    private const float Ppu = 72f; // Unity camera: ortho size 5 → 10 world units tall at 720 px

    private SimWorld _world = null!;
    private IInputSource[] _sources = null!;
    private InputTrace _trace = null!;
    private readonly InputFrame[] _inputs = new InputFrame[2];
    private PlayerView[] _views = null!;
    private HudView _hud = null!;
    private bool _paused;
    private bool _traceSaved;

    // Screenshot automation (BRAWLER_SHOT_DIR + BRAWLER_SHOT_TICKS): capture at the
    // given sim ticks, plus at match end, then quit.
    private string _shotDir = "";
    private System.Collections.Generic.Queue<int> _shotTicks = new();

    public override void _Ready()
    {
        MatchSession.Game ??= new BrawlerSim.Serialization.GameRecord(
            "generated", "editor-run",
            BrawlerSim.Genome.GameGenome.Generate(
                BrawlerSim.Genome.GenerationConfig.Default, new Pcg32(1)));

        _world = new SimWorld(MatchSession.Game.Genome);
        _sources = BuildSources();
        _trace = new InputTrace();

        Position = GetViewportRect().Size / 2f;

        var stage = new StageView();
        AddChild(stage);
        stage.Setup(_world, Ppu);

        _views = new PlayerView[2];
        for (int i = 0; i < 2; i++)
        {
            var view = new PlayerView();
            AddChild(view);
            var character = MatchSession.Game.Genome.Characters[i];
            view.Setup(_world.Players[i], character.SpriteIndex,
                character.Moves.Select(m => m.SpriteIndex).ToArray(), Ppu);
            view.Sync();
            _views[i] = view;
        }

        _hud = new HudView();
        AddChild(_hud);
        _hud.Setup(_world);
        _hud.Sync(paused: false);

        _shotDir = OS.GetEnvironment("BRAWLER_SHOT_DIR");
        string ticks = OS.GetEnvironment("BRAWLER_SHOT_TICKS");
        if (_shotDir.Length > 0 && ticks.Length > 0)
        {
            foreach (string tick in ticks.Split(','))
            {
                _shotTicks.Enqueue(int.Parse(tick));
            }
        }
        string fastForward = OS.GetEnvironment("BRAWLER_TICKS_PER_FRAME");
        if (fastForward.Length > 0)
        {
            _ticksPerFrame = int.Parse(fastForward);
        }
    }

    /// <summary>Automation fast-forward: sim ticks per rendered frame (default 1 = real time).</summary>
    private int _ticksPerFrame = 1;

    public override void _PhysicsProcess(double delta)
    {
        if (_paused)
        {
            return;
        }
        if (_world.IsOver)
        {
            OnMatchOver();
            return;
        }

        for (int step = 0; step < _ticksPerFrame && !_world.IsOver; step++)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                _inputs[i] = _sources[i].GetInput(_world, i);
            }
            _trace.Record(_inputs);
            _world.Tick(_inputs);

            // Render the exact requested tick: stop fast-forwarding this frame so the
            // screenshot shows the state it names.
            if (_shotTicks.Count > 0 && _world.TickCount >= _shotTicks.Peek())
            {
                _ = CaptureAsync($"tick_{_shotTicks.Dequeue():D5}", quitWhenDone: false);
                break;
            }
        }

        foreach (PlayerView view in _views)
        {
            view.Sync();
        }
        _hud.Sync(_paused);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_pause"))
        {
            if (_paused && @event is InputEventKey { PhysicalKeycode: Key.Q, Pressed: true })
            {
                BackToMenu();
            }
            return;
        }
        if (_world.IsOver)
        {
            BackToMenu();
            return;
        }
        _paused = !_paused;
        _hud.Sync(_paused);
    }

    private void OnMatchOver()
    {
        _hud.Sync(_paused);
        if (!_traceSaved)
        {
            _traceSaved = true;
            SaveTrace();
            if (_shotDir.Length > 0)
            {
                _ = CaptureAsync("end", quitWhenDone: true);
            }
        }
    }

    private void SaveTrace()
    {
        string dir = ProjectSettings.GlobalizePath("user://replays");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "last_match.trace.json");
        InputTraceJson.Save(_trace, path);
        BrawlerSim.Serialization.GameGenomeJson.Save(
            MatchSession.Game!, System.IO.Path.Combine(dir, "last_match.game.json"));
        GD.Print($"match trace saved: {path} ({_trace.TickCount} ticks, hash {_world.StateHash()})");
    }

    private async System.Threading.Tasks.Task CaptureAsync(string name, bool quitWhenDone)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = System.IO.Path.Combine(_shotDir, $"{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"shot saved: {path}");
        if (quitWhenDone)
        {
            GetTree().Quit();
        }
    }

    private void BackToMenu()
    {
        MatchSession.Trace = null;
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }

    private IInputSource[] BuildSources() => MatchSession.Mode switch
    {
        MatchMode.HumanVsHuman => new IInputSource[]
        {
            new HumanInputSource(1, ShieldHoldMask(0)),
            new HumanInputSource(2, ShieldHoldMask(1)),
        },
        MatchMode.HumanVsCpu => new IInputSource[]
        {
            new HumanInputSource(1, ShieldHoldMask(0)),
            AgentConfig.Default.CreateSource(new Pcg32(MatchSession.AiSeed, 1)),
        },
        MatchMode.AiVsAi => new IInputSource[]
        {
            AgentConfig.Default.CreateSource(new Pcg32(MatchSession.AiSeed, 0)),
            AgentConfig.Default.CreateSource(new Pcg32(MatchSession.AiSeed, 1)),
        },
        MatchMode.Replay => ReplaySources(),
        _ => throw new System.InvalidOperationException($"Unknown mode {MatchSession.Mode}"),
    };

    /// <summary>Which of this character's buttons map to shield moves (hold semantics).</summary>
    private bool[] ShieldHoldMask(int playerIndex)
    {
        var character = MatchSession.Game!.Genome.Characters[playerIndex];
        var mask = new bool[BrawlerSim.Sim.InputFrame.ActionCount];
        for (int b = 0; b < mask.Length; b++)
        {
            mask[b] = character.Moves[character.ButtonMoves[b]].Type == BrawlerSim.Genome.MoveType.Shield;
        }
        return mask;
    }

    private static IInputSource[] ReplaySources()
    {
        var source = new TraceInputSource(MatchSession.Trace!);
        return new IInputSource[] { source, source };
    }
}
