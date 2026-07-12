using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Samples Godot input actions into one InputFrame per sim tick. Jump and attack
/// buttons are press EDGES (matching how humans played the Unity build); movement axes
/// are levels; SHIELD-mapped buttons are levels too (hold to shield, release to drop —
/// 2026-07-12). The sim never sees Godot types — only the InputFrame.
/// </summary>
public sealed class HumanInputSource : IInputSource
{
    private readonly string _left;
    private readonly string _right;
    private readonly string _up;
    private readonly string _down;
    private readonly string _jump;
    private readonly string[] _actions;
    private readonly bool[] _holdButtons;

    /// <param name="holdButtons">Per action button: true = level semantics (the
    /// button's mapped move is a shield); null/absent = all edges.</param>
    public HumanInputSource(int playerNumber, bool[]? holdButtons = null)
    {
        _holdButtons = holdButtons ?? new bool[InputFrame.ActionCount];
        string prefix = $"p{playerNumber}_";
        _left = prefix + "left";
        _right = prefix + "right";
        _up = prefix + "up";
        _down = prefix + "down";
        _jump = prefix + "jump";
        _actions = new string[InputFrame.ActionCount];
        for (int b = 0; b < _actions.Length; b++)
        {
            _actions[b] = $"{prefix}action{b}";
        }
    }

    public InputFrame GetInput(SimWorld world, int playerIndex)
    {
        byte actions = 0;
        for (int b = 0; b < _actions.Length; b++)
        {
            bool pressed = _holdButtons[b]
                ? Input.IsActionPressed(_actions[b])
                : Input.IsActionJustPressed(_actions[b]);
            if (pressed)
            {
                actions |= InputFrame.ActionBit(b);
            }
        }
        return new InputFrame(
            Input.GetActionStrength(_right) - Input.GetActionStrength(_left),
            Input.GetActionStrength(_up) - Input.GetActionStrength(_down),
            Input.IsActionJustPressed(_jump),
            actions);
    }
}
