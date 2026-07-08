using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Samples Godot input actions into one InputFrame per sim tick. Jump/attack are press
/// EDGES (unlike the level-based AI), matching how humans played the Unity build.
/// The sim never sees Godot types — only the InputFrame.
/// </summary>
public sealed class HumanInputSource : IInputSource
{
    private readonly string _left;
    private readonly string _right;
    private readonly string _jump;
    private readonly string _attack;

    public HumanInputSource(int playerNumber)
    {
        string prefix = $"p{playerNumber}_";
        _left = prefix + "left";
        _right = prefix + "right";
        _jump = prefix + "jump";
        _attack = prefix + "attack";
    }

    public InputFrame GetInput(SimWorld world, int playerIndex) =>
        new(
            Input.GetActionStrength(_right) - Input.GetActionStrength(_left),
            Input.IsActionJustPressed(_jump),
            Input.IsActionJustPressed(_attack));
}
