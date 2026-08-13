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
    private ProjectileLayer _projectiles = null!;
    private SpawnPadView _spawnPads = null!;
    private ArenaCamera _camera = null!;
    private int _restartCountdown;

    /// <summary>The seed of the match currently playing (shown in the info line).</summary>
    public ulong CurrentSeed => _seed;

    /// <summary>Fires when a match ends or a new one starts (info line refresh).</summary>
    public System.Action? MatchChanged;

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
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
        if (_record is null)
        {
            return;
        }

        _world = new SimWorld(_record.Genome);
        int players = _world.Players.Count; // 2-4 since 2026-08-12
        _inputs = new InputFrame[players];
        _sources = new IInputSource[players];
        for (int i = 0; i < players; i++)
        {
            _sources[i] = AgentConfig.Default.CreateSource(new Pcg32(_seed, (ulong)i));
        }
        _restartCountdown = RestartDelayFrames;

        Position = GetViewportRect().Size / 2f;

        var stage = new StageView();
        AddChild(stage);
        stage.Setup(_world, Ppu);

        _views = new PlayerView[players];
        for (int i = 0; i < players; i++)
        {
            var view = new PlayerView();
            AddChild(view);
            CharacterGenomeView(i, view);
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

        MatchChanged?.Invoke();
    }

    private void CharacterGenomeView(int i, PlayerView view)
    {
        var character = _record!.Genome.Characters[i];
        view.Setup(_world!.Players[i], character.SpriteIndex,
            character.Moves.Select(m => m.SpriteIndex).ToArray(), Ppu);
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
            _world.Tick(_inputs);
        }

        foreach (PlayerView view in _views)
        {
            view.Sync();
        }
        _projectiles.Sync();
        _spawnPads.Sync();
        _camera.Sync((float)delta);
    }
}
