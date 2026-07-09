using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Sim;

namespace BrawlerSim.Evolution;

/// <summary>
/// The genetic algorithm, Unity semantics preserved: every generation the WHOLE
/// population (survivors included) is re-evaluated by AI self-play; individuals are
/// sorted by aggregated fitness; the bottom dropout fraction is replaced by children of
/// two uniformly random survivors (Breed = positional crossover + all-or-none mutation).
///
/// Determinism: breeding draws from one run-level RNG (checkpointable via Snapshot);
/// each match's agents get streams derived from (seed, generation, individual, round),
/// so evaluation parallelism never affects results.
/// </summary>
public sealed class EvolutionEngine
{
    private readonly EvolutionConfig _config;
    private readonly IFitnessFunction _fitness;
    private readonly GameGenome[] _population;
    private readonly float[] _lastFitness;
    private readonly Pcg32 _rng;

    public int GenerationsCompleted { get; private set; }

    public IReadOnlyList<GameGenome> Population => _population;

    /// <summary>Aggregated fitness of the population as of the last completed Step.</summary>
    public IReadOnlyList<float> LastFitness => _lastFitness;

    public IFitnessFunction FitnessFunction => _fitness;

    public EvolutionEngine(EvolutionConfig config, IFitnessFunction? fitness = null)
    {
        _config = config;
        _fitness = fitness ?? new StandardFitness(
            config.TargetGameLengthSeconds, config.Match.MaxMatchSeconds);
        _rng = new Pcg32(config.Seed);
        _population = new GameGenome[config.PopulationSize];
        _lastFitness = new float[config.PopulationSize];
        for (int i = 0; i < _population.Length; i++)
        {
            _population[i] = GameGenome.Generate(config.Generation, _rng);
        }
    }

    /// <summary>Resume constructor: state comes from a checkpoint (see RunStore).</summary>
    public EvolutionEngine(
        EvolutionConfig config,
        IReadOnlyList<GameGenome> population,
        (ulong State, ulong Inc) rngState,
        int generationsCompleted,
        IFitnessFunction? fitness = null)
    {
        if (population.Count != config.PopulationSize)
        {
            throw new ArgumentException(
                $"Checkpoint population size {population.Count} does not match config {config.PopulationSize}.");
        }
        _config = config;
        _fitness = fitness ?? new StandardFitness(
            config.TargetGameLengthSeconds, config.Match.MaxMatchSeconds);
        _rng = Pcg32.Resume(rngState.State, rngState.Inc);
        _population = population.ToArray();
        _lastFitness = new float[config.PopulationSize];
        GenerationsCompleted = generationsCompleted;
    }

    public (ulong State, ulong Inc) RngSnapshot => _rng.Snapshot();

    /// <summary>
    /// Runs one generation: evaluate all → stats → replace the bottom dropout fraction
    /// with children of random survivors. After Step, Population holds the NEXT
    /// generation (children not yet evaluated) — exactly what a checkpoint stores.
    /// </summary>
    public GenerationStats Step()
    {
        int generation = GenerationsCompleted;
        EvaluateAll(generation, _lastFitness);

        // Stable ascending sort by fitness (ties keep index order → deterministic).
        int[] order = Enumerable.Range(0, _population.Length)
            .OrderBy(i => _lastFitness[i])
            .ToArray();

        int cut = (int)(_population.Length * _config.DropoutRate);
        int survivors = _population.Length - cut;

        float total = 0f, survivorTotal = 0f;
        for (int rank = 0; rank < order.Length; rank++)
        {
            float fitness = _lastFitness[order[rank]];
            total += fitness;
            if (rank >= cut)
            {
                survivorTotal += fitness;
            }
        }
        int bestIndex = order[^1];
        var stats = new GenerationStats(
            generation,
            _lastFitness[bestIndex],
            total / _population.Length,
            survivorTotal / survivors,
            bestIndex);

        // Replace the bottom `cut` individuals with children of two random survivors.
        GameGenome[] snapshot = (GameGenome[])_population.Clone();
        for (int rank = 0; rank < cut; rank++)
        {
            GameGenome parentA = snapshot[order[cut + _rng.NextInt(survivors)]];
            GameGenome parentB = snapshot[order[cut + _rng.NextInt(survivors)]];
            _population[order[rank]] =
                GameGenomeOps.Breed(parentA, parentB, _config.MutationRate, _rng, _config.Generation);
        }

        GenerationsCompleted++;
        return stats;
    }

    /// <summary>
    /// Re-runs an individual's evaluation match with its exact per-round seed, recording
    /// the input trace — the audit trail for any fitness score in the run.
    /// </summary>
    public (MatchResult Result, InputTrace Trace) ReplayEvaluation(int individual, int generation, int round = 0)
    {
        MatchResult result = RunMatch(_population[individual], generation, individual, round, recordTrace: true);
        return (result, result.Trace!);
    }

    private void EvaluateAll(int generation, float[] fitness)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _config.Parallelism > 0 ? _config.Parallelism : Environment.ProcessorCount,
        };
        Parallel.For(0, _population.Length, options, i =>
        {
            Span<float> rounds = stackalloc float[_config.RoundsPerIndividual];
            for (int round = 0; round < rounds.Length; round++)
            {
                MatchResult result = RunMatch(_population[i], generation, i, round, recordTrace: false);
                rounds[round] = _fitness.Evaluate(result);
            }
            fitness[i] = Aggregate(rounds);
        });
    }

    private MatchResult RunMatch(GameGenome genome, int generation, int individual, int round, bool recordTrace)
    {
        ulong seed = SeedMix.MatchSeed(_config.Seed, generation, individual, round);
        var sources = new IInputSource[]
        {
            _config.Agent.CreateSource(new Pcg32(seed, 0)),
            _config.Agent.CreateSource(new Pcg32(seed, 1)),
        };
        return MatchRunner.Run(genome, sources, _config.Match, recordTrace);
    }

    private float Aggregate(Span<float> rounds)
    {
        // In-place insertion sort: rounds counts are tiny and this allocates nothing.
        for (int i = 1; i < rounds.Length; i++)
        {
            float value = rounds[i];
            int j = i - 1;
            while (j >= 0 && rounds[j] > value)
            {
                rounds[j + 1] = rounds[j];
                j--;
            }
            rounds[j + 1] = value;
        }
        if (_config.Aggregate == FitnessAggregate.Median)
        {
            return rounds[rounds.Length / 2]; // Unity parity: upper median
        }
        float total = 0f;
        foreach (float value in rounds)
        {
            total += value;
        }
        return total / rounds.Length;
    }
}
