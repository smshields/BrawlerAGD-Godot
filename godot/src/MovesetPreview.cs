using System.Linq;
using Godot;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// The character-select pane's live moveset demo (2026-08-14, Game Player spec:
/// "the character performs all available actions in the pane"). A REAL mini-sim,
/// not an animation: the fighter stands on a synthetic platform and a scripted
/// input source cycles jump and every mapped button — shield buttons held, the
/// rest pressed — so state tints, wind-up telegraphs, shield circles, dash
/// strobes, and projectiles render exactly as in a match. The sim needs two
/// characters, so a twin is parked on a far-away perch outside the camera crop
/// (TIMED rule: nobody can be eliminated). View-only; the world is private.
/// </summary>
public partial class MovesetPreview : SubViewportContainer
{
    private const float Ppu = 16f;

    private SimWorld? _world;
    private IInputSource? _script;
    private Node2D _root = null!;
    private PlayerView _performer = null!;
    private SubViewport _viewport = null!;

    public void Setup(CharacterGenome character, string displayName)
    {
        // The performer carries the built game's display name so the in-world tag
        // matches the pane; the parked twin gets a blank tag.
        character = new CharacterGenome(displayName, character.Stocks, character.SpriteIndex,
            character.Params, character.Moves, character.ButtonMoves);
        Stretch = true;
        _viewport = new SubViewport
        {
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };
        AddChild(_viewport);

        // A wide performance floor plus a far-off perch for the parked twin — the
        // spawn genes put the performer center stage and the twin on the perch.
        var platforms = new[]
        {
            new PlatformGene(-7, -4, 14, 1),
            new PlatformGene(28, -4, 3, 1),
        };
        var stage = new StageGenome(platforms, StageRules.LegacyParams(platforms).With(
            (StageParams.Spawn1X, 0f), (StageParams.Spawn1Y, -1f),
            (StageParams.Spawn2X, 29.5f), (StageParams.Spawn2Y, -1f)));
        var genome = new GameGenome(new[] { character, character }, stage);
        _world = new SimWorld(genome, MatchConfig.Default with
        {
            EndRule = MatchEndRule.Timed, // infinite stocks: the demo never ends
            MaxMatchSeconds = float.MaxValue / 4f,
        });
        _script = new ScriptedCycle(character);

        _root = new Node2D();
        _viewport.AddChild(_root);
        var stageView = new StageView();
        _root.AddChild(stageView);
        stageView.Setup(_world, Ppu);
        _performer = new PlayerView();
        _root.AddChild(_performer);
        _performer.Setup(_world.Players[0], character.SpriteIndex,
            character.Moves.Select(m => m.SpriteIndex).ToArray(), Ppu);
        var projectiles = new ProjectileLayer();
        _root.AddChild(projectiles);
        projectiles.Setup(_world, Ppu);
        _projectiles = projectiles;
    }

    private ProjectileLayer? _projectiles;

    public override void _PhysicsProcess(double delta)
    {
        if (_world is null || _script is null)
        {
            return;
        }
        System.Span<InputFrame> inputs = stackalloc[]
        {
            _script.GetInput(_world, 0),
            InputFrame.Neutral,
        };
        _world.Tick(inputs);
        _performer.Sync();
        _projectiles?.Sync();

        // Crop centered on the PERFORMER's body (2026-08-17, designer: the fighter
        // must sit centered in the pane), holding a fixed height above the floor.
        Vector2 size = _viewport.Size;
        _root.Position = new Vector2(size.X / 2f - _performer.Position.X, size.Y / 2f + 0.4f * Ppu);
    }

    /// <summary>Cycles: settle → jump → each mapped button (shields held, others
    /// pressed) with recovery gaps. Deterministic, loops forever.</summary>
    private sealed class ScriptedCycle : IInputSource
    {
        private readonly (int Ticks, InputFrame Frame)[] _timeline;
        private readonly int _total;

        public ScriptedCycle(CharacterGenome character)
        {
            var steps = new System.Collections.Generic.List<(int, InputFrame)>
            {
                (36, InputFrame.Neutral),
                (1, new InputFrame(0f, 0f, true, 0)),  // jump
                (50, InputFrame.Neutral),
            };
            for (int b = 0; b < InputFrame.ActionCount; b++)
            {
                bool hold = character.Moves[character.ButtonMoves[b]].Type == MoveType.Shield;
                if (hold)
                {
                    steps.Add((30, new InputFrame(0f, 0f, false, InputFrame.ActionBit(b))));
                }
                else
                {
                    steps.Add((1, new InputFrame(0f, 0f, false, InputFrame.ActionBit(b))));
                }
                steps.Add((60, InputFrame.Neutral));
            }
            _timeline = steps.ToArray();
            _total = steps.Sum(s => s.Item1);
        }

        public InputFrame GetInput(SimWorld world, int playerIndex)
        {
            int t = world.TickCount % _total;
            foreach ((int ticks, InputFrame frame) in _timeline)
            {
                if (t < ticks)
                {
                    return frame;
                }
                t -= ticks;
            }
            return InputFrame.Neutral;
        }
    }
}
