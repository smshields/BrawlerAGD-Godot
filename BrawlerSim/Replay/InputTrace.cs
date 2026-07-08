using BrawlerSim.Sim;

namespace BrawlerSim.Replay;

/// <summary>
/// The complete input history of a match: [tick][player]. A match is a pure function of
/// (genome, config, inputs), so a trace makes any match — AI-evaluated or human-played —
/// exactly reproducible and auditable. Traces of graded matches are the evidence trail
/// for every fitness score.
/// </summary>
public sealed class InputTrace
{
    private readonly List<InputFrame[]> _ticks = new();

    public int TickCount => _ticks.Count;
    public int PlayerCount => _ticks.Count > 0 ? _ticks[0].Length : 0;

    public InputFrame Get(int tick, int playerIndex) => _ticks[tick][playerIndex];

    public void Record(ReadOnlySpan<InputFrame> frame)
    {
        _ticks.Add(frame.ToArray());
    }
}

/// <summary>Plays a recorded trace back into the sim; neutral input past the end.</summary>
public sealed class TraceInputSource : IInputSource
{
    private readonly InputTrace _trace;

    public TraceInputSource(InputTrace trace)
    {
        _trace = trace;
    }

    public InputFrame GetInput(SimWorld world, int playerIndex) =>
        world.TickCount < _trace.TickCount
            ? _trace.Get(world.TickCount, playerIndex)
            : InputFrame.Neutral;
}
