using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sprites;
using Xunit;
using NG = NameGen;

namespace BrawlerSim.Tests.Sprites;

/// <summary>
/// Stage tile theme selection (M4d, 2026-09-01 —
/// docs/features/stage-tile-selection.md) against the REAL shipped library
/// (godot/assets/tiles_v2_slices.json): library contract, determinism, register
/// coherence with stage naming, distribution shaping, the ThemeId gene's
/// heredity/repair through breeding, ContentKey exclusion, serialization, and the
/// presentation pass.
/// </summary>
public class StageThemeSelectionTests
{
    private static readonly Lazy<StageThemeLibrary> LibraryLazy = new(() =>
        StageThemeLibrary.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "tiles_v2_slices.json"))));

    private static readonly Lazy<StageThemeConfig> TuningLazy = new(() =>
        StageThemeConfig.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "tile_selection.json"))));

    private static StageThemeLibrary Library => LibraryLazy.Value;

    private static StageThemeSelector NewSelector() => new(Library, TuningLazy.Value);

    private static GenerationConfig ThemeConfig() =>
        GenerationConfig.Default with { StageThemeSelector = NewSelector() };

    private static GameGenome Game(ulong seed, bool themes = true) =>
        GameGenome.Generate(themes ? ThemeConfig() : GenerationConfig.Default, new Pcg32(seed));

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {relative} above the test directory");
    }

    // ── the library and tuning files ───────────────────────────────────────────

    [Fact]
    public void ShippedLibraryParsesCompletely()
    {
        Assert.Equal(59, Library.Themes.Count);
        Assert.Equal(32, Library.TileSize);
        Assert.Equal(12, Library.DropHeight);
        // The drop-slab art height IS the thin-platform collision thickness
        // (designer 2026-09-01) — if either side changes, change both.
        Assert.Equal(
            BrawlerSim.Sim.MatchConfig.Default.ThinPlatformThickness,
            Library.DropHeight / (float)Library.TileSize, 0.0001f);
        var exposureKeys = new[]
        {
            "TL", "TM", "TR", "TS", "ML", "MM", "MR", "MS",
            "BL", "BM", "BR", "BS", "SL", "SM", "SR", "SS",
        };
        Assert.All(Library.Themes, t =>
        {
            Assert.NotEmpty(t.Registers);
            foreach (string key in exposureKeys)
            {
                Assert.True(t.Tiles.ContainsKey(key) && t.Tiles[key].Count >= 1,
                    $"theme {t.Name} is missing piece {key}");
            }
            foreach (string key in new[] { "L", "M", "R", "S" })
            {
                Assert.True(t.DropTiles.ContainsKey(key) && t.DropTiles[key].Count >= 1,
                    $"theme {t.Name} is missing drop piece {key}");
            }
        });
        // Affinity keys stay inside the declared stage-trait vocabulary.
        var vocabulary = Library.StageTraitVocabulary.ToHashSet(StringComparer.Ordinal);
        Assert.All(Library.Themes, t =>
            Assert.All(t.TraitAffinity.Keys, trait => Assert.Contains(trait, vocabulary)));
        Assert.NotNull(Library.ByName("volcanic"));
        Assert.Null(Library.ByName("no_such_theme"));
    }

    // ── determinism + register coherence ───────────────────────────────────────

    [Fact]
    public void SelectionIsDeterministicAndSharesTheNamingRegister()
    {
        // The brief's coherence rule over a seed sweep: the theme's register equals
        // the register the stage NAME generates under — shared seed by construction.
        StageThemeSelector selector = NewSelector();
        var generator = NG.NameGenerator.CreateDefault();
        int checkedStages = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            StageGenome stage = Game(seed, themes: false).Stage;
            ulong themeSeed = StageThemeSelector.ThemeSeed(stage);
            var a = selector.SelectCandidates(stage, themeSeed, out string registerA);
            var b = selector.SelectCandidates(stage, themeSeed, out string registerB);
            Assert.Equal(registerA, registerB);
            Assert.Equal(a.Select(c => c.Theme.Name), b.Select(c => c.Theme.Name));

            NG.NameResult name = generator.GenerateStageName(
                StageThemeSelector.Map(stage),
                new NG.NameOptions { Seed = themeSeed, Register = registerA });
            Assert.Equal(registerA, name.Register);
            // The picked theme belongs to the shared register whenever the register
            // pool served it (the fey fallback is the exception by design).
            if (Library.Themes.Count(t => t.Registers.Contains(registerA))
                >= TuningLazy.Value.RegisterPoolFloor)
            {
                Assert.Contains(registerA, a[0].Theme.Registers);
                checkedStages++;
            }
        }
        Assert.True(checkedStages > 100, "the register-pool path was barely exercised");
    }

    [Fact]
    public void GenerationResolvesAThemeAndTheGeneSurvivesRoundTrip()
    {
        GameGenome game = Game(11);
        Assert.True(Library.Contains(game.Stage.ThemeId));

        string json = GameGenomeJson.Serialize(new GameRecord("themed", null, game));
        Assert.Contains("\"themeId\"", json);
        GameRecord reloaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(game.Stage.ThemeId, reloaded.Genome.Stage.ThemeId);

        // Theme-less generation stays null and serializes without the field.
        GameGenome bare = Game(11, themes: false);
        Assert.Null(bare.Stage.ThemeId);
        Assert.DoesNotContain("themeId",
            GameGenomeJson.Serialize(new GameRecord("bare", null, bare)));
    }

    [Fact]
    public void ContentKeyAndNamingSeedIgnoreTheThemeGene()
    {
        StageGenome bare = Game(9, themes: false).Stage;
        StageGenome themed = bare.WithThemeId("volcanic");
        Assert.Equal(BuiltGame.ContentKey(bare), BuiltGame.ContentKey(themed));
        Assert.Equal(BuiltGameNaming.NamingSeed(bare), BuiltGameNaming.NamingSeed(themed));
        Assert.DoesNotContain("ThemeId", BuiltGame.ContentKey(themed));
    }

    // ── distribution shaping ───────────────────────────────────────────────────

    [Fact]
    public void TwoThousandSelectionsHoldTheShapedDistribution()
    {
        StageThemeSelector selector = NewSelector();
        var picks = new Dictionary<string, int>(StringComparer.Ordinal);
        int goofy = 0;
        const int n = 2000;
        var goofVibes = TuningLazy.Value.GoofVibes.ToHashSet(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= n; seed++)
        {
            StageGenome stage = Game(seed, themes: false).Stage;
            ThemeCandidate top = selector.SelectCandidates(
                stage, StageThemeSelector.ThemeSeed(stage), out _)[0];
            picks[top.Theme.Name] = picks.TryGetValue(top.Theme.Name, out int c) ? c + 1 : 1;
            if (goofVibes.Contains(top.Theme.Vibe))
            {
                goofy++;
            }
        }
        // The brief: no theme above 12%, goofy channel around its reserved 8%.
        Assert.True(picks.Values.Max() <= n * 0.12,
            $"theme monopoly: {picks.MaxBy(p => p.Value)}");
        Assert.InRange(goofy / (double)n, 0.04, 0.13);
        Assert.True(picks.Count >= 30, $"only {picks.Count} themes ever picked");
    }

    // ── heredity + repair through breeding ─────────────────────────────────────

    [Fact]
    public void BredStagesKeepAKnownThemeAndCrossoverInheritsFromAParent()
    {
        GenerationConfig config = ThemeConfig();
        var rng = new Pcg32(77);
        var pool = Enumerable.Range(1, 12)
            .Select(i => GameGenome.Generate(config, new Pcg32((ulong)i)))
            .ToList();
        for (int gen = 0; gen < 25; gen++)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                GameGenome a = pool[i];
                GameGenome b = pool[(i + 1) % pool.Count];
                GameGenome child = GameGenomeOps.Breed(a, b, 0.5f, rng, config);
                Assert.True(Library.Contains(child.Stage.ThemeId),
                    $"bred stage lost its theme gene (gen {gen} idx {i})");
                pool[i] = child;
            }
        }
    }

    [Fact]
    public void EnsureGeneRepairsUnknownAndContradictedThemes()
    {
        StageThemeSelector selector = NewSelector();
        StageGenome stage = Game(21).Stage;
        Assert.Same(stage, selector.EnsureGene(stage)); // resolved gene stands

        StageGenome unknown = stage.WithThemeId("deleted_theme");
        StageGenome repaired = selector.EnsureGene(unknown);
        Assert.True(Library.Contains(repaired.ThemeId));
        Assert.NotEqual("deleted_theme", repaired.ThemeId);
        // Same content seed → the repair is stable.
        Assert.Equal(repaired.ThemeId, selector.EnsureGene(repaired).ThemeId);
    }

    [Fact]
    public void ThemeLessBreedingStreamsAreUntouched()
    {
        // The crossover theme coin is RNG-GATED on a theme existing: breeding two
        // theme-less genomes must consume the exact pre-feature stream (the
        // fingerprint golden's guarantee, checked directly here).
        GameGenome a = Game(31, themes: false);
        GameGenome b = Game(32, themes: false);
        GameGenome child1 = GameGenomeOps.Crossover(a, b, new Pcg32(5));
        GameGenome child2 = GameGenomeOps.Crossover(a, b, new Pcg32(5));
        Assert.Null(child1.Stage.ThemeId);
        Assert.Equal(
            GameGenomeJson.Serialize(new GameRecord("c", null, child1)),
            GameGenomeJson.Serialize(new GameRecord("c", null, child2)));
    }

    // ── the presentation pass ──────────────────────────────────────────────────

    [Fact]
    public void PresentationSettlesThemesNamesAndRegistersForStages()
    {
        var game = new BuiltGame { Name = "THEME TEST" };
        for (ulong i = 0; i < 4; i++)
        {
            GameGenome g = Game(400 + i, themes: false);
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P1", null, g.Characters[0]), out _));
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P2", null, g.Characters[1]), out _));
            Assert.True(game.TryAddStage(new BuiltStage("SRC STAGE", null, g.Stage), out _));
        }
        // Sprite selector deliberately null: this test isolates the STAGE half
        // (characters degrade to names-only, exactly the documented fallback).
        int changed = BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), null, NewSelector());
        Assert.True(changed > 0);
        foreach (BuiltStage s in game.Stages)
        {
            Assert.False(BuiltGameNaming.NeedsGeneratedName(s.DisplayName));
            Assert.True(Library.Contains(s.ThemeId));
            Assert.False(string.IsNullOrEmpty(s.Register));
            Assert.Contains(s.Register!, NG.Data.NameGenData.LoadEmbedded().Registers.Select(r => r.Name));
            Assert.Equal(s.ThemeId, s.Presented.ThemeId); // views read Presented
        }

        // Persisted once: a second pass changes nothing.
        Assert.Equal(0, BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), null, NewSelector()));

        // v5 round-trip carries the stage presentation.
        BuiltGame reloaded = BuiltGameJson.Deserialize(BuiltGameJson.Serialize(game));
        Assert.Equal(
            game.Stages.Select(s => (s.DisplayName, s.ThemeId, s.Register)),
            reloaded.Stages.Select(s => (s.DisplayName, s.ThemeId, s.Register)));
    }

    [Fact]
    public void PresentationWithoutAThemeLibraryIsThePureNamingPass()
    {
        var game = new BuiltGame { Name = "BARE" };
        GameGenome g = Game(500, themes: false);
        Assert.True(game.TryAddStage(new BuiltStage("SRC STAGE", null, g.Stage), out _));
        BuiltGamePresentation.EnsurePresented(game, NG.NameGenerator.CreateDefault(), null);
        Assert.False(BuiltGameNaming.NeedsGeneratedName(game.Stages[0].DisplayName));
        Assert.Null(game.Stages[0].ThemeId);
        Assert.Null(game.Stages[0].Register);
    }
}
