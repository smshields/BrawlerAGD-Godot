using BrawlerSim.Backgrounds;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sprites;
using Xunit;
using NG = NameGen;

namespace BrawlerSim.Tests.Backgrounds;

/// <summary>
/// Background selection (backgrounds track Phase 1, 2026-09-02 —
/// docs/background-implementation-brief.md) against the REAL shipped corpus index
/// (godot/assets/backgrounds_v1/): library contract + license invariants,
/// determinism, register coherence with naming/tiles, distribution shaping, the
/// BackgroundId gene's heredity/repair through breeding, ContentKey exclusion,
/// remap legality, the parametric variant, the presentation pass, and the credits
/// model the credits screens render from.
/// </summary>
public class BackgroundSelectionTests
{
    private static readonly Lazy<BackgroundLibrary> LibraryLazy = new(() =>
        BackgroundLibrary.LoadFile(Repo("backgrounds_v1_index.json")));

    private static readonly Lazy<BackgroundPalette> PaletteLazy = new(() =>
        BackgroundPalette.LoadFiles(
            Repo("master_palette.json"),
            Repo("background_ramps_bidir.json"),
            Repo("remap_transition_table.json")));

    private static readonly Lazy<BackgroundSelectionConfig> TuningLazy = new(() =>
        BackgroundSelectionConfig.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "background_selection.json"))));

    private static readonly Lazy<StageThemeLibrary> ThemesLazy = new(() =>
        StageThemeLibrary.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "tiles_v2_slices.json"))));

    private static BackgroundLibrary Library => LibraryLazy.Value;

    private static string Repo(string file) =>
        FindRepoFile(Path.Combine("godot", "assets", "backgrounds_v1", file));

    private static BackgroundSelector NewSelector() =>
        new(Library, PaletteLazy.Value, TuningLazy.Value, ThemesLazy.Value);

    private static StageThemeSelector NewThemeSelector() => new(ThemesLazy.Value);

    private static GenerationConfig FullConfig() => GenerationConfig.Default with
    {
        StageThemeSelector = NewThemeSelector(),
        BackgroundSelector = NewSelector(),
    };

    private static GameGenome Game(ulong seed, bool backgrounds = true) =>
        GameGenome.Generate(
            backgrounds ? FullConfig() : GenerationConfig.Default, new Pcg32(seed));

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

    // ── the shipped index and its invariants ───────────────────────────────────

    [Fact]
    public void ShippedIndexParsesCleanlyAndHoldsTheLicenseInvariants()
    {
        Assert.Equal("bg-v1", Library.Contract);
        Assert.Empty(Library.Refused); // the shipped index must be invariant-clean
        Assert.True(Library.Entries.Count >= 900, $"only {Library.Entries.Count} entries");
        Assert.All(Library.Entries, e =>
        {
            // Attribution present exactly when the license requires it.
            if (BackgroundLibrary.RequiresAttribution(e.License))
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Attribution), e.Id);
            }
            // Descriptors are the 8x8 Lab thumbnail (192 bytes) or absent.
            Assert.True(e.Descriptor.Length is 0 or 192, e.Id);
            // Remap targets stay inside the declared vocabulary.
            Assert.All(e.Remaps, r => Assert.Contains(r, Library.RemapTargets));
        });
        // Every role the brief names is populated.
        foreach (string role in new[] { "full", "far", "mid", "element" })
        {
            Assert.Contains(Library.Entries, e => e.LayerRole == role);
        }
        // Affinity keys stay inside the declared stage-trait vocabulary.
        var vocabulary = Library.TraitVocabulary.ToHashSet(StringComparer.Ordinal);
        Assert.All(Library.Entries, e =>
            Assert.All(e.TraitAffinity.Keys, t => Assert.Contains(t, vocabulary)));
    }

    [Fact]
    public void LoaderRefusesUnknownLicensesAndMissingAttribution()
    {
        const string json = """
        {
          "contract": "bg-v1", "version": "test",
          "entries": [
            {"id": "ok", "file": "full/ok.png", "size": [480, 270],
             "layerRole": "full", "license": "CC0"},
            {"id": "bad_license", "file": "full/b.png", "size": [480, 270],
             "layerRole": "full", "license": "CC-BY-SA-4.0"},
            {"id": "no_attr", "file": "full/c.png", "size": [480, 270],
             "layerRole": "full", "license": "CC-BY-4.0"},
            {"id": "attr_ok", "file": "full/d.png", "size": [480, 270],
             "layerRole": "full", "license": "CC-BY-4.0",
             "author": "A", "attribution": "Art by A, CC-BY 4.0"}
          ]
        }
        """;
        BackgroundLibrary library = BackgroundLibrary.Parse(json);
        Assert.Equal(new[] { "ok", "attr_ok" }, library.Entries.Select(e => e.Id));
        Assert.Equal(2, library.Refused.Count);
        Assert.Contains(library.Refused, r => r.Contains("bad_license"));
        Assert.Contains(library.Refused, r => r.Contains("no_attr"));
        // A wrong contract is a hard error, not a refusal.
        Assert.Throws<NotSupportedException>(() =>
            BackgroundLibrary.Parse("""{"contract": "bg-v2", "entries": []}"""));
    }

    // ── determinism + register coherence ───────────────────────────────────────

    [Fact]
    public void SelectionIsDeterministicAndSharesTheNamingAndTileRegister()
    {
        BackgroundSelector selector = NewSelector();
        StageThemeSelector themes = NewThemeSelector();
        var generator = NG.NameGenerator.CreateDefault();
        int registerPoolStages = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            StageGenome stage = Game(seed, backgrounds: false).Stage;
            ulong sharedSeed = BackgroundSelector.BackgroundSeed(stage);
            var a = selector.SelectCandidates(stage, sharedSeed, null, out string registerA);
            var b = selector.SelectCandidates(stage, sharedSeed, null, out string registerB);
            Assert.Equal(registerA, registerB);
            Assert.Equal(a.Select(c => c.Entry.Id), b.Select(c => c.Entry.Id));

            // Coherence by construction: the background register equals the tile
            // theme selector's register AND the register the stage name generates
            // under, from the same shared seed.
            themes.SelectCandidates(stage, sharedSeed, out string themeRegister);
            Assert.Equal(themeRegister, registerA);
            NG.NameResult name = generator.GenerateStageName(
                StageThemeSelector.Map(stage),
                new NG.NameOptions { Seed = sharedSeed, Register = registerA });
            Assert.Equal(registerA, name.Register);

            // The picked entry serves the shared register whenever the register pool
            // did (fey and thin pools fall back to the full library by design).
            if (Library.Entries.Count(e => e.LayerRole == "full"
                && e.Register.Contains(registerA)) >= TuningLazy.Value.RegisterPoolFloor)
            {
                Assert.Contains(registerA, a[0].Entry.Register);
                registerPoolStages++;
            }
        }
        Assert.True(registerPoolStages > 100, "the register-pool path was barely exercised");
    }

    [Fact]
    public void GenerationResolvesAGeneAndItSurvivesRoundTrip()
    {
        GameGenome game = Game(11);
        Assert.True(Library.Contains(game.Stage.BackgroundId));
        Assert.True(ThemesLazy.Value.Contains(game.Stage.ThemeId)); // themes ride along

        string json = GameGenomeJson.Serialize(new GameRecord("bg", null, game));
        Assert.Contains("\"backgroundId\"", json);
        GameRecord reloaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(game.Stage.BackgroundId, reloaded.Genome.Stage.BackgroundId);

        // Background-less generation stays null and serializes without the field —
        // the legacy path (pre-v14 files) byte-for-byte.
        GameGenome bare = Game(11, backgrounds: false);
        Assert.Null(bare.Stage.BackgroundId);
        Assert.DoesNotContain("backgroundId",
            GameGenomeJson.Serialize(new GameRecord("bare", null, bare)));
    }

    [Fact]
    public void ContentKeyAndNamingSeedIgnoreTheBackgroundGene()
    {
        StageGenome bare = Game(9, backgrounds: false).Stage;
        StageGenome bg = bare.WithBackgroundId(Library.Entries[0].Id);
        Assert.Equal(BuiltGame.ContentKey(bare), BuiltGame.ContentKey(bg));
        Assert.Equal(BuiltGameNaming.NamingSeed(bare), BuiltGameNaming.NamingSeed(bg));
        Assert.DoesNotContain("BackgroundId", BuiltGame.ContentKey(bg));
    }

    // ── distribution shaping ───────────────────────────────────────────────────

    [Fact]
    public void TwoThousandSelectionsSpreadAcrossTheCorpus()
    {
        BackgroundSelector selector = NewSelector();
        var picks = new Dictionary<string, int>(StringComparer.Ordinal);
        var registers = new HashSet<string>(StringComparer.Ordinal);
        const int n = 2000;
        for (ulong seed = 1; seed <= n; seed++)
        {
            StageGenome stage = Game(seed, backgrounds: false).Stage;
            BackgroundCandidate top = selector.SelectCandidates(
                stage, BackgroundSelector.BackgroundSeed(stage), null, out string register)[0];
            picks[top.Entry.Id] = picks.TryGetValue(top.Entry.Id, out int c) ? c + 1 : 1;
            registers.Add(register);
        }
        // The brief's spread test: no entry above 2% of picks (small sampling slack),
        // every register reachable, a wide slice of the corpus in actual use.
        Assert.True(picks.Values.Max() <= n * 0.025,
            $"background monopoly: {picks.MaxBy(p => p.Value)}");
        Assert.Superset(new HashSet<string> { "fantasy", "scifi", "horror", "normal" }, registers);
        Assert.True(registers.Count >= 4, "not every register was reached");
        Assert.True(picks.Count >= 100, $"only {picks.Count} distinct backgrounds picked");
    }

    // ── heredity + repair through breeding ─────────────────────────────────────

    [Fact]
    public void BredStagesKeepAKnownBackgroundGene()
    {
        GenerationConfig config = FullConfig();
        var rng = new Pcg32(77);
        var pool = Enumerable.Range(1, 12)
            .Select(i => GameGenome.Generate(config, new Pcg32((ulong)i)))
            .ToList();
        for (int gen = 0; gen < 25; gen++)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                GameGenome child = GameGenomeOps.Breed(
                    pool[i], pool[(i + 1) % pool.Count], 0.5f, rng, config);
                Assert.True(Library.Contains(child.Stage.BackgroundId),
                    $"bred stage lost its background gene (gen {gen} idx {i})");
                pool[i] = child;
            }
        }
    }

    [Fact]
    public void EnsureGeneRepairsUnknownIdsAndIsStable()
    {
        BackgroundSelector selector = NewSelector();
        StageGenome stage = Game(21).Stage;
        Assert.Same(stage, selector.EnsureGene(stage)); // resolved gene stands

        StageGenome unknown = stage.WithBackgroundId("deleted_background");
        StageGenome repaired = selector.EnsureGene(unknown);
        Assert.True(Library.Contains(repaired.BackgroundId));
        Assert.NotEqual("deleted_background", repaired.BackgroundId);
        Assert.Equal(repaired.BackgroundId, selector.EnsureGene(repaired).BackgroundId);
    }

    [Fact]
    public void BackgroundLessBreedingStreamsAreUntouched()
    {
        // The crossover background coin is RNG-GATED on a background existing:
        // breeding two background-less genomes must consume the exact pre-feature
        // stream (the fingerprint golden's guarantee, checked directly here).
        GameGenome a = Game(31, backgrounds: false);
        GameGenome b = Game(32, backgrounds: false);
        GameGenome child1 = GameGenomeOps.Crossover(a, b, new Pcg32(5));
        GameGenome child2 = GameGenomeOps.Crossover(a, b, new Pcg32(5));
        Assert.Null(child1.Stage.BackgroundId);
        Assert.Equal(
            GameGenomeJson.Serialize(new GameRecord("c", null, child1)),
            GameGenomeJson.Serialize(new GameRecord("c", null, child2)));
    }

    // ── remap legality + post-remap harmony ────────────────────────────────────

    [Fact]
    public void RemapPicksStayInsideTheEntrysValidatedTargets()
    {
        BackgroundSelector selector = NewSelector();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            StageGenome stage = Game(seed).Stage;
            BackgroundEntry entry = Library.ById(stage.BackgroundId)!;
            string? remap = selector.PickRemap(
                entry, stage.ThemeId, BackgroundSelector.BackgroundSeed(stage));
            if (remap is not null)
            {
                Assert.Contains(remap, entry.Remaps);
                Assert.Contains(remap, PaletteLazy.Value.TransitionsFor(entry.PaletteGroup));
            }
        }
    }

    [Fact]
    public void HarmonyIsScoredOnThePostRemapGroup()
    {
        // Find a real (entry, theme) pair where a remap target harmonizes strictly
        // better than the native palette — the settle must take the remap.
        BackgroundSelector selector = NewSelector();
        int proven = 0;
        foreach (ThemeDef theme in ThemesLazy.Value.Themes)
        {
            string? themeGroup = selector.ThemeGroup(theme.Name);
            if (themeGroup is null)
            {
                continue;
            }
            foreach (BackgroundEntry entry in Library.Entries.Where(e => e.LayerRole == "full"))
            {
                double native = selector.GroupHarmony(entry.PaletteGroup, themeGroup);
                var better = selector.LegalRemaps(entry)
                    .Where(t => selector.GroupHarmony(t, themeGroup)
                        > native + TuningLazy.Value.RemapNoneBonus + 1e-9)
                    .ToList();
                if (better.Count == 0)
                {
                    continue;
                }
                string? picked = selector.PickRemap(entry, theme.Name, seed: 123);
                Assert.NotNull(picked);
                Assert.True(
                    selector.GroupHarmony(picked!, themeGroup)
                        >= better.Max(t => selector.GroupHarmony(t, themeGroup)) - 1e-9,
                    $"{entry.Id} vs {theme.Name}: picked {picked}");
                proven++;
                break; // one proof per theme is plenty
            }
            if (proven >= 10)
            {
                break;
            }
        }
        Assert.True(proven >= 10, "no harmony-improving remap pairs found in the corpus");
    }

    [Fact]
    public void RemapColorTransformIsDeterministicAndPoolSnapped()
    {
        BackgroundPalette palette = PaletteLazy.Value;
        var color = ((byte)80, (byte)120, (byte)200);
        var once = palette.RemapColor(color, "blue", "violet");
        Assert.Equal(once, palette.RemapColor(color, "blue", "violet"));
        // Night darkens, grey desaturates.
        var night = palette.RemapColor(color, "blue", "night");
        Assert.True(night.Item1 + night.Item2 + night.Item3
            < color.Item1 + color.Item2 + color.Item3);
        var grey = palette.RemapColor(((byte)200, (byte)60, (byte)60), "red", "grey");
        int spread = Math.Max(grey.Item1, Math.Max(grey.Item2, grey.Item3))
            - Math.Min(grey.Item1, Math.Min(grey.Item2, grey.Item3));
        Assert.True(spread < 30, $"grey remap kept a strong hue: {grey}");
    }

    // ── the parametric variant ─────────────────────────────────────────────────

    [Fact]
    public void VariantIsDeterministicAndInBounds()
    {
        BackgroundSelector selector = NewSelector();
        BackgroundSelectionConfig config = TuningLazy.Value;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            StageGenome stage = Game(seed).Stage;
            BackgroundEntry entry = Library.ById(stage.BackgroundId)!;
            ulong s = BackgroundSelector.BackgroundSeed(stage);
            BackgroundVariant v = selector.Variant(entry, stage, s);
            Assert.Equal(v, selector.Variant(entry, stage, s));

            Assert.InRange(v.Crop.X, 0, entry.Width - v.Crop.W);
            Assert.InRange(v.Crop.Y, 0, entry.Height - v.Crop.H);
            Assert.True(v.Crop.W >= 1 && v.Crop.H >= 1);
            // The crop matches the stage's kill-box aspect (what the quad covers).
            Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
            double aspect = blast.X / (double)blast.Y;
            Assert.InRange(v.Crop.W / (double)v.Crop.H, aspect * 0.9, aspect * 1.1);

            Assert.InRange(v.Brightness, 1f - config.BrightnessJitter, 1f + config.BrightnessJitter);
            Assert.InRange(v.Contrast, 1f - config.ContrastJitter, 1f + config.ContrastJitter);
            Assert.InRange(v.BlurScale, config.BlurScaleMin, config.BlurScaleMax);

            // Horizon anchoring: when the entry has a horizon and the crop can move
            // vertically, the horizon lands inside (or clamped toward) the band.
            if (entry.HorizonY is { } horizon && v.Crop.H < entry.Height)
            {
                double f = (horizon - v.Crop.Y) / (double)v.Crop.H;
                if (v.Crop.Y > 0 && v.Crop.Y < entry.Height - v.Crop.H)
                {
                    Assert.InRange(f, config.HorizonBandMin - 0.01, config.HorizonBandMax + 0.01);
                }
            }
        }
    }

    // ── the presentation pass + built-game v6 ──────────────────────────────────

    [Fact]
    public void PresentationSettlesBackgroundsWithThemesAndHoldsLineupDistinctness()
    {
        var game = new BuiltGame { Name = "BG TEST" };
        for (ulong i = 0; i < 4; i++)
        {
            GameGenome g = Game(600 + i, backgrounds: false);
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P1", null, g.Characters[0]), out _));
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P2", null, g.Characters[1]), out _));
            Assert.True(game.TryAddStage(new BuiltStage("SRC STAGE", null, g.Stage), out _));
        }
        int changed = BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), null, NewThemeSelector(), NewSelector());
        Assert.True(changed > 0);

        var descriptors = new List<byte[]>();
        foreach (BuiltStage s in game.Stages)
        {
            Assert.False(BuiltGameNaming.NeedsGeneratedName(s.DisplayName));
            Assert.True(Library.Contains(s.BackgroundId));
            Assert.True(ThemesLazy.Value.Contains(s.ThemeId));
            Assert.False(string.IsNullOrEmpty(s.Register));
            BackgroundEntry entry = Library.ById(s.BackgroundId)!;
            if (s.BackgroundRemap is { } remap)
            {
                Assert.Contains(remap, entry.Remaps);
            }
            Assert.Equal(s.BackgroundId, s.Presented.BackgroundId); // views read Presented
            if (entry.Descriptor.Length > 0)
            {
                descriptors.Add(entry.Descriptor);
            }
        }
        // No two stages in one game under the descriptor minimum (the brief's rule).
        for (int i = 0; i < descriptors.Count; i++)
        {
            for (int j = i + 1; j < descriptors.Count; j++)
            {
                Assert.True(BackgroundEntry.DescriptorDistance(descriptors[i], descriptors[j])
                    >= TuningLazy.Value.DescriptorMinDistance,
                    $"stages {i} and {j} are perceptually near-identical");
            }
        }

        // Persisted once: a second pass changes nothing.
        Assert.Equal(0, BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), null, NewThemeSelector(), NewSelector()));

        // v6 round-trip carries the background presentation.
        BuiltGame reloaded = BuiltGameJson.Deserialize(BuiltGameJson.Serialize(game));
        Assert.Equal(
            game.Stages.Select(s => (s.BackgroundId, s.BackgroundRemap)),
            reloaded.Stages.Select(s => (s.BackgroundId, s.BackgroundRemap)));
    }

    [Fact]
    public void LegacyEntriesWithoutTheGenePresentTheOldPath()
    {
        // A pre-v14 stage (null gene, no selector attached) renders the blank
        // backdrop: Presented is the untouched genome.
        GameGenome g = Game(700, backgrounds: false);
        var entry = new BuiltStage("KEPT NAME", null, g.Stage);
        Assert.Null(entry.Presented.BackgroundId);
        Assert.Same(g.Stage, entry.Presented);
    }

    // ── the credits model (the legal gate) ─────────────────────────────────────

    [Fact]
    public void CreditsModelCarriesEveryRequiredAttributionInTheIndex()
    {
        BackgroundCreditsModel model = BackgroundCredits.Build(Library);
        var expected = Library.Entries
            .Where(e => BackgroundLibrary.RequiresAttribution(e.License))
            .Select(e => e.Attribution!)
            .ToHashSet(StringComparer.Ordinal);
        var rendered = model.Required
            .SelectMany(a => a.Attributions)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(expected.SetEquals(rendered),
            "credits model dropped or invented attribution strings");
        Assert.NotEmpty(expected); // the shipped corpus does carry CC-BY/OGA-BY work
        Assert.NotEmpty(model.CourtesyAuthors);
        // Grouped by author, each group non-empty and sorted for stable rendering.
        Assert.All(model.Required, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Author));
            Assert.NotEmpty(a.Attributions);
        });
    }
}
