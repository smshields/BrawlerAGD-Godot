using System.Text;
using System.Text.Json;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Fitness;
using BrawlerSim.Fitness.Terms;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// RUN PROVENANCE for designer-built fitness (2026-09-16, phase 3). The rule being
/// pinned: a run scored by a custom instrument records that instrument BY VALUE, so
/// resuming reconstructs the exact scoring that produced the history — even if the
/// recipe file on disk was edited or deleted since.
///
/// This closes a hole that predates the feature: EvolutionEngine has always accepted
/// an injected IFitnessFunction, and run.json recorded only its NAME, so resuming such
/// a run silently rebuilt the DEFAULTS.
/// </summary>
public class FitnessRecipeProvenanceTests
{
    /// <summary>A custom instrument that is clearly not any shipped version: blocks
    /// weighted 3x and the stun tolerance opened up.</summary>
    private static FitnessRecipe Custom(string name = "custom/aggressive-shields")
    {
        FitnessRecipe seed = FitnessRecipe.FromDefault(name);
        seed = FitnessRecipeTests.Edited(seed, BlocksTerm.TermId, "reward", 6f);
        return FitnessRecipeTests.Edited(seed, StunLockTerm.TermId, "tolerance", 0.4f);
    }

    private static EvolutionConfig ConfigWith(FitnessRecipe? recipe) => new()
    {
        Seed = 99,
        PopulationSize = 8,
        RoundsPerIndividual = 1,
        FitnessRecipe = recipe,
    };

    private static ulong Fingerprint(IReadOnlyList<GameGenome> population)
    {
        ulong hash = Fnv1a.OffsetBasis;
        foreach (GameGenome genome in population)
        {
            hash = Fnv1a.Hash(
                Encoding.UTF8.GetBytes(GameGenomeJson.Serialize(new GameRecord("g", null, genome))), hash);
        }
        return hash;
    }

    private static string TempRunDir() =>
        Path.Combine(Path.GetTempPath(), $"brawler-recipe-run-{Guid.NewGuid():N}");

    [Fact]
    public void ARecipeWinsOverTheVersionName()
    {
        var engine = new EvolutionEngine(ConfigWith(Custom()) with { FitnessName = "standard-v3" });
        Assert.Equal("custom/aggressive-shields", engine.FitnessFunction.Name);
    }

    [Fact]
    public void ARunScoredByARecipeRecordsItByValueAndResumesUnderIt()
    {
        FitnessRecipe recipe = Custom();
        string runDir = TempRunDir();
        try
        {
            var engine = new EvolutionEngine(ConfigWith(recipe));
            var history = new List<GenerationStats> { engine.Step() };
            RunStore.SaveCheckpoint(runDir, engine, ConfigWith(recipe), history);

            // The document is IN the manifest, not merely referenced by it.
            string manifest = File.ReadAllText(Path.Combine(runDir, RunStore.ManifestFileName));
            Assert.Contains("fitnessRecipe", manifest);
            Assert.Contains("custom/aggressive-shields", manifest);
            Assert.Contains(recipe.ContentHash.ToString(), manifest);

            (EvolutionEngine resumed, EvolutionConfig loaded, _) = RunStore.Load(runDir);
            Assert.NotNull(loaded.FitnessRecipe);
            Assert.Equal(recipe.ContentHash, loaded.FitnessRecipe!.ContentHash);
            Assert.Equal(recipe.Name, resumed.FitnessFunction.Name);

            // And it is the same instrument, not just the same name.
            foreach ((string label, BrawlerSim.Sim.MatchResult result) in FitnessFixtures.TwoPlayer)
            {
                Assert.True(
                    BitConverter.SingleToInt32Bits(recipe.ToFitness().Evaluate(result))
                        == BitConverter.SingleToInt32Bits(resumed.FitnessFunction.Evaluate(result)),
                    label);
            }
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void ResumingARecipeScoredRunMatchesAnUninterruptedOne()
    {
        // The determinism-suite property, extended to custom instruments: selection
        // depends on fitness, so if the resumed run rebuilt a different instrument the
        // populations would diverge.
        FitnessRecipe recipe = Custom();

        var straight = new EvolutionEngine(ConfigWith(recipe));
        var straightStats = new List<GenerationStats>();
        for (int gen = 0; gen < 5; gen++)
        {
            straightStats.Add(straight.Step());
        }

        string runDir = TempRunDir();
        try
        {
            var first = new EvolutionEngine(ConfigWith(recipe));
            var history = new List<GenerationStats>();
            for (int gen = 0; gen < 3; gen++)
            {
                history.Add(first.Step());
            }
            RunStore.SaveCheckpoint(runDir, first, ConfigWith(recipe), history);

            (EvolutionEngine resumed, _, List<GenerationStats> loadedHistory) = RunStore.Load(runDir);
            var resumedStats = new List<GenerationStats>(loadedHistory);
            for (int gen = 0; gen < 2; gen++)
            {
                resumedStats.Add(resumed.Step());
            }

            Assert.Equal(straightStats, resumedStats);
            Assert.Equal(Fingerprint(straight.Population), Fingerprint(resumed.Population));
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void EditingTheRecipeFileAfterTheRunCannotRescoreIt()
    {
        // Recorded BY VALUE is the whole point: the run resumes under what it ran with.
        FitnessRecipe recipe = Custom();
        string runDir = TempRunDir();
        string recipePath = Path.Combine(Path.GetTempPath(), $"recipe-{Guid.NewGuid():N}.json");
        try
        {
            FitnessRecipeJson.Save(recipe, recipePath);
            var engine = new EvolutionEngine(ConfigWith(FitnessRecipeJson.Load(recipePath)));
            RunStore.SaveCheckpoint(runDir, engine, ConfigWith(recipe), new List<GenerationStats> { engine.Step() });

            // Someone edits the file afterwards — or deletes it.
            FitnessRecipeJson.Save(
                FitnessRecipeTests.Edited(recipe, BlocksTerm.TermId, "reward", 99f), recipePath);
            File.Delete(recipePath);

            (EvolutionEngine resumed, EvolutionConfig loaded, _) = RunStore.Load(runDir);
            Assert.Equal(recipe.ContentHash, loaded.FitnessRecipe!.ContentHash);
            Assert.Equal(6f, ((IFitnessTermList)resumed.FitnessFunction).Terms
                .Single(t => t.Id == BlocksTerm.TermId).Values["reward"]);
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
            File.Delete(recipePath);
        }
    }

    [Fact]
    public void AHandEditedManifestRecipeIsRefusedRatherThanResumedUnderSilently()
    {
        FitnessRecipe recipe = Custom();
        string runDir = TempRunDir();
        try
        {
            var engine = new EvolutionEngine(ConfigWith(recipe));
            RunStore.SaveCheckpoint(runDir, engine, ConfigWith(recipe), new List<GenerationStats> { engine.Step() });

            // Tamper with a constant in the manifest, leaving the recorded hash alone.
            string path = Path.Combine(runDir, RunStore.ManifestFileName);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"reward\": 6", "\"reward\": 99"));

            var error = Assert.Throws<InvalidDataException>(() => RunStore.Load(runDir));
            Assert.Contains("different instrument", error.Message);
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void ManifestsWrittenBeforeRecipesExistedLoadUnchanged()
    {
        // Every run in runs/ predates this feature: no fitnessRecipe field at all, and
        // resume must keep behaving exactly as it did.
        string runDir = TempRunDir();
        try
        {
            var config = ConfigWith(null) with { FitnessName = "standard-v5" };
            var engine = new EvolutionEngine(config);
            RunStore.SaveCheckpoint(runDir, engine, config, new List<GenerationStats> { engine.Step() });

            string manifest = File.ReadAllText(Path.Combine(runDir, RunStore.ManifestFileName));
            Assert.DoesNotContain("fitnessRecipe", manifest); // nulls are omitted

            (EvolutionEngine resumed, EvolutionConfig loaded, _) = RunStore.Load(runDir);
            Assert.Null(loaded.FitnessRecipe);
            Assert.Equal("standard-v5", resumed.FitnessFunction.Name);
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void AMalformedEmbeddedRecipeFailsAtLoadNotMidRun()
    {
        FitnessRecipe recipe = Custom();
        string runDir = TempRunDir();
        try
        {
            var engine = new EvolutionEngine(ConfigWith(recipe));
            RunStore.SaveCheckpoint(runDir, engine, ConfigWith(recipe), new List<GenerationStats> { engine.Step() });

            string path = Path.Combine(runDir, RunStore.ManifestFileName);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"blocks\"", "\"blokcs\""));

            Assert.ThrowsAny<Exception>(() => RunStore.Load(runDir));
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }

    [Fact]
    public void ARecipeRunRecordsItsNameAsTheFitnessName()
    {
        // So a chart, a filename, or a report can never show it as a shipped version.
        FitnessRecipe recipe = Custom();
        string runDir = TempRunDir();
        try
        {
            var engine = new EvolutionEngine(ConfigWith(recipe));
            RunStore.SaveCheckpoint(runDir, engine, ConfigWith(recipe), new List<GenerationStats> { engine.Step() });

            using JsonDocument doc = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(runDir, RunStore.ManifestFileName)));
            Assert.Equal(recipe.Name, doc.RootElement.GetProperty("fitnessName").GetString());
        }
        finally
        {
            Directory.Delete(runDir, recursive: true);
        }
    }
}
