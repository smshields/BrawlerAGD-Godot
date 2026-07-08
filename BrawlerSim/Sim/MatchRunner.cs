using BrawlerSim.Genome;
using BrawlerSim.Replay;

namespace BrawlerSim.Sim;

/// <summary>Runs a full match headless. This is the evaluation engine's hot loop.</summary>
public static class MatchRunner
{
    public static MatchResult Run(
        GameGenome genome,
        IReadOnlyList<IInputSource> sources,
        MatchConfig? config = null,
        bool recordTrace = false)
    {
        var world = new SimWorld(genome, config);
        InputTrace? trace = recordTrace ? new InputTrace() : null;
        Span<InputFrame> inputs = stackalloc InputFrame[world.Players.Count];

        while (!world.IsOver)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = sources[i].GetInput(world, i);
            }
            trace?.Record(inputs);
            world.Tick(inputs);
        }
        return world.BuildResult(trace);
    }

    /// <summary>
    /// Re-runs a match from its trace and returns the result. The caller compares
    /// FinalHash values to verify bit-exact reproduction (the CI tick-equivalence gate).
    /// </summary>
    public static MatchResult Replay(GameGenome genome, InputTrace trace, MatchConfig? config = null)
    {
        var source = new TraceInputSource(trace);
        var sources = new IInputSource[trace.PlayerCount == 0 ? 2 : trace.PlayerCount];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = source;
        }
        return Run(genome, sources, config);
    }
}
