using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Serialization;

namespace BrawlerSim.Evolution;

/// <summary>
/// Disk layout of an evolution run — the checkpoint written after every generation:
///   run.json               manifest: config, RNG state, per-generation stats
///   population/game_NNN.json   the current (next-to-evaluate) population
///   best.json + best.trace.json   best individual so far and the exact match it was graded on
/// A run directory is fully self-describing: resume, reload, or hand any game.json to
/// the Play mode.
/// </summary>
public static class RunStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void SaveCheckpoint(string runDir, EvolutionEngine engine, EvolutionConfig config, List<GenerationStats> history)
    {
        Directory.CreateDirectory(runDir);
        string populationDir = Path.Combine(runDir, "population");
        Directory.CreateDirectory(populationDir);

        for (int i = 0; i < engine.Population.Count; i++)
        {
            GameGenomeJson.Save(
                new GameRecord($"game_{i:D3}", $"evolved:gen{engine.GenerationsCompleted}/idx{i}", engine.Population[i]),
                Path.Combine(populationDir, $"game_{i:D3}.json"));
        }

        (ulong state, ulong inc) = engine.RngSnapshot;
        var manifest = new RunManifest
        {
            FormatVersion = 1,
            FitnessName = engine.FitnessFunction.Name,
            Seed = config.Seed,
            PopulationSize = config.PopulationSize,
            DropoutRate = config.DropoutRate,
            MutationRate = config.MutationRate,
            RoundsPerIndividual = config.RoundsPerIndividual,
            Aggregate = config.Aggregate.ToString(),
            TargetGameLengthSeconds = config.TargetGameLengthSeconds,
            Agent = config.Agent.Kind.ToString(),
            AgentRandomness = config.Agent.Randomness,
            AgentDecisionIntervalTicks = config.Agent.DecisionIntervalTicks,
            MaxMatchSeconds = config.Match.MaxMatchSeconds,
            DiversityWeight = config.DiversityWeight,
            FitnessCollisionScalar = config.FitnessCollisionScalar,
            GenerationsCompleted = engine.GenerationsCompleted,
            RngState = state,
            RngInc = inc,
            Stats = history.Select(s => new GenerationStatsDoc
            {
                Generation = s.Generation,
                TopFitness = s.TopFitness,
                AverageFitness = s.AverageFitness,
                AverageSurvivorFitness = s.AverageSurvivorFitness,
                BestIndex = s.BestIndex,
            }).ToList(),
        };
        File.WriteAllText(Path.Combine(runDir, "run.json"), JsonSerializer.Serialize(manifest, Options));
    }

    public static void SaveBest(string runDir, GameGenome best, GenerationStats stats, InputTrace trace)
    {
        GameGenomeJson.Save(
            new GameRecord("best", $"evolved:gen{stats.Generation}/idx{stats.BestIndex}/fitness{stats.TopFitness:F2}", best),
            Path.Combine(runDir, "best.json"));
        InputTraceJson.Save(trace, Path.Combine(runDir, "best.trace.json"));
    }

    public static (EvolutionEngine Engine, EvolutionConfig Config, List<GenerationStats> History) Load(string runDir)
    {
        string manifestPath = Path.Combine(runDir, "run.json");
        RunManifest manifest = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(manifestPath), Options)
            ?? throw new JsonException($"Could not parse {manifestPath}.");

        var config = new EvolutionConfig
        {
            // Pre-v3 manifests recorded "standard-v2"; honoring the recorded name means
            // resumed runs keep the fitness that produced their history.
            FitnessName = manifest.FitnessName ?? "standard-v2",
            FitnessCollisionScalar = manifest.FitnessCollisionScalar,
            Seed = manifest.Seed,
            PopulationSize = manifest.PopulationSize,
            DropoutRate = manifest.DropoutRate,
            MutationRate = manifest.MutationRate,
            RoundsPerIndividual = manifest.RoundsPerIndividual,
            Aggregate = Enum.Parse<FitnessAggregate>(manifest.Aggregate ?? "Median"),
            TargetGameLengthSeconds = manifest.TargetGameLengthSeconds,
            // Runs checkpointed before 2026-07-09 have no agent fields: they were
            // produced by the decision tree — resuming must keep that instrument.
            Agent = new Agents.AgentConfig
            {
                Kind = Enum.Parse<Agents.AgentKind>(manifest.Agent ?? "DecisionTree"),
                Randomness = manifest.AgentRandomness ?? Agents.AgentConfig.Default.Randomness,
                DecisionIntervalTicks =
                    manifest.AgentDecisionIntervalTicks ?? Agents.AgentConfig.Default.DecisionIntervalTicks,
            },
            Match = Sim.MatchConfig.Default with { MaxMatchSeconds = manifest.MaxMatchSeconds ?? 60f },
            DiversityWeight = manifest.DiversityWeight ?? 0f,
        };

        var population = new List<GameGenome>(manifest.PopulationSize);
        for (int i = 0; i < manifest.PopulationSize; i++)
        {
            population.Add(GameGenomeJson.Load(
                Path.Combine(runDir, "population", $"game_{i:D3}.json"), config.Generation).Genome);
        }

        var history = (manifest.Stats ?? new List<GenerationStatsDoc>())
            .Select(s => new GenerationStats(
                s.Generation, s.TopFitness, s.AverageFitness, s.AverageSurvivorFitness, s.BestIndex))
            .ToList();

        var engine = new EvolutionEngine(
            config, population, (manifest.RngState, manifest.RngInc), manifest.GenerationsCompleted);
        return (engine, config, history);
    }

    private sealed class RunManifest
    {
        public int FormatVersion { get; set; }
        public string? FitnessName { get; set; }
        public ulong Seed { get; set; }
        public int PopulationSize { get; set; }
        public float DropoutRate { get; set; }
        public float MutationRate { get; set; }
        public int RoundsPerIndividual { get; set; }
        public string? Aggregate { get; set; }
        public float TargetGameLengthSeconds { get; set; }
        public string? Agent { get; set; }
        public float? AgentRandomness { get; set; }
        public int? AgentDecisionIntervalTicks { get; set; }
        public float? MaxMatchSeconds { get; set; }
        public float? DiversityWeight { get; set; }
        public float? FitnessCollisionScalar { get; set; }
        public int GenerationsCompleted { get; set; }
        public ulong RngState { get; set; }
        public ulong RngInc { get; set; }
        public List<GenerationStatsDoc>? Stats { get; set; }
    }

    private sealed class GenerationStatsDoc
    {
        public int Generation { get; set; }
        public float TopFitness { get; set; }
        public float AverageFitness { get; set; }
        public float AverageSurvivorFitness { get; set; }
        public int BestIndex { get; set; }
    }
}
