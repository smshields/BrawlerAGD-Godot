using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Samples Godot input actions into one InputFrame per sim tick. Jump and the action
/// buttons are press EDGES (unlike the level-based AI), matching how humans played the
/// Unity build; movement axes are levels. The sim never sees Godot types — only the
/// InputFrame.
/// </summary>
public sealed class HumanInputSource : IInputSource
{
    private readonly string _left;
    private readonly string _right;
    private readonly string _up;
    private readonly string _down;
    private readonly string _jump;
    private readonly string[] _actions;

    public HumanInputSource(int playerNumber)
    {
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
            if (Input.IsActionJustPressed(_actions[b]))
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
