using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

public class DescriptorBinsTests
{
    /// <summary>Synthetic bins with integer edges 1..7 on every axis and pilot range
    /// [0, 8] — hand-checkable BinOf/CellIndex arithmetic.</summary>
    private static DescriptorBins IntegerBins()
    {
        float[] edges = { 1f, 2f, 3f, 4f, 5f, 6f, 7f };
        return new DescriptorBins(
            new[] { edges, edges, edges, edges },
            new[] { 0f, 0f, 0f, 0f }, new[] { 8f, 8f, 8f, 8f },
            pilotSeed: 0, pilotSamples: 8);
    }

    [Fact]
    public void BinOfCountsEdgesAtOrBelowTheValue()
    {
        DescriptorBins bins = IntegerBins();
        Assert.Equal(0, bins.BinOf(0, 0.5f));
        Assert.Equal(1, bins.BinOf(0, 1f)); // edges are inclusive lower bounds
        Assert.Equal(3, bins.BinOf(0, 3.9f));
        Assert.Equal(7, bins.BinOf(0, 7.5f));
        Assert.Equal(7, bins.BinOf(0, 100f)); // clamps into the top bin
        Assert.Equal(0, bins.BinOf(0, -5f));  // clamps into the bottom bin
    }

    [Fact]
    public void CellIndexAndCoordinatesRoundTrip()
    {
        DescriptorBins bins = IntegerBins();
        float[] descriptor = { 0.5f, 3.5f, 7.5f, 1f }; // bins 0, 3, 7, 1
        int cell = bins.CellIndex(descriptor);
        Assert.Equal(((0 * 8 + 3) * 8 + 7) * 8 + 1, cell);
        Assert.Equal(new[] { 0, 3, 7, 1 }, DescriptorBins.CellCoordinates(cell));
    }

    [Fact]
    public void OutsidePilotRangeIsDetectedPerAxis()
    {
        DescriptorBins bins = IntegerBins();
        Assert.False(bins.IsOutsidePilotRange(new[] { 0f, 4f, 8f, 2f }));
        Assert.True(bins.IsOutsidePilotRange(new[] { -0.1f, 4f, 8f, 2f }));
        Assert.True(bins.IsOutsidePilotRange(new[] { 0f, 4f, 8.1f, 2f }));
    }

    [Fact]
    public void PilotIsDeterministic()
    {
        DescriptorBins a = DescriptorBins.FromPilot(GenerationConfig.Default, pilotSeed: 5, samples: 400);
        DescriptorBins b = DescriptorBins.FromPilot(GenerationConfig.Default, pilotSeed: 5, samples: 400);
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            Assert.Equal(a.Edges[axis], b.Edges[axis]);
            Assert.Equal(a.PilotMin[axis], b.PilotMin[axis]);
            Assert.Equal(a.PilotMax[axis], b.PilotMax[axis]);
        }
    }

    [Fact]
    public void PilotEdgesAreEqualFrequencyOctiles()
    {
        // Re-binning the pilot's own configuration puts roughly 1/8 of a fresh sample
        // in each bin on every axis (ties and sampling noise allowed for).
        DescriptorBins bins = DescriptorBins.FromPilot(GenerationConfig.Default, pilotSeed: 5, samples: 1200);
        var rng = new BrawlerSim.Determinism.Pcg32(999);
        var counts = new int[Descriptors.Count, DescriptorBins.BinsPerAxis];
        const int samples = 400;
        for (int i = 0; i < samples; i++)
        {
            float[] descriptor = Descriptors.Compute(GameGenome.Generate(GenerationConfig.Default, rng));
            for (int axis = 0; axis < Descriptors.Count; axis++)
            {
                counts[axis, bins.BinOf(axis, descriptor[axis])]++;
            }
        }
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            for (int bin = 0; bin < DescriptorBins.BinsPerAxis; bin++)
            {
                // Expected 50 per bin; generous tolerance for sampling noise.
                Assert.InRange(counts[axis, bin], 18, 100);
            }
        }
    }

    [Fact]
    public void BinsRoundTripThroughJson()
    {
        DescriptorBins bins = DescriptorBins.FromPilot(GenerationConfig.Default, pilotSeed: 12, samples: 200);
        string path = Path.Combine(Path.GetTempPath(), $"bins-{Guid.NewGuid():N}.json");
        try
        {
            bins.Save(path);
            DescriptorBins loaded = DescriptorBins.Load(path);
            for (int axis = 0; axis < Descriptors.Count; axis++)
            {
                Assert.Equal(bins.Edges[axis], loaded.Edges[axis]);
            }
            Assert.Equal(bins.PilotMin, loaded.PilotMin);
            Assert.Equal(bins.PilotMax, loaded.PilotMax);
            Assert.Equal(bins.PilotSeed, loaded.PilotSeed);
            Assert.Equal(bins.PilotSamples, loaded.PilotSamples);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ArchiveInsertionIsStandardMapElites()
    {
        var archive = new MapElitesArchive(IntegerBins());
        var rng = new BrawlerSim.Determinism.Pcg32(3);
        GameGenome genome = GameGenome.Generate(GenerationConfig.Default, rng);
        float[] descriptor = { 0.5f, 0.5f, 0.5f, 0.5f };

        int cell = archive.Offer(new ArchiveEntry(genome, 10f, descriptor, 0), out bool replaced);
        Assert.True(cell >= 0);
        Assert.False(replaced);
        Assert.Equal(1, archive.Count);

        // Equal fitness is rejected — insertion requires STRICTLY greater.
        Assert.Equal(-1, archive.Offer(new ArchiveEntry(genome, 10f, descriptor, 1), out _));

        Assert.Equal(cell, archive.Offer(new ArchiveEntry(genome, 11f, descriptor, 2), out replaced));
        Assert.True(replaced);
        Assert.Equal(1, archive.Count);
        Assert.True(archive.TryGet(cell, out ArchiveEntry entry));
        Assert.Equal(11f, entry.Fitness);
        Assert.Equal(2, entry.Candidate);
    }

    [Fact]
    public void QdScoreFloorsNegativeFitnessToZero()
    {
        var archive = new MapElitesArchive(IntegerBins());
        var rng = new BrawlerSim.Determinism.Pcg32(3);
        GameGenome genome = GameGenome.Generate(GenerationConfig.Default, rng);
        archive.Offer(new ArchiveEntry(genome, -50f, new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 0), out _);
        archive.Offer(new ArchiveEntry(genome, 30f, new[] { 7.5f, 0.5f, 0.5f, 0.5f }, 1), out _);
        Assert.Equal(30.0, archive.QdScore);
        Assert.Equal(2, archive.Count);
        Assert.Equal(30f, archive.Best!.Fitness);
    }

    [Fact]
    public void ArchiveCellsIterateInAscendingCellOrder()
    {
        var archive = new MapElitesArchive(IntegerBins());
        var rng = new BrawlerSim.Determinism.Pcg32(3);
        GameGenome genome = GameGenome.Generate(GenerationConfig.Default, rng);
        archive.Offer(new ArchiveEntry(genome, 1f, new[] { 7.5f, 7.5f, 7.5f, 7.5f }, 0), out _);
        archive.Offer(new ArchiveEntry(genome, 1f, new[] { 0.5f, 0.5f, 0.5f, 0.5f }, 1), out _);
        archive.Offer(new ArchiveEntry(genome, 1f, new[] { 3.5f, 0.5f, 0.5f, 0.5f }, 2), out _);
        int previous = -1;
        foreach (KeyValuePair<int, ArchiveEntry> kv in archive.Cells)
        {
            Assert.True(kv.Key > previous, "archive iteration is not ascending by cell");
            previous = kv.Key;
        }
        Assert.Equal(3, archive.Count);
    }

    [Fact]
    public void OutOfPilotRangeValuesAreCountedButStillBinned()
    {
        var archive = new MapElitesArchive(IntegerBins());
        var rng = new BrawlerSim.Determinism.Pcg32(3);
        GameGenome genome = GameGenome.Generate(GenerationConfig.Default, rng);
        int cell = archive.Offer(new ArchiveEntry(genome, 1f, new[] { 9f, 0.5f, 0.5f, 0.5f }, 0), out _);
        Assert.True(cell >= 0); // clamped into the top bin, not rejected
        Assert.Equal(1, archive.OutOfPilotRangeCount);
        Assert.Equal(7, DescriptorBins.CellCoordinates(cell)[0]);
    }
}
