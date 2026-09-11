using BrawlerSim.Backgrounds;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sprites;
using Xunit;

namespace BrawlerSim.Tests.Backgrounds;

/// <summary>
/// Parallax recombination (backgrounds track Phase 2, 2026-09-02 — brief §Phase 2):
/// the three runtime pairing predicates + pairExclude over recombined stages, the
/// goof lane, composite-gene round-trips and atomic crossover/repair, path
/// reachability at the tuned probability, and the render layout's bounds.
/// </summary>
public class BackgroundRecombinationTests
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

    private static string Repo(string file) =>
        FindRepoFile(Path.Combine("godot", "assets", "backgrounds_v1", file));

    private static BackgroundSelector NewSelector() =>
        new(LibraryLazy.Value, PaletteLazy.Value, TuningLazy.Value, ThemesLazy.Value);

    private static GenerationConfig FullConfig() => GenerationConfig.Default with
    {
        StageThemeSelector = new StageThemeSelector(ThemesLazy.Value),
        BackgroundSelector = NewSelector(),
    };

    private static StageGenome Stage(ulong seed) =>
        GameGenome.Generate(FullConfig(), new Pcg32(seed)).Stage;

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

    // ── the composite gene format ──────────────────────────────────────────────

    [Fact]
    public void CompositeGeneRoundTripsItsParts()
    {
        var composite = new BackgroundComposite("sky_a", "town_b", "moon_c", "night");
        Assert.Equal("far:sky_a|mid:town_b|near:moon_c|remap:night", composite.ToGene());
        Assert.Equal(composite, BackgroundComposite.TryParse(composite.ToGene()));

        var bare = new BackgroundComposite("sky_a", "town_b", null, null);
        Assert.Equal("far:sky_a|mid:town_b|near:none|remap:none", bare.ToGene());
        Assert.Equal(bare, BackgroundComposite.TryParse(bare.ToGene()));

        // A pre-v0.4 three-part gene still parses — near absent, flagged for repair.
        BackgroundComposite legacy =
            BackgroundComposite.TryParse("far:sky_a|mid:town_b|remap:night")!;
        Assert.Equal("sky_a", legacy.FarId);
        Assert.Null(legacy.NearId);
        Assert.False(legacy.HasNear);

        Assert.Null(BackgroundComposite.TryParse("plain_single_id"));
        Assert.Null(BackgroundComposite.TryParse("far:only_far"));
        Assert.Null(BackgroundComposite.TryParse(null));
        Assert.False(BackgroundComposite.IsComposite("plain_single_id"));
    }

    // ── predicates over recombined stages ──────────────────────────────────────

    [Fact]
    public void RecombinedStagesSatisfyThePairingPredicates()
    {
        BackgroundSelector selector = NewSelector();
        BackgroundSelectionConfig config = TuningLazy.Value;
        int composites = 0, singles = 0, goofChecked = 0, nears = 0, midRegisterWaived = 0;
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            StageGenome bare = GameGenome.Generate(
                GenerationConfig.Default, new Pcg32(seed)).Stage;
            ulong s = BackgroundSelector.BackgroundSeed(bare);
            BackgroundSpec spec = selector.ResolveSpec(bare, s, null);
            // Determinism of the whole spec.
            Assert.Equal(spec, selector.ResolveSpec(bare, s, null));
            if (!spec.IsComposite)
            {
                singles++;
                continue;
            }
            composites++;
            BackgroundEntry far = spec.Far!;
            BackgroundEntry mid = spec.Mid!;
            Assert.Equal("far", far.LayerRole);
            Assert.Equal("mid", mid.LayerRole);

            // Detail floor (remediation §3): no stack is boxes all the way down.
            Assert.True(
                far.Metrics.Detail >= config.DetailFloor
                    || (!mid.BoxRisk && mid.Metrics.Detail >= config.DetailFloor),
                $"box stack: {far.Id} + {mid.Id}");

            // The near layer (remediation §2, in the gene since v0.4): a discrete
            // prop, blocklist-clean against BOTH lower layers.
            if (spec.Near is { } near)
            {
                nears++;
                Assert.Equal("element", near.LayerRole);
                Assert.True(Math.Max(near.Width, near.Height)
                    <= config.NearMaxAspect * Math.Min(near.Width, near.Height) + 1e-3f,
                    $"strip-shaped near: {near.Id}");
                Assert.False(BackgroundSelector.PairExcluded(near, far));
                Assert.False(BackgroundSelector.PairExcluded(near, mid));
            }

            // (a) register intersection contains the stage register — except the
            // goof lane, where the waiver is the point; the full-library fallback
            // for thin register pools (mirroring Phase 1); and the MID's necessity
            // waiver (v0.4: an empty register-matched mid tier waives the predicate
            // rather than abandoning the stack — bounded below).
            string register = selector.PickRegister(s);
            bool farPoolServed = LibraryLazy.Value.Entries.Count(e =>
                e.LayerRole == "far" && e.Register.Contains(register))
                    >= TuningLazy.Value.RegisterPoolFloor;
            if (!spec.Goof && farPoolServed)
            {
                Assert.Contains(register, far.Register);
                if (!mid.Register.Contains(register))
                {
                    midRegisterWaived++;
                }
            }
            if (spec.Goof)
            {
                goofChecked++;
            }

            // (b) the unified remap is one the pair legally shares.
            Assert.Contains(spec.Remap, selector.SharedRemapCandidates(far, mid));

            // pairExclude holds in BOTH lanes.
            Assert.False(BackgroundSelector.PairExcluded(far, mid));

            // (c) ordering violations are answered by the FORCED seam haze.
            BackgroundLayout layout = selector.Layout(
                bare.WithBackgroundId(spec.Gene()), s)!;
            Assert.Equal(
                selector.OrderingHolds(far, mid)
                    ? TuningLazy.Value.SeamHazeBase
                    : TuningLazy.Value.SeamHazeForced,
                layout.SeamHaze);
        }
        // v0.4 (designer): the three-layer stack is the DEFAULT and the single path
        // an EXTREME fallback — over the real corpus it should never fire, and the
        // near plane should resolve everywhere.
        Assert.Equal(0, singles);
        Assert.Equal(composites, nears);
        // The goof lane fires at roughly its budget, and the mid's necessity waiver
        // stays the exception, not the rule.
        Assert.InRange(goofChecked, 1, composites / 3);
        Assert.InRange(midRegisterWaived, 0, composites / 3);
    }

    [Fact]
    public void GoofLaneStillHonorsSharedRemapAndBlocklist()
    {
        // Synthetic corpus: a goof pair from disjoint registers is legal ONLY
        // through the unified remap and the blocklist. The far tagged "space"
        // excludes daylight mids — goof never overrides pairExclude.
        const string json = """
        {
          "contract": "bg-v1", "version": "test",
          "remapTargets": ["grey", "night"],
          "entries": [
            {"id": "nebula", "file": "far/nebula.png", "size": [480, 270],
             "layerRole": "far", "license": "CC0", "paletteGroup": "violet",
             "register": ["scifi"], "scene": ["space"],
             "pairExclude": ["daylight"],
             "remaps": ["grey", "night"],
             "metrics": {"satMean": 0.2, "contrastBand": 10, "valMean": 0.8,
                         "domLightColor": [200, 190, 220]}},
            {"id": "meadow_town", "file": "mid/meadow.png", "size": [480, 200],
             "layerRole": "mid", "license": "CC0", "paletteGroup": "green",
             "register": ["normal"], "scene": ["daylight", "forest"],
             "remaps": ["grey", "night"],
             "metrics": {"satMean": 0.3, "contrastBand": 12, "valMean": 0.5,
                         "domLightColor": [120, 160, 90]}},
            {"id": "night_town", "file": "mid/night.png", "size": [480, 200],
             "layerRole": "mid", "license": "CC0", "paletteGroup": "blue",
             "register": ["normal"], "scene": ["city"],
             "remaps": ["grey", "night"],
             "metrics": {"satMean": 0.2, "contrastBand": 12, "valMean": 0.4,
                         "domLightColor": [60, 70, 110]}}
          ]
        }
        """;
        var library = BackgroundLibrary.Parse(json);
        var selector = new BackgroundSelector(library, PaletteLazy.Value, TuningLazy.Value);
        BackgroundEntry nebula = library.ById("nebula")!;
        BackgroundEntry meadow = library.ById("meadow_town")!;
        BackgroundEntry night = library.ById("night_town")!;

        Assert.True(BackgroundSelector.PairExcluded(nebula, meadow)); // space x daylight
        Assert.False(BackgroundSelector.PairExcluded(nebula, night));
        Assert.Superset(
            new HashSet<string?>(selector.SharedRemapCandidates(nebula, night)),
            new HashSet<string?> { "grey", "night" });
        Assert.True(BackgroundSelector.SceneDistance(nebula, night) >= 1.0);
    }

    // ── heredity: atomic crossover + whole-composite repair ────────────────────

    [Fact]
    public void CompositeGenesCrossAtomicallyAndRoundTripSerialization()
    {
        BackgroundSelector selector = NewSelector();
        // Find two seeds whose stages resolved composites.
        var parents = new List<GameGenome>();
        for (ulong seed = 1; parents.Count < 2 && seed < 200; seed++)
        {
            GameGenome g = GameGenome.Generate(FullConfig(), new Pcg32(seed));
            if (BackgroundComposite.IsComposite(g.Stage.BackgroundId))
            {
                parents.Add(g);
            }
        }
        Assert.Equal(2, parents.Count);

        // Serialization round-trip carries the composite verbatim.
        string json = GameGenomeJson.Serialize(new GameRecord("c", null, parents[0]));
        Assert.Contains("far:", json);
        Assert.Equal(parents[0].Stage.BackgroundId,
            GameGenomeJson.Deserialize(json).Genome.Stage.BackgroundId);

        // Selector-less crossover: the child's gene is EXACTLY one parent's whole
        // composite (the 50/50 coin never splices layers).
        for (ulong s = 1; s <= 30; s++)
        {
            GameGenome child = GameGenomeOps.Crossover(
                parents[0], parents[1], new Pcg32(s));
            Assert.Contains(child.Stage.BackgroundId,
                new[] { parents[0].Stage.BackgroundId, parents[1].Stage.BackgroundId });
        }

        // Repair re-resolves the WHOLE composite: a half-dead gene never keeps its
        // surviving layers.
        BackgroundComposite parsed = BackgroundComposite.TryParse(parents[0].Stage.BackgroundId)!;
        StageGenome halfDead = parents[0].Stage.WithBackgroundId(
            new BackgroundComposite(
                parsed.FarId, "deleted_mid", parsed.NearId, parsed.Remap).ToGene());
        Assert.True(selector.NeedsRepair(halfDead));
        StageGenome repaired = selector.EnsureGene(halfDead);
        BackgroundSpec repairedSpec = selector.ParseGene(repaired.BackgroundId)!;
        Assert.NotNull(repairedSpec);
        Assert.DoesNotContain("deleted_mid", repaired.BackgroundId);
        // Stable: repairing again changes nothing.
        Assert.Same(repaired, selector.EnsureGene(repaired));

        // A pre-v0.4 three-part gene repairs into the full three-layer stack.
        StageGenome legacy = parents[0].Stage.WithBackgroundId(
            $"far:{parsed.FarId}|mid:{parsed.MidId}|remap:{parsed.Remap ?? "none"}");
        Assert.True(selector.NeedsRepair(legacy));
        StageGenome grown = selector.EnsureGene(legacy);
        Assert.True(BackgroundComposite.TryParse(grown.BackgroundId)!.HasNear);
        Assert.Same(grown, selector.EnsureGene(grown));
    }

    // ── the render layout ──────────────────────────────────────────────────────

    [Fact]
    public void LayoutIsDeterministicInsideTheBandsAndSpread()
    {
        BackgroundSelector selector = NewSelector();
        BackgroundSelectionConfig config = TuningLazy.Value;
        int nears = 0, composites = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            StageGenome stage = Stage(seed);
            ulong s = BackgroundSelector.BackgroundSeed(stage);
            BackgroundLayout layout = selector.Layout(stage, s)!;
            Assert.Equal(layout, selector.Layout(stage, s));
            Assert.InRange(layout.FarFactor, config.FarFactorMin, config.FarFactorMax);
            if (layout.Mid is null)
            {
                continue;
            }
            composites++;
            // Remediation §2 bands + the minimum adjacent spread (under 0.25 apart
            // two layers read as one plane).
            Assert.InRange(layout.MidFactor, config.MidFactorMin, config.MidFactorMax);
            Assert.True(layout.MidFactor - layout.FarFactor >= config.LayerSpreadMin - 1e-4f,
                "far and mid read as one plane");
            if (layout.Accent is { } near)
            {
                nears++;
                Assert.Equal("element", near.Element.LayerRole);
                Assert.InRange(near.Anchor, 0, 2);
                Assert.InRange(near.Factor, config.NearFactorMin, config.NearFactorMax);
                Assert.True(near.Factor - layout.MidFactor >= config.LayerSpreadMin - 1e-4f,
                    "mid and near read as one plane");
                Assert.InRange(near.Scale, config.NearScaleMin, config.NearScaleMax);
            }
        }
        Assert.True(composites > 250, $"only {composites} composite layouts sampled");
        Assert.Equal(composites, nears); // three layers minimum over the real corpus
    }

    [Fact]
    public void ArchivedSingleGenesKeepThePhaseOneLayoutShape()
    {
        // A plain single-id gene (archived pre-v0.4 content, and the extreme
        // fallback) keeps the Phase-1 layout shape: no mid, no seam, no near, the
        // variant's crop on the single entry.
        BackgroundSelector selector = NewSelector();
        BackgroundEntry full = LibraryLazy.Value.Entries.First(e => e.LayerRole == "full");
        StageGenome stage = GameGenome.Generate(
            GenerationConfig.Default, new Pcg32(11)).Stage.WithBackgroundId(full.Id);
        ulong s = BackgroundSelector.BackgroundSeed(stage);
        BackgroundLayout layout = selector.Layout(stage, s)!;
        Assert.NotNull(layout.Single);
        Assert.Null(layout.Mid);
        Assert.Equal(0f, layout.SeamHaze);
        Assert.Null(layout.Accent);
        Assert.Equal(selector.Variant(layout.Single!, stage, s), layout.Variant);
    }
}
