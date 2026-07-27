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
    private ProjectileLayer _projectiles = null!;
    private HudView _hud = null!;
    private ArenaCamera _camera = null!;
    private MinimapView _minimap = null!;
    private SpawnPadView _spawnPads = null!;
    private DeathFlashView _deathFlash = null!;
    private PauseMenuView _pauseMenu = null!;
    private readonly int[] _prevStocks = new int[2]; // KO edge detection for the flash
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

        _projectiles = new ProjectileLayer();
        AddChild(_projectiles);
        _projectiles.Setup(_world, Ppu);

        // Spawning Behaviors (2026-07-22): temporary spawn platforms under the players.
        _spawnPads = new SpawnPadView();
        AddChild(_spawnPads);
        _spawnPads.Setup(_world, Ppu);

        // Map Size (2026-07-21): the framing camera and the minimap overlay.
        _camera = new ArenaCamera();
        AddChild(_camera);
        _camera.Setup(_world, Ppu);

        _minimap = new MinimapView();
        AddChild(_minimap);
        _minimap.Setup(_world, _camera);

        // Death Animations (2026-07-22): edge-anchored KO flash overlay.
        _deathFlash = new DeathFlashView();
        AddChild(_deathFlash);
        for (int i = 0; i < 2; i++)
        {
            _prevStocks[i] = _world.Players[i].Stocks;
        }

        _hud = new HudView();
        AddChild(_hud);
        _hud.Setup(_world, MatchSession.Game.Genome);
        _hud.Sync(_inputs);

        // Pause menu (HUD polish, 2026-07-23): a real navigable menu replaces the
        // old text overlay; it owns the debug-panel toggle and SETTINGS.
        _pauseMenu = new PauseMenuView();
        AddChild(_pauseMenu);
        _pauseMenu.ResumeRequested += () => SetPaused(false);
        _pauseMenu.QuitRequested += BackToMenu;

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
        string pauseAt = OS.GetEnvironment("BRAWLER_PAUSE_AT");
        if (pauseAt.Length > 0)
        {
            _pauseAtTick = int.Parse(pauseAt); // automation: verify the pause menu
        }
    }

    private int _pauseAtTick = -1;

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

        // Snapshot buffer for the pre-tick death direction/speed/damage, so a KO
        // resolved by a tick (which resets the victim off-screen) can still fire its
        // flash. Hoisted out of the fast-forward loop (CA2014).
        System.Span<(BrawlerSim.Determinism.Vec2 Pos, BrawlerSim.Determinism.Vec2 Vel, float Dmg)> pre =
            stackalloc (BrawlerSim.Determinism.Vec2, BrawlerSim.Determinism.Vec2, float)[2];
        for (int step = 0; step < _ticksPerFrame && !_world.IsOver; step++)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                _inputs[i] = _sources[i].GetInput(_world, i);
            }
            _trace.Record(_inputs);
            for (int i = 0; i < 2; i++)
            {
                pre[i] = (_world.Players[i].Position, _world.Players[i].Velocity, _world.Players[i].Damage);
            }
            _world.Tick(_inputs);
            DetectDeaths(pre);
            if (_pauseAtTick >= 0 && _world.TickCount >= _pauseAtTick)
            {
                _pauseAtTick = -1;
                SetPaused(true);
                if (_shotDir.Length > 0)
                {
                    _ = CaptureAsync("paused", quitWhenDone: false);
                }
                break;
            }

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
        _projectiles.Sync();
        _spawnPads.Sync();
        _camera.Sync((float)delta);
        _minimap.Sync();
        _hud.Sync(_inputs);
    }

    /// <summary>Fire the death flash when a player lost a stock or the match ended by
    /// KO this tick, using the PRE-tick snapshot (the victim is already reset/absent by
    /// now). Direction points from the death point toward arena center; speed/damage
    /// normalize to the flash's scale inputs.</summary>
    private void DetectDeaths(
        System.ReadOnlySpan<(BrawlerSim.Determinism.Vec2 Pos, BrawlerSim.Determinism.Vec2 Vel, float Dmg)> pre)
    {
        for (int i = 0; i < 2; i++)
        {
            bool lostStock = _world.Players[i].Stocks < _prevStocks[i];
            bool finalKo = _world.IsOver && _world.LoserIndex == i;
            if (lostStock || finalKo)
            {
                TriggerDeathFlash(pre[i].Pos, pre[i].Vel, pre[i].Dmg, _world.Players[i].BodyHalf.X);
            }
            _prevStocks[i] = _world.Players[i].Stocks;
        }
    }

    /// <summary>Build the death streak — always on screen (2026-07-23 designer fix).
    /// The death point is mapped into CAMERA view fractions (unclamped): off one axis
    /// → the streak sits on that screen edge pointing perpendicularly inward
    /// (bottom→up, right→left); off BOTH axes (past a corner of the view) → the
    /// streak sits in that corner pointing diagonally toward the camera center;
    /// still inside the view (a visible blast edge) → it fires from the actual death
    /// point, perpendicular to the crossed blast edge. Width is capped below the
    /// character; intensity scales with KO speed + damage.</summary>
    private void TriggerDeathFlash(
        BrawlerSim.Determinism.Vec2 pos, BrawlerSim.Determinism.Vec2 vel, float dmg, float bodyHalfX)
    {
        BrawlerSim.Sim.Aabb view = _camera.ViewWorldRect;
        float fx = view.Right > view.Left
            ? (pos.X - view.Left) / (view.Right - view.Left) : 0.5f;
        float fy = view.Top > view.Bottom
            ? (view.Top - pos.Y) / (view.Top - view.Bottom) : 0.5f;
        bool offX = fx < 0f || fx > 1f;
        bool offY = fy < 0f || fy > 1f;
        var anchor = new Vector2(Mathf.Clamp(fx, 0f, 1f), Mathf.Clamp(fy, 0f, 1f));

        Vector2 inward;
        if (offX && offY)
        {
            // Past a corner of the view: point diagonally at the camera center,
            // normalized in PIXELS so the angle is true on the 16:9 viewport.
            Vector2 size = GetViewportRect().Size;
            inward = new Vector2((0.5f - anchor.X) * size.X, (0.5f - anchor.Y) * size.Y).Normalized();
        }
        else if (offX)
        {
            inward = fx > 1f ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
        }
        else if (offY)
        {
            inward = fy > 1f ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
        }
        else
        {
            // Death point visible on screen (the blast edge is inside the view):
            // fire from the point itself, perpendicular to the crossed blast edge.
            var blast = _world.BlastZone.Half;
            bool crossedX = blast.X > 0f && blast.Y > 0f
                && System.MathF.Abs(pos.X) / blast.X >= System.MathF.Abs(pos.Y) / blast.Y;
            inward = crossedX
                ? (pos.X >= 0f ? new Vector2(-1f, 0f) : new Vector2(1f, 0f))
                : (pos.Y >= 0f ? new Vector2(0f, 1f) : new Vector2(0f, -1f));
        }

        float widthPx = bodyHalfX * 2f * Ppu * _camera.Zoom.X * 0.8f; // ≤ the character
        float speed = System.MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        float intensity = Mathf.Clamp(0.3f + 0.45f * (speed / 30f) + 0.25f * (dmg / 150f), 0f, 1f);
        _deathFlash.Trigger(anchor, inward, widthPx, intensity);
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
        SetPaused(!_paused);
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            _pauseMenu.Open();
        }
        else
        {
            _pauseMenu.Close();
        }
    }

    private void OnMatchOver()
    {
        _hud.Sync(_inputs);
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
