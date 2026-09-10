using System.Text.Json;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Serialization;

namespace BrawlerSim.Evolution;

/// <summary>
/// Disk layout of a MAP-Elites run (2026-09-10, docs/features/map-elites.md) — the
/// RunStore counterpart, written after every batch:
///   run.json                manifest: kind "map-elites", config, FROZEN bin edges,
///                           RNG state, per-batch stats, archive index (cell →
///                           fitness + RAW descriptor + candidate)
///   archive/cell_NNNN.json  one game.json per filled cell (only changed cells are
///                           rewritten per checkpoint)
///   best.json + best.trace.json   best-raw-fitness elite and its grading match
/// The raw descriptors live in the manifest so a COPY of the archive can be re-binned
/// later (global cross-run views); the live archive's bins never change.
/// </summary>
public static class MapElitesStore
{
    public const string Kind = "map-elites";
    public const string ArchiveDirName = "archive";

    public static string CellFileName(int cell) => $"cell_{cell:D4}.json";

    private static readonly JsonSerializerOptions Options = JsonOptions.Document;

    /// <summary>True when the directory holds a MAP-Elites run (manifest kind) —
    /// the app-side dispatcher between RunStore.Load and MapElitesStore.Load.</summary>
    public static bool IsMapElitesRun(string runDir)
    {
        string manifestPath = Path.Combine(runDir, RunStore.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return false;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("kind", out JsonElement kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == Kind;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void SaveCheckpoint(string runDir, MapElitesEngine engine, MapElitesConfig config,
        List<MapElitesBatchStats> history)
    {
        Directory.CreateDirectory(runDir);
        string archiveDir = Path.Combine(runDir, ArchiveDirName);
        Directory.CreateDirectory(archiveDir);

        foreach (int cell in engine.FlushDirtyCells())
        {
            if (!engine.Archive.TryGet(cell, out ArchiveEntry entry))
            {
                continue; // cells are never removed; guard anyway
            }
            GameGenomeJson.Save(
                new GameRecord($"cell_{cell:D4}",
                    FormattableString.Invariant(
                        $"mapelites:cell{cell}/cand{entry.Candidate}/fitness{entry.Fitness:F2}"),
                    entry.Genome),
                Path.Combine(archiveDir, CellFileName(cell)));
        }

        (ulong state, ulong inc) = engine.RngSnapshot;
        var manifest = new MapElitesManifest
        {
            FormatVersion = 1,
            Kind = Kind,
            FitnessName = engine.FitnessFunction.Name,
            Seed = config.Seed,
            BatchSize = config.BatchSize,
            InitialRandomCandidates = config.InitialRandomCandidates,
            MutationRate = config.MutationRate,
            RoundsPerIndividual = config.RoundsPerIndividual,
            Aggregate = config.Aggregate.ToString(),
            TargetGameLengthSeconds = config.TargetGameLengthSeconds,
            Agent = config.Agent.Kind.ToString(),
            AgentRandomness = config.Agent.Randomness,
            AgentDecisionIntervalTicks = config.Agent.DecisionIntervalTicks,
            MaxMatchSeconds = config.Match.MaxMatchSeconds,
            MaxStunSeconds = float.IsPositiveInfinity(config.Match.MaxStunSeconds)
                ? null : config.Match.MaxStunSeconds,
            FitnessCollisionScalar = config.FitnessCollisionScalar,
            Players = config.Generation.CharacterCount == 2 ? null : config.Generation.CharacterCount,
            Composition = RunStore.CompositionDoc(config.Generation),
            TypeRerollRate = config.Generation.IsComposed ? config.Generation.TypeRerollRate : null,
            RangeOverrides = RunStore.RangeOverridesDoc(config.Generation),
            Sprites = config.Generation.SpriteSelector is null ? null : true,
            Themes = config.Generation.StageThemeSelector is null ? null : true,
            Backgrounds = config.Generation.BackgroundSelector is null ? null : true,
            Bins = config.Bins.ToDoc(),
            BatchesCompleted = engine.BatchesCompleted,
            CandidatesEvaluated = engine.CandidatesEvaluated,
            OutOfPilotRange = engine.Archive.OutOfPilotRangeCount,
            RngState = state,
            RngInc = inc,
            Stats = history.Select(s => new BatchStatsDoc
            {
                Batch = s.Batch,
                Filled = s.Filled,
                Coverage = s.Coverage,
                QdScore = s.QdScore,
                BestFitness = s.BestFitness,
                Insertions = s.Insertions,
                Replacements = s.Replacements,
                OutOfPilotRange = s.OutOfPilotRange,
            }).ToList(),
            Archive = engine.Archive.Cells.Select(kv => new ArchiveIndexDoc
            {
                Cell = kv.Key,
                Fitness = kv.Value.Fitness,
                Descriptor = kv.Value.Descriptor,
                Candidate = kv.Value.Candidate,
            }).ToList(),
        };
        File.WriteAllText(Path.Combine(runDir, RunStore.ManifestFileName),
            JsonSerializer.Serialize(manifest, Options));
    }

    /// <summary>Best-raw-fitness elite + its grading trace, RunStore.SaveBest parity.</summary>
    public static void SaveBest(string runDir, ArchiveEntry best, InputTrace trace)
    {
        GameGenomeJson.Save(
            new GameRecord("best",
                FormattableString.Invariant(
                    $"mapelites:cand{best.Candidate}/fitness{best.Fitness:F2}"),
                best.Genome),
            Path.Combine(runDir, RunStore.BestGameFileName));
        InputTraceJson.Save(trace, Path.Combine(runDir, RunStore.BestTraceFileName));
    }

    /// <summary>Resumes a checkpoint. Selector arguments re-attach like RunStore.Load;
    /// bins come back frozen exactly as stamped — a resumed archive never re-bins.</summary>
    public static (MapElitesEngine Engine, MapElitesConfig Config, List<MapElitesBatchStats> History) Load(
        string runDir, Sprites.SpriteSelector? spriteSelector = null,
        Sprites.StageThemeSelector? themeSelector = null,
        Backgrounds.BackgroundSelector? backgroundSelector = null)
    {
        string manifestPath = Path.Combine(runDir, RunStore.ManifestFileName);
        MapElitesManifest manifest =
            JsonSerializer.Deserialize<MapElitesManifest>(File.ReadAllText(manifestPath), Options)
            ?? throw new JsonException($"Could not parse {manifestPath}.");
        if (manifest.Kind != Kind)
        {
            throw new InvalidDataException(
                $"{manifestPath} is not a MAP-Elites run (kind '{manifest.Kind}') — use RunStore.Load.");
        }

        GenerationConfig generation = RunStore.WithSelectors(
            RunStore.BuildGeneration(manifest.Players, manifest.Composition,
                manifest.TypeRerollRate, manifest.RangeOverrides),
            manifest.Sprites == true, manifest.Themes == true, manifest.Backgrounds == true,
            spriteSelector, themeSelector, backgroundSelector);

        var config = new MapElitesConfig
        {
            FitnessName = manifest.FitnessName,
            FitnessCollisionScalar = manifest.FitnessCollisionScalar,
            Seed = manifest.Seed,
            BatchSize = manifest.BatchSize,
            InitialRandomCandidates = manifest.InitialRandomCandidates,
            MutationRate = manifest.MutationRate,
            RoundsPerIndividual = manifest.RoundsPerIndividual,
            Aggregate = Enum.Parse<FitnessAggregate>(manifest.Aggregate ?? "Median"),
            TargetGameLengthSeconds = manifest.TargetGameLengthSeconds,
            Agent = new Agents.AgentConfig
            {
                Kind = Enum.Parse<Agents.AgentKind>(manifest.Agent ?? "Utility"),
                Randomness = manifest.AgentRandomness ?? Agents.AgentConfig.Default.Randomness,
                DecisionIntervalTicks =
                    manifest.AgentDecisionIntervalTicks ?? Agents.AgentConfig.Default.DecisionIntervalTicks,
            },
            Match = Sim.MatchConfig.Default with
            {
                MaxMatchSeconds = manifest.MaxMatchSeconds ?? Sim.MatchConfig.Default.MaxMatchSeconds,
                MaxStunSeconds = manifest.MaxStunSeconds ?? float.PositiveInfinity,
            },
            Generation = generation,
            Bins = DescriptorBins.FromDoc(
                manifest.Bins ?? throw new JsonException($"{manifestPath} has no bins.")),
        };

        var archive = new List<(int Cell, ArchiveEntry Entry)>();
        foreach (ArchiveIndexDoc doc in manifest.Archive ?? new List<ArchiveIndexDoc>())
        {
            GameGenome genome = GameGenomeJson.Load(
                Path.Combine(runDir, ArchiveDirName, CellFileName(doc.Cell)), config.Generation).Genome;
            archive.Add((doc.Cell, new ArchiveEntry(
                genome, doc.Fitness,
                doc.Descriptor ?? throw new JsonException($"cell {doc.Cell} has no descriptor"),
                doc.Candidate)));
        }

        var history = (manifest.Stats ?? new List<BatchStatsDoc>())
            .Select(s => new MapElitesBatchStats(
                s.Batch, s.Filled, s.Coverage, s.QdScore, s.BestFitness,
                s.Insertions, s.Replacements, s.OutOfPilotRange))
            .ToList();

        var engine = new MapElitesEngine(
            config, archive, (manifest.RngState, manifest.RngInc),
            manifest.BatchesCompleted, manifest.CandidatesEvaluated, manifest.OutOfPilotRange);
        return (engine, config, history);
    }

    private sealed class MapElitesManifest
    {
        public int FormatVersion { get; set; }
        public string? Kind { get; set; }
        public string? FitnessName { get; set; }
        public ulong Seed { get; set; }
        public int BatchSize { get; set; }
        public int InitialRandomCandidates { get; set; }
        public float MutationRate { get; set; }
        public int RoundsPerIndividual { get; set; }
        public string? Aggregate { get; set; }
        public float TargetGameLengthSeconds { get; set; }
        public string? Agent { get; set; }
        public float? AgentRandomness { get; set; }
        public int? AgentDecisionIntervalTicks { get; set; }
        public float? MaxMatchSeconds { get; set; }
        public float? MaxStunSeconds { get; set; }
        public float? FitnessCollisionScalar { get; set; }
        public int? Players { get; set; }
        public bool? Sprites { get; set; }
        public bool? Themes { get; set; }
        public bool? Backgrounds { get; set; }
        public List<string>? Composition { get; set; }
        public float? TypeRerollRate { get; set; }
        public List<RunStore.RangeOverrideDoc>? RangeOverrides { get; set; }
        public DescriptorBins.Doc? Bins { get; set; }
        public int BatchesCompleted { get; set; }
        public int CandidatesEvaluated { get; set; }
        public int OutOfPilotRange { get; set; }
        public ulong RngState { get; set; }
        public ulong RngInc { get; set; }
        public List<BatchStatsDoc>? Stats { get; set; }
        public List<ArchiveIndexDoc>? Archive { get; set; }
    }

    private sealed class BatchStatsDoc
    {
        public int Batch { get; set; }
        public int Filled { get; set; }
        public float Coverage { get; set; }
        public double QdScore { get; set; }
        public float BestFitness { get; set; }
        public int Insertions { get; set; }
        public int Replacements { get; set; }
        public int OutOfPilotRange { get; set; }
    }

    private sealed class ArchiveIndexDoc
    {
        public int Cell { get; set; }
        public float Fitness { get; set; }
        public float[]? Descriptor { get; set; }
        public int Candidate { get; set; }
    }
}
