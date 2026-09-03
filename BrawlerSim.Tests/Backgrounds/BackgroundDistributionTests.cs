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

    [Fact]
    public void LayerFitAlwaysCoversTheKillBoxWithinTheDensityCap()
    {
        const float maxScale = 8f;
        foreach ((int cropW, int cropH, float boxW, float boxH) in new[]
        {
            (480, 270, 1440f, 810f),      // typical map: single stretched copy
            (480, 246, 3600f, 3600f),     // huge map: must tile
            (65, 65, 2800f, 900f),        // tiny mid on a wide map: must tile
            (576, 324, 1280f, 720f),      // dense art on a small map
        })
        {
            LayerFit far = BackgroundLayerFit.Far(new BgRect(0, 0, cropW, cropH), boxH, maxScale);
            Assert.True(far.Scale <= maxScale + 1e-4f);
            if (!far.Tiled)
            {
                Assert.True(cropH * far.Scale >= boxH - 0.5f, "untiled far fails to cover");
            }
            LayerFit mid = BackgroundLayerFit.Mid(cropW, boxW, maxScale);
            Assert.True(mid.Scale <= maxScale + 1e-4f);
            if (!mid.Tiled)
            {
                Assert.True(cropW * mid.Scale >= boxW - 0.5f, "untiled mid fails to cover");
            }
            // Tiled fits cover by construction: repeats extend to any width.
        }
    }
}
