using BrawlerSim.Agents;
using BrawlerSim.Genome;
using BrawlerSim.Sim;

namespace BrawlerSim.Evolution;

public enum FitnessAggregate
{
    /// <summary>Median of the per-round fitness scores (Unity default, evalStrategy 0).</summary>
    Median,
    Average,
}

/// <summary>
/// Everything an evolution run needs. Defaults mirror the Unity setup (pop 100,
/// dropout 0.5, mutation 0.4, 45 s target inside 60 s matches).
/// </summary>
public sealed record EvolutionConfig
{
    public ulong Seed { get; init; } = 1;
    public int PopulationSize { get; init; } = 100;
    public float DropoutRate { get; init; } = 0.5f;
    public float MutationRate { get; init; } = 0.4f;
    public int RoundsPerIndividual { get; init; } = 1;
    public FitnessAggregate Aggregate { get; init; } = FitnessAggregate.Median;
    public float TargetGameLengthSeconds { get; init; } = 45f;

    /// <summary>Which versioned fitness scores this run (FitnessRegistry). Recorded in
    /// run.json; resuming honors the recorded name, so old runs keep standard-v2.</summary>
    public string FitnessName { get; init; } = Fitness.FitnessRegistry.DefaultName;

    /// <summary>Evaluation threads; 0 = one per processor. Results are identical at any value.</summary>
    public int Parallelism { get; init; }

    public GenerationConfig Generation { get; init; } = GenerationConfig.Default;
    public MatchConfig Match { get; init; } = MatchConfig.Default;

    /// <summary>The playtesting instrument (recorded in run.json — part of what a
    /// fitness score MEANS). Utility by default since 2026-07-09.</summary>
    public AgentConfig Agent { get; init; } = AgentConfig.Default;

    /// <summary>
    /// Optional fitness-sharing bonus (2026-07-09 noise study): when &gt; 0, each
    /// individual's SELECTION score is fitness + weight × (mean normalized genome
    /// distance to the rest of the population — see GenomeDistance). Recorded stats
    /// stay raw fitness. 0 (default) = exact legacy selection. Distances are ~[0,1],
    /// fitness ~±100, so weights of 10–50 are meaningful.
    /// </summary>
    public float DiversityWeight { get; init; }
}

/// <summary>One generation's summary, recorded in the run manifest.</summary>
public sealed record GenerationStats(
    int Generation,
    float TopFitness,
    float AverageFitness,
    float AverageSurvivorFitness,
    int BestIndex);
