using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// Scores one evaluated match. Implementations are pure and stateless so evaluation can
/// run in parallel; they are versioned by Name so run manifests record exactly which
/// fitness produced which scores (the paper's future work — persona fitness,
/// human-informed fitness — slots in as new implementations).
/// </summary>
public interface IFitnessFunction
{
    string Name { get; }

    float Evaluate(MatchResult result);
}
