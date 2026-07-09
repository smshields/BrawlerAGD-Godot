namespace BrawlerSim.Sim;

/// <summary>
/// One player's input for one tick — the ONLY channel by which anything outside the sim
/// influences a match. Semantics note: Jump/Actions are consumed as "wants to act this
/// tick". Human sources should send press EDGES; the ported decision-tree agent sends
/// LEVELS, which reproduces the Unity AI's instant ground-jump→air-jump chaining.
///
/// 2026-07-08 control-scheme rework (docs/features/multi-move-controls.md): the single
/// Attack bit became four assignable action buttons (a bitmask; which move each button
/// triggers is a genome gene), and Vertical was added. Vertical is captured in traces
/// but currently read by nothing — it exists so future features (down-attacks,
/// drop-through) don't force a second trace-format migration.
/// </summary>
public readonly record struct InputFrame(float Horizontal, float Vertical, bool Jump, byte Actions)
{
    /// <summary>Number of assignable action buttons in the control scheme.</summary>
    public const int ActionCount = 4;

    public static readonly InputFrame Neutral = new(0f, 0f, false, 0);

    /// <summary>Bitmask with only <paramref name="button"/> (0..ActionCount-1) pressed.</summary>
    public static byte ActionBit(int button) => (byte)(1 << button);

    public bool ActionPressed(int button) => (Actions & (1 << button)) != 0;

    /// <summary>
    /// Lowest-index pressed action button, or -1 when none. When several buttons are
    /// pressed the same tick, the lowest index wins — a deterministic tie-break that is
    /// part of the input contract.
    /// </summary>
    public int FirstAction
    {
        get
        {
            for (int b = 0; b < ActionCount; b++)
            {
                if ((Actions & (1 << b)) != 0)
                {
                    return b;
                }
            }
            return -1;
        }
    }
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
