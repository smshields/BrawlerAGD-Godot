using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Sim;

namespace BrawlerSim.Evolution;

/// <summary>
/// Everything a MAP-Elites run needs — the EvolutionConfig counterpart (2026-09-10,
/// docs/features/map-elites.md). Bins are part of the config on purpose: they are
/// frozen at run start and stamped into every checkpoint (archive identity =
/// character count + composition mode + range overrides + bin edges).
/// </summary>
public sealed record MapElitesConfig
{
    public ulong Seed { get; init; } = 1;

    /// <summary>Candidates generated + evaluated per Step (the checkpoint cadence).</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Candidates generated FRESH (GameGenome.Generate) before variation
    /// starts breeding elites; also the floor while the archive has fewer than two
    /// elites. Standard MAP-Elites initialization.</summary>
    public int InitialRandomCandidates { get; init; } = 500;

    public float MutationRate { get; init; } = 0.4f;
    public int RoundsPerIndividual { get; init; } = 1;
    public FitnessAggregate Aggregate { get; init; } = FitnessAggregate.Median;
    public float TargetGameLengthSeconds { get; init; } = 45f;

    /// <summary>Registry name; null = auto by player count (standard-v5 / ffa-v2).</summary>
    public string? FitnessName { get; init; }
    public float? FitnessCollisionScalar { get; init; }

    /// <summary>Evaluation threads; 0 = one per processor. Results identical at any value.</summary>
    public int Parallelism { get; init; }

    public GenerationConfig Generation { get; init; } = GenerationConfig.Default;
    public MatchConfig Match { get; init; } = MatchConfig.Default;
    public AgentConfig Agent { get; init; } = AgentConfig.Default;

    /// <summary>The frozen equal-frequency bin edges (DescriptorBins.FromPilot).</summary>
    public required DescriptorBins Bins { get; init; }
}

/// <summary>One batch's summary, recorded in the run manifest. Filled/Coverage/
/// QdScore/BestFitness are archive-wide as of the batch's end; Insertions/
/// Replacements/OutOfPilotRange count THIS batch only.</summary>
public sealed record MapElitesBatchStats(
    int Batch,
    int Filled,
    float Coverage,
    double QdScore,
    float BestFitness,
    int Insertions,
    int Replacements,
    int OutOfPilotRange);

/// <summary>
/// MAP-Elites (illumination) engine — the EvolutionEngine sibling (2026-09-10,
/// docs/features/map-elites.md). Per batch: candidates are bred from two uniform-
/// random elites (fresh-generated during initialization), evaluated by AI self-play
/// in parallel, and offered to the archive SEQUENTIALLY in batch order — insertion
/// order is part of the determinism contract, so runs are bit-reproducible at any
/// parallelism, and resume == uninterrupted.
///
/// Determinism: breeding/selection draw from one run-level RNG (checkpointable);
/// each candidate's evaluation streams derive from SeedMix.MatchSeed(seed, 0,
/// candidate, round) where candidate is the run-monotone counter — so any elite's
/// grading match is replayable forever from (runSeed, candidate).
/// </summary>
public sealed class MapElitesEngine
{
    private readonly MapElitesConfig _config;
    private readonly IFitnessFunction _fitness;
    private readonly Pcg32 _rng;

    public MapElitesArchive Archive { get; }

    public int BatchesCompleted { get; private set; }

    /// <summary>Run-monotone candidate counter — the evaluation seed key.</summary>
    public int CandidatesEvaluated { get; private set; }

    /// <summary>The most recent batch's candidates and their aggregated fitness —
    /// the EvolutionEngine Population/LastFitness counterpart for chart snapshots.
    /// Empty until the first Step (and after a resume, until the next Step).</summary>
    public IReadOnlyList<GameGenome> LastBatch => _lastBatch;
    public IReadOnlyList<float> LastBatchFitness => _lastBatchFitness;
    private GameGenome[] _lastBatch = System.Array.Empty<GameGenome>();
    private float[] _lastBatchFitness = System.Array.Empty<float>();

    public IFitnessFunction FitnessFunction => _fitness;

    /// <summary>Cells written since the last checkpoint flush (ascending order) —
    /// lets the store rewrite only changed cell files.</summary>
    private readonly SortedSet<int> _dirtyCells = new();

    public MapElitesEngine(MapElitesConfig config, IFitnessFunction? fitness = null)
    {
        if (config.RoundsPerIndividual < 1)
        {
            throw new ArgumentException(
                $"RoundsPerIndividual must be at least 1 (got {config.RoundsPerIndividual}).");
        }
        if (config.BatchSize < 1)
        {
            throw new ArgumentException($"BatchSize must be at least 1 (got {config.BatchSize}).");
        }
        _config = config;
        _fitness = ResolveFitness(config, fitness);
        _rng = new Pcg32(config.Seed);
        Archive = new MapElitesArchive(config.Bins);
    }

    /// <summary>Resume constructor: state comes from a checkpoint (MapElitesStore).</summary>
    public MapElitesEngine(
        MapElitesConfig config,
        IReadOnlyList<(int Cell, ArchiveEntry Entry)> archive,
        (ulong State, ulong Inc) rngState,
        int batchesCompleted,
        int candidatesEvaluated,
        int outOfPilotRangeCount,
        IFitnessFunction? fitness = null)
    {
        _config = config;
        _fitness = ResolveFitness(config, fitness);
        _rng = Pcg32.Resume(rngState.State, rngState.Inc);
        Archive = new MapElitesArchive(config.Bins);
        foreach ((int cell, ArchiveEntry entry) in archive)
        {
            Archive.Restore(cell, entry);
        }
        Archive.OutOfPilotRangeCount = outOfPilotRangeCount;
        BatchesCompleted = batchesCompleted;
        CandidatesEvaluated = candidatesEvaluated;
    }

    private static IFitnessFunction ResolveFitness(MapElitesConfig config, IFitnessFunction? fitness) =>
        fitness ?? FitnessRegistry.Create(
            config.FitnessName, config.TargetGameLengthSeconds, config.Match.MaxMatchSeconds,
            config.FitnessCollisionScalar, config.Generation.CharacterCount);

    public (ulong State, ulong Inc) RngSnapshot => _rng.Snapshot();

    /// <summary>
    /// Runs one batch: generate/breed BatchSize candidates (all run-RNG draws happen
    /// here, before any evaluation), evaluate them in parallel, then offer each to
    /// the archive sequentially in batch order.
    /// </summary>
    public MapElitesBatchStats Step()
    {
        int batch = BatchesCompleted;
        var candidates = new GameGenome[_config.BatchSize];
        ArchiveEntry[] elites = Archive.ElitesSnapshot();
        for (int i = 0; i < candidates.Length; i++)
        {
            bool initializing = CandidatesEvaluated + i < _config.InitialRandomCandidates
                || elites.Length < 2;
            if (initializing)
            {
                candidates[i] = GameGenome.Generate(_config.Generation, _rng);
            }
            else
            {
                GameGenome parentA = elites[_rng.NextInt(elites.Length)].Genome;
                GameGenome parentB = elites[_rng.NextInt(elites.Length)].Genome;
                candidates[i] = GameGenomeOps.Breed(
                    parentA, parentB, _config.MutationRate, _rng, _config.Generation);
            }
        }

        int firstCandidate = CandidatesEvaluated;
        var fitness = new float[candidates.Length];
        var descriptors = new float[candidates.Length][];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _config.Parallelism > 0 ? _config.Parallelism : Environment.ProcessorCount,
        };
        Parallel.For(0, candidates.Length, options, i =>
        {
            descriptors[i] = Descriptors.Compute(candidates[i]);
            Span<float> rounds = stackalloc float[_config.RoundsPerIndividual];
            for (int round = 0; round < rounds.Length; round++)
            {
                MatchResult result = RunMatch(candidates[i], firstCandidate + i, round, recordTrace: false);
                rounds[round] = _fitness.Evaluate(result);
            }
            fitness[i] = Aggregate(rounds);
        });

        int insertions = 0, replacements = 0;
        int outOfPilotBefore = Archive.OutOfPilotRangeCount;
        for (int i = 0; i < candidates.Length; i++)
        {
            var entry = new ArchiveEntry(candidates[i], fitness[i], descriptors[i], firstCandidate + i);
            int cell = Archive.Offer(entry, out bool replaced);
            if (cell >= 0)
            {
                _dirtyCells.Add(cell);
                if (replaced)
                {
                    replacements++;
                }
                else
                {
                    insertions++;
                }
            }
        }

        _lastBatch = candidates;
        _lastBatchFitness = fitness;
        CandidatesEvaluated += candidates.Length;
        BatchesCompleted++;
        return new MapElitesBatchStats(
            batch,
            Archive.Count,
            Archive.Coverage,
            Archive.QdScore,
            Archive.Best?.Fitness ?? float.MinValue,
            insertions,
            replacements,
            Archive.OutOfPilotRangeCount - outOfPilotBefore);
    }

    /// <summary>Re-runs an archive entry's evaluation match with its exact per-round
    /// seed, recording the trace — the audit trail for any archive fitness.</summary>
    public (MatchResult Result, InputTrace Trace) ReplayEvaluation(ArchiveEntry entry, int round = 0)
    {
        MatchResult result = RunMatch(entry.Genome, entry.Candidate, round, recordTrace: true);
        return (result, result.Trace!);
    }

    /// <summary>Cells changed since the last saved checkpoint, ascending. The store
    /// clears them via MarkDirtyCellsSaved AFTER its writes succeed, so a failed
    /// checkpoint never loses track of unsaved cells.</summary>
    public IReadOnlyCollection<int> DirtyCells => _dirtyCells;

    public void MarkDirtyCellsSaved() => _dirtyCells.Clear();

    private MatchResult RunMatch(GameGenome genome, int candidate, int round, bool recordTrace)
    {
        ulong seed = SeedMix.MatchSeed(_config.Seed, 0, candidate, round);
        IInputSource[] sources = _config.Agent.CreateSources(seed, genome.Characters.Count);
        return MatchRunner.Run(genome, sources, _config.Match, recordTrace);
    }

    private float Aggregate(Span<float> rounds) =>
        FitnessAggregation.Aggregate(rounds, _config.Aggregate);
}
