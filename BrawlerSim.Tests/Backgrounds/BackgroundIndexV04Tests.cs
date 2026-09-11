using BrawlerSim.Backgrounds;
using Xunit;

namespace BrawlerSim.Tests.Backgrounds;

/// <summary>
/// The v0.4 index contract (backgrounds-handoff-v04-core, 2026-09-11): the coverage
/// fields (canTileX/mirrorTileX/extendTop/extendBottom) and detail-floor fields
/// (metrics.detail/edgeDensity/dithered, boxRisk) parse from the REAL shipped index,
/// and the corpus-wide invariants the remediation doc promises actually hold.
/// </summary>
public class BackgroundIndexV04Tests
{
    private static readonly Lazy<BackgroundLibrary> LibraryLazy = new(() =>
        BackgroundLibrary.LoadFile(IndexPath()));

    private static BackgroundLibrary Library => LibraryLazy.Value;

    private static string IndexPath()
    {
        string relative = Path.Combine(
            "godot", "assets", "backgrounds_v1", "backgrounds_v1_index.json");
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
    public void CorpusParsesCompletely()
    {
        Assert.Equal("0.4-fixture", Library.Version);
        Assert.Equal(1190, Library.Entries.Count);
        Assert.Empty(Library.Refused);
    }

    [Fact]
    public void CoverageFieldsHoldTheContract()
    {
        foreach (BackgroundEntry e in Library.Entries)
        {
            // Mirror-wrap is the universal horizontal fallback: no entry may refuse both.
            Assert.True(e.CanTileX || e.MirrorTileX, e.Id);
            foreach (BgExtend extend in new[] { e.ExtendTop, e.ExtendBottom })
            {
                Assert.Contains(extend.Mode,
                    new[] { BgExtend.Solid, BgExtend.Smear, BgExtend.Transparent });
                if (extend.Mode == BgExtend.Solid)
                {
                    Assert.NotNull(extend.Color);
                    Assert.Equal(3, extend.Color!.Count);
                }
            }
        }
    }

    [Fact]
    public void DetailFieldsHoldTheContract()
    {
        int boxRisk = 0;
        foreach (BackgroundEntry e in Library.Entries)
        {
            Assert.InRange(e.Metrics.Detail, 0f, 1f);
            Assert.InRange(e.Metrics.EdgeDensity, 0f, 1f);
            if (e.BoxRisk)
            {
                boxRisk++;
            }
        }
        // The remediation defines boxRisk as detail < 0.12; the corpus must leave a
        // comfortable non-boxRisk majority for the pairing rule to draw from.
        Assert.InRange(boxRisk, 1, Library.Entries.Count / 2);
        Assert.Contains(Library.Entries, e => e.Metrics.Dithered);
    }

    /// <summary>Eight v0.4 entries carry layerRole "element" with files stored under
    /// mid/ — the index's `file` path is authoritative and the loader must never
    /// reconstruct paths from the role. Pin that the shipped corpus resolves.</summary>
    [Fact]
    public void EveryIndexedFileExistsExactlyWhereTheIndexSaysItIs()
    {
        string root = Path.GetDirectoryName(IndexPath())!;
        var crossFiled = new List<string>();
        foreach (BackgroundEntry e in Library.Entries)
        {
            Assert.True(File.Exists(Path.Combine(root, e.File)), e.File);
            if (!e.File.StartsWith(e.LayerRole + "/", StringComparison.Ordinal))
            {
                crossFiled.Add(e.Id);
            }
        }
        Assert.Equal(8, crossFiled.Count); // the known mid/-stored elements
    }
}
