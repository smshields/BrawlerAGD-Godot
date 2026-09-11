using BrawlerSim.Backgrounds;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sprites;
using Xunit;

namespace BrawlerSim.Tests.Backgrounds;

/// <summary>
/// The designer fix round of 2026-09-03: no source pack may dominate picks (the
/// per-entry cap could not police family share — one city pack contributes half the
/// full-scene corpus), zero-remap entries are pairable through the widened
/// unified-remap rule, and the layer-fit rule always covers the kill box within the
/// density cap.
/// </summary>
public class BackgroundDistributionTests
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

    private static string Repo(string file) =>
        FindRepoFile(Path.Combine("godot", "assets", "backgrounds_v1", file));

    private static BackgroundSelector NewSelector() => new(
        LibraryLazy.Value, PaletteLazy.Value, TuningLazy.Value,
        StageThemeLibrary.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "tiles_v2_slices.json"))));

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

    [Fact]
    public void CapGroupShareBoundsEveryGroupAndPreservesTotalMass()
    {
        // One group holding 80% of the mass gets pushed to the cap; the freed mass
        // lifts the others; totals stay put (relative sampling is unchanged).
        var probs = new double[] { 0.5, 0.3, 0.1, 0.06, 0.04 };
        var groups = new[] { 0, 0, 1, 2, 3 };
        SelectionMath.CapGroupShare(probs, groups, 0.4f);
        Assert.Equal(1.0, probs.Sum(), 6);
        Assert.True(probs[0] + probs[1] <= 0.4 + 1e-6, "group 0 above the cap");
        // Infeasible caps (cap x groups <= 1) leave the pool untouched.
        var tiny = new double[] { 0.7, 0.3 };
        var tinyGroups = new[] { 0, 1 };
        SelectionMath.CapGroupShare(tiny, tinyGroups, 0.5f);
        Assert.Equal(new[] { 0.7, 0.3 }, tiny);
    }

    [Fact]
    public void NoSourcePackDominatesSingleImagePicks()
    {
        BackgroundSelector selector = NewSelector();
        var bySource = new Dictionary<string, int>(StringComparer.Ordinal);
        const int n = 2000;
        for (ulong seed = 1; seed <= n; seed++)
        {
            StageGenome stage = GameGenome.Generate(
                GenerationConfig.Default, new Pcg32(seed)).Stage;
            BackgroundCandidate top = selector.SelectCandidates(
                stage, BackgroundSelector.BackgroundSeed(stage), null, out _)[0];
            bySource[top.Entry.Source] =
                bySource.TryGetValue(top.Entry.Source, out int c) ? c + 1 : 1;
        }
        // The cap is 0.25 per pool; register pooling and small-pool fallbacks add
        // slack, so assert a loose ceiling that the pre-fix ~50% clearly violates.
        (string source, int picks) = bySource.MaxBy(kv => kv.Value) is var kv2
            ? (kv2.Key, kv2.Value) : ("", 0);
        Assert.True(picks <= n * 0.33, $"source monopoly: {source} took {picks}/{n}");
    }

    [Fact]
    public void MidPoolsSpreadAcrossSourcesAndReachZeroRemapEntries()
    {
        BackgroundSelector selector = NewSelector();
        var midPicks = new Dictionary<string, int>(StringComparer.Ordinal);
        var midBySource = new Dictionary<string, int>(StringComparer.Ordinal);
        int composites = 0, zeroRemapMids = 0;
        for (ulong seed = 1; seed <= 3000 && composites < 700; seed++)
        {
            StageGenome stage = GameGenome.Generate(
                GenerationConfig.Default, new Pcg32(seed)).Stage;
            BackgroundSpec spec = selector.ResolveSpec(
                stage, BackgroundSelector.BackgroundSeed(stage), null);
            if (!spec.IsComposite)
            {
                continue;
            }
            composites++;
            midPicks[spec.Mid!.Id] = midPicks.TryGetValue(spec.Mid!.Id, out int c) ? c + 1 : 1;
            midBySource[spec.Mid!.Source] =
                midBySource.TryGetValue(spec.Mid!.Source, out int s) ? s + 1 : 1;
            if (spec.Mid!.Remaps.Count == 0)
            {
                zeroRemapMids++;
            }
        }
        Assert.True(composites >= 500, $"only {composites} composites sampled");
        // Family share bounded; single mids bounded; the widened predicate (b)
        // reaches entries the old both-lists-intersect rule locked out entirely.
        (string src, int picks) = midBySource.MaxBy(kv => kv.Value) is var kv2
            ? (kv2.Key, kv2.Value) : ("", 0);
        Assert.True(picks <= composites * 0.40, $"mid family monopoly: {src} {picks}/{composites}");
        Assert.True(midPicks.Values.Max() <= composites * 0.10,
            $"mid entry monopoly: {midPicks.MaxBy(p => p.Value)}");
        Assert.True(midPicks.Count >= 30, $"only {midPicks.Count} distinct mids picked");
        Assert.True(zeroRemapMids > 0,
            "zero-remap mids never picked — the widened unified-remap rule is dead");
    }

    /// <summary>Asserts one plan covers its required strip of the kill box: the base
    /// row reaches full width (wrapped or wide enough), and every vertical gap is
    /// closed by an OPAQUE extension piece. The magenta debug clear behind the stack
    /// must be unreachable (remediation §1).</summary>
    private static void AssertCovers(CoveragePlan plan, float boxW, float boxH,
        float requiredTop, string what)
    {
        if (plan.TileBothAxes)
        {
            return; // both-axes repetition covers by construction
        }
        Assert.True(plan.Wrapped || plan.BaseW >= boxW - 0.5f, $"{what}: width gap");
        if (plan.BaseY > requiredTop + 0.5f)
        {
            Assert.NotNull(plan.Top);
            Assert.NotEqual(BgExtend.Transparent, plan.Top!.Extend.Mode);
            Assert.True(plan.Top.Y <= requiredTop + 0.5f
                && plan.Top.Y + plan.Top.Height >= plan.BaseY - 0.5f, $"{what}: top gap");
        }
        float bottomEdge = plan.BaseY + plan.BaseH;
        if (bottomEdge < boxH - 0.5f)
        {
            Assert.NotNull(plan.Bottom);
            Assert.NotEqual(BgExtend.Transparent, plan.Bottom!.Extend.Mode);
            Assert.True(plan.Bottom.Y <= bottomEdge + 0.5f
                && plan.Bottom.Y + plan.Bottom.Height >= boxH - 0.5f, $"{what}: bottom gap");
        }
    }

    /// <summary>The remediation §1 property test: over 500 seeded stages, at arena
    /// sizes up to (4x width, 3x height) of the base viewport, the coverage plans
    /// leave zero clear-color pixels. Zoom needs no axis of its own — the camera is
    /// hard-clamped inside the kill box at every zoom, and parallax factors in
    /// [0, 1] can only shrink a layer's visible window, so covering the box IS
    /// covering every camera framing.</summary>
    [Fact]
    public void CoveragePlansMakeClearColorUnreachable()
    {
        BackgroundSelector selector = NewSelector();
        BackgroundSelectionConfig config = TuningLazy.Value;
        var boxes = new (float W, float H)[]
        {
            (1280f, 720f),          // base viewport
            (5120f, 720f),          // 4x wide
            (1280f, 2160f),         // 3x tall
            (5120f, 2160f),         // 4x x 3x
        };
        int plansChecked = 0;
        for (ulong seed = 1; seed <= 500; seed++)
        {
            StageGenome stage = GameGenome.Generate(
                GenerationConfig.Default, new Pcg32(seed)).Stage;
            ulong s = BackgroundSelector.BackgroundSeed(stage);
            BackgroundSpec spec = selector.ResolveSpec(stage, s, null);
            BackgroundVariant variant = selector.Variant(
                spec.Single ?? spec.Far!, stage, s);
            foreach ((float boxW, float boxH) in boxes)
            {
                CoveragePlan far = BackgroundCoverage.Far(
                    spec.Single ?? spec.Far!, variant.Crop, boxW, boxH, config.LayerMaxScale);
                Assert.True(far.Scale <= config.LayerMaxScale + 1e-4f);
                AssertCovers(far, boxW, boxH, requiredTop: 0f, $"far {spec.Gene()}");
                plansChecked++;
                if (spec.Mid is { } mid)
                {
                    // Floor lines from the top edge to the bottom edge — every
                    // anchor must extend down to the box bottom.
                    foreach (float floorY in new[] { 0f, boxH * 0.5f, boxH * 0.8f, boxH })
                    {
                        CoveragePlan midPlan = BackgroundCoverage.Mid(
                            mid, boxW, boxH, floorY, config.LayerMaxScale);
                        Assert.True(midPlan.Scale <= config.LayerMaxScale + 1e-4f);
                        // A mid only owes coverage from its own top edge down.
                        AssertCovers(midPlan, boxW, boxH,
                            requiredTop: Math.Max(0f, midPlan.BaseY), $"mid {mid.Id}");
                        plansChecked++;
                    }
                }
            }
        }
        Assert.True(plansChecked > 4000, $"only {plansChecked} plans checked");
    }
}
