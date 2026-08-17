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
/// 2026-08-17 (designer): a KEY→MOVE legend overlays the demo — each control
/// (device-correct keycaps for humans, none for CPUs) with its move name in the
/// debug strip's vocabulary, the row lighting up while its move is demoed.
/// </summary>
public partial class MovesetPreview : SubViewportContainer
{
    private const float Ppu = 16f;

    private SimWorld? _world;
    private ScriptedCycle? _script;
    private Node2D _root = null!;
    private PlayerView _performer = null!;
    private SubViewport _viewport = null!;
    private Label[] _legend = System.Array.Empty<Label>();

    public void Setup(CharacterGenome character, string displayName,
        string? jumpCap = null, string[]? actionCaps = null)
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

        BuildLegend(character, jumpCap, actionCaps);
    }

    private ProjectileLayer? _projectiles;

    /// <summary>The key→move legend (2026-08-17): JUMP + the five action buttons,
    /// keycap (when the pane has a device) + the move's debug-strip name; the row
    /// being demoed lights up. Drawn over the viewport's top-left corner.</summary>
    private void BuildLegend(CharacterGenome character, string? jumpCap, string[]? actionCaps)
    {
        var box = new VBoxContainer
        {
            Position = new Vector2(6f, 4f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 0);
        AddChild(box);

        _legend = new Label[1 + InputFrame.ActionCount];
        for (int row = 0; row < _legend.Length; row++)
        {
            string cap = row == 0
                ? jumpCap ?? ""
                : actionCaps is not null && row - 1 < actionCaps.Length ? actionCaps[row - 1] : "";
            string move = row == 0
                ? "JUMP"
                : HudView.MoveAbbrev(character, character.ButtonMoves[row - 1]);
            var label = new Label
            {
                Text = cap.Length > 0 ? $"{cap,-3} {move}" : move,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 10);
            label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            label.AddThemeConstantOverride("outline_size", 3);
            _legend[row] = label;
            box.AddChild(label);
        }
    }

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

        // Light the legend row whose move is being demoed (press + its recovery).
        int active = _script.ActiveSlot(_world.TickCount);
        for (int row = 0; row < _legend.Length; row++)
        {
            _legend[row].Modulate = row == active
                ? Colors.White
                : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    /// <summary>Cycles: settle → jump → each mapped button (shields held, others
    /// pressed) with recovery gaps. Deterministic, loops forever. Each step carries
    /// the legend slot it demos (-1 settle, 0 jump, 1+b action button b) — a press's
    /// recovery gap belongs to its move so the highlight is readable.</summary>
    private sealed class ScriptedCycle : IInputSource
    {
        private readonly (int Ticks, InputFrame Frame, int Slot)[] _timeline;
        private readonly int _total;

        public ScriptedCycle(CharacterGenome character)
        {
            var steps = new System.Collections.Generic.List<(int, InputFrame, int)>
            {
                (36, InputFrame.Neutral, -1),
                (1, new InputFrame(0f, 0f, true, 0), 0),  // jump
                (50, InputFrame.Neutral, 0),
            };
            for (int b = 0; b < InputFrame.ActionCount; b++)
            {
                bool hold = character.Moves[character.ButtonMoves[b]].Type == MoveType.Shield;
                if (hold)
                {
                    steps.Add((30, new InputFrame(0f, 0f, false, InputFrame.ActionBit(b)), 1 + b));
                }
                else
                {
                    steps.Add((1, new InputFrame(0f, 0f, false, InputFrame.ActionBit(b)), 1 + b));
                }
                steps.Add((60, InputFrame.Neutral, 1 + b));
            }
            _timeline = steps.ToArray();
            _total = steps.Sum(s => s.Item1);
        }

        public InputFrame GetInput(SimWorld world, int playerIndex) =>
            StepAt(world.TickCount).Frame;

        /// <summary>The legend slot demoed at a sim tick (-1 during the settle).</summary>
        public int ActiveSlot(int tick) => StepAt(tick).Slot;

        private (int Ticks, InputFrame Frame, int Slot) StepAt(int tick)
        {
            int t = tick % _total;
            foreach ((int ticks, InputFrame frame, int slot) in _timeline)
            {
                if (t < ticks)
                {
                    return (ticks, frame, slot);
                }
                t -= ticks;
            }
            return _timeline[0];
        }
    }
}
