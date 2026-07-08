namespace BrawlerSim.Sim;

/// <summary>
/// One player's input for one tick — the ONLY channel by which anything outside the sim
/// influences a match. Semantics note: Jump/Attack are consumed as "wants to act this
/// tick". Human sources should send press EDGES; the ported decision-tree agent sends
/// LEVELS, which reproduces the Unity AI's instant ground-jump→air-jump chaining.
/// </summary>
public readonly record struct InputFrame(float Horizontal, bool Jump, bool Attack)
{
    public static readonly InputFrame Neutral = new(0f, false, false);
}

/// <summary>
/// Supplies a player's input each tick. Implementations: decision-tree agent (AI),
/// input-trace playback (replay), scripted sequences (tests), and — in the Godot layer —
/// live human input. The source may READ the world; it must never mutate it.
/// </summary>
public interface IInputSource
{
    InputFrame GetInput(SimWorld world, int playerIndex);
}
