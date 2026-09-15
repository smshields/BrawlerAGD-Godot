using System.Linq;
using Godot;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Live match preview (Evolution Explorer, 2026-07-27): a miniature arena that plays
/// CONTINUOUS AI-vs-AI matches on one game inside a SubViewport — used by the evolve
/// dashboard's point preview. Reuses the real view components (StageView, PlayerView,
/// ProjectileLayer, SpawnPadView, ArenaCamera) over a private SimWorld; each finished
/// match lingers briefly, then the next one starts on the next seed. View-layer only:
/// no traces are recorded, MatchSession is untouched, and nothing here can influence
/// the evolution run that spawned it.
/// </summary>
public partial class MatchPreview : Node2D
{
    private const float Ppu = 72f;
    private const int RestartDelayFrames = 90; // linger ~1.5 s on the end state

    private GameRecord? _record;
    private ulong _seed;
    private SimWorld? _world;
    private IInputSource[] _sources = System.Array.Empty<IInputSource>();
    private InputFrame[] _inputs = new InputFrame[2]; // resized per game (2026-08-12)
    private PlayerView[] _views = System.Array.Empty<PlayerView>();
    private GroundFxView _groundFx = null!;
    private GroundFxDetector? _groundFxDetector;
    private ProjectileLayer _projectiles = null!;
    private SpawnPadView _spawnPads = null!;
    private ArenaCamera _camera = null!;
    private int _restartCountdown;

    /// <summary>The seed of the match currently playing (shown in the info line).</summary>
    public ulong CurrentSeed => _seed;

    /// <summary>Fires when a match ends or a new one starts (info line refresh).</summary>
    public System.Action? MatchChanged;

    /// <summary>When set, consulted at each match end for the next game to play
    /// (main-menu backdrop rotation, 2026-09-10); a null result keeps the current
    /// game. Unset (the evolve preview), the same game loops on the next seed.</summary>
    public System.Func<GameRecord?>? NextGame;

    public void ShowGame(GameRecord record, ulong firstSeed)
    {
        _record = record;
        _seed = firstSeed;
        Rebuild();
    }

    public void Stop()
    {
        _record = null;
        _world = null;
        UiWidgets.ClearChildren(this);
    }

    private void Rebuild()
    {
        UiWidgets.ClearChildren(this);
        if (_record is null)
        {
            return;
        }

        _world = new SimWorld(_record.Genome);
        int players = _world.Players.Count; // 2-4 since 2026-08-12
        _inputs = new InputFrame[players];
        _sources = AgentConfig.Default.CreateSources(_seed, players);
        _restartCountdown = RestartDelayFrames;

        Position = GetViewportRect().Size / 2f;

        // Backdrop (designer 2026-09-02): the evolve preview shows the stage's real
        // look. Added first (draw order), set up after the camera exists below.
        var background = new BackgroundView();
        AddChild(background);

        var stage = new StageView();
        AddChild(stage);
        stage.Setup(_world, Ppu, _record.Genome.Stage); // themed tiles (M4d)

        // Ground FX (2026-09-14, designer: previews included): theme-colored dust,
        // no weather plan here — the preview stack carries no WeatherSystem.
        _groundFx = new GroundFxView();
        AddChild(_groundFx);
        _groundFx.Setup(Ppu, _record.Genome.Stage, weatherPlan: null);
        _groundFxDetector = new GroundFxDetector(_world);

        _views = new PlayerView[players];
        for (int i = 0; i < players; i++)
        {
            var view = new PlayerView();
            AddChild(view);
            SetupPlayerView(i, view);
            _views[i] = view;
        }

        _projectiles = new ProjectileLayer();
        AddChild(_projectiles);
        _projectiles.Setup(_world, Ppu);

        _spawnPads = new SpawnPadView();
        AddChild(_spawnPads);
        _spawnPads.Setup(_world, Ppu);

        _camera = new ArenaCamera();
        AddChild(_camera);
        _camera.Setup(_world, Ppu);
        background.Setup(Ppu, _record.Genome.Stage, _camera);

        MatchChanged?.Invoke();
    }

    private void SetupPlayerView(int i, PlayerView view)
    {
        var character = _record!.Genome.Characters[i];
        view.Setup(_world!.Players[i], character, Ppu);
        view.Sync();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_world is null)
        {
            return;
        }
        if (_world.IsOver)
        {
            // Hold the final frame briefly, then play the next seed.
            if (--_restartCountdown <= 0)
            {
                if (NextGame?.Invoke() is { } next)
                {
                    _record = next;
                }
                _seed++;
                Rebuild();
                return;
            }
        }
        else
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                _inputs[i] = _sources[i].GetInput(_world, i);
            }
            _groundFxDetector?.BeforeTick();
            _world.Tick(_inputs);
            _groundFxDetector?.AfterTick();
        }

        foreach (PlayerView view in _views)
        {
            view.Sync();
        }
        _projectiles.Sync();
        _spawnPads.Sync();
        _camera.Sync((float)delta);
        _groundFxDetector?.Drain(_groundFx, weatherGate: 0f);
    }
}
