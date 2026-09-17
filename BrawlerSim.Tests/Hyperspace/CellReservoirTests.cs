using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;

namespace BrawlerSim.Tests.Hyperspace;

/// <summary>
/// The per-cell reservoir behind the galaxy view's planets (2026-09-16,
/// docs/features/galaxy-view.md): a cell keeps its elite plus members SPREAD across
/// the cell, so a converged run does not orbit every star with copies of itself.
/// </summary>
public class CellReservoirTests
{
    private static DescriptorBins SingleCellBins()
    {
        // Every interior edge at 0, so every descriptor in [0,1) lands in one cell —
        // exactly the converged-cell case the reservoir exists for.
        var edges = new List<IReadOnlyList<float>>();
        for (int axis = 0; axis < Descriptors.Count; axis++)
        {
            edges.Add(Enumerable.Repeat(1000f, DescriptorBins.BinsPerAxis - 1).ToArray());
        }
        var zeros = Enumerable.Repeat(0f, Descriptors.Count).ToArray();
        var ones = Enumerable.Repeat(1f, Descriptors.Count).ToArray();
        return new DescriptorBins(edges, zeros, ones, 1UL, 1);
    }

    private static GameGenome Genome(ulong seed) =>
        GameGenome.Generate(GenerationConfig.Default, new Pcg32(seed));

    private static float[] Descriptor() => new[] { 0.5f, 0.5f, 0.5f, 0.5f };

    [Fact]
    public void DefaultArchiveRetainsNoMembers()
    {
        // The cube tab's archive is untouched by the galaxy work: capacity 0 means
        // elites only, exactly as before.
        var archive = new MapElitesArchive(SingleCellBins());
        for (int i = 0; i < 20; i++)
        {
            archive.Offer(new ArchiveEntry(Genome((ulong)i + 1), i, Descriptor(), i), out _);
        }
        Assert.Equal(1, archive.Count);
        Assert.Equal(0, archive.RetainedMembers);
        Assert.Empty(archive.MembersOf(archive.Cells.First().Key));
    }

    [Fact]
    public void EliteIsTheFittestAndMembersFillTheRest()
    {
        var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 4);
        for (int i = 0; i < 12; i++)
        {
            archive.Offer(new ArchiveEntry(Genome((ulong)i + 1), i, Descriptor(), i), out _);
        }
        int cell = archive.Cells.First().Key;
        Assert.True(archive.TryGet(cell, out ArchiveEntry elite));
        Assert.Equal(11f, elite.Fitness);                  // the best offered
        Assert.Equal(4, archive.MembersOf(cell).Count);    // capacity respected
        Assert.Equal(4, archive.RetainedMembers);
        // A member is never the elite itself.
        Assert.DoesNotContain(archive.MembersOf(cell), m => ReferenceEquals(m, elite));
    }

    [Fact]
    public void MembersSpreadAcrossTheCellRatherThanClusteringOnTheElite()
    {
        // A converged cell: many near-clones of one lineage, plus a handful of
        // genuinely different games. Picking runners-up by FITNESS would orbit the
        // star with clones; the reservoir must keep the different ones.
        var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 3);
        GameGenome founder = Genome(1);

        // The elite, then 15 near-clones of it at descending fitness...
        archive.Offer(new ArchiveEntry(founder, 100f, Descriptor(), 0), out _);
        for (int i = 0; i < 15; i++)
        {
            archive.Offer(new ArchiveEntry(founder, 99f - i, Descriptor(), i + 1), out _);
        }
        // ...then three unrelated genomes, all WORSE than every clone.
        for (int i = 0; i < 3; i++)
        {
            archive.Offer(new ArchiveEntry(Genome((ulong)(50 + i)), 1f + i, Descriptor(), 100 + i), out _);
        }

        int cell = archive.Cells.First().Key;
        IReadOnlyList<ArchiveEntry> members = archive.MembersOf(cell);
        Assert.Equal(3, members.Count);
        float nearestToElite = members.Min(m => GenomeDistance.Normalized(m.Genome, founder));
        Assert.True(nearestToElite > 0f,
            "every planet was a clone of its star — the spread rule did nothing");
        // The low-fitness outsiders won their slots on distance, not score.
        Assert.True(members.Count(m => m.Fitness < 10f) >= 2,
            "fitness ranking beat the spread rule");
    }

    [Fact]
    public void MembersAreDeterministicForAGivenOfferSequence()
    {
        // Reproducibility is per offer SEQUENCE (both call sites feed one: the
        // shadow archive in generation order, QUALITY EXPLORATION in scan order).
        static int[] Run()
        {
            var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 5);
            for (int i = 0; i < 40; i++)
            {
                archive.Offer(new ArchiveEntry(Genome((ulong)i + 1), (i * 37) % 23, Descriptor(), i), out _);
            }
            return archive.MembersOf(archive.Cells.First().Key).Select(m => m.Candidate).ToArray();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void DisplacedEliteFallsBackIntoTheMemberPool()
    {
        var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 2);
        archive.Offer(new ArchiveEntry(Genome(1), 10f, Descriptor(), 0), out _);
        archive.Offer(new ArchiveEntry(Genome(2), 20f, Descriptor(), 1), out bool replaced);

        Assert.True(replaced);
        int cell = archive.Cells.First().Key;
        Assert.True(archive.TryGet(cell, out ArchiveEntry elite));
        Assert.Equal(20f, elite.Fitness);
        // The old elite is the best thing this cell had — it becomes a planet, not litter.
        Assert.Contains(archive.MembersOf(cell), m => m.Candidate == 0);
    }

    [Fact]
    public void TheMemberBudgetCapsArchiveWideRetention()
    {
        // The safety valve: memory, not geometry, bounds the planet count
        // (~4.4 KB per retained genome).
        var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 8, maxRetainedMembers: 3);
        for (int i = 0; i < 30; i++)
        {
            archive.Offer(new ArchiveEntry(Genome((ulong)i + 1), i % 7, Descriptor(), i), out _);
        }
        Assert.True(archive.RetainedMembers <= 3,
            $"retained {archive.RetainedMembers} members past the budget");
    }

    [Fact]
    public void ArchiveStatisticsStillReadTheEliteOnly()
    {
        // Coverage/QD/Best describe the ELITES; planets must not inflate them.
        var archive = new MapElitesArchive(SingleCellBins(), memberCapacity: 6);
        for (int i = 0; i < 10; i++)
        {
            archive.Offer(new ArchiveEntry(Genome((ulong)i + 1), i, Descriptor(), i), out _);
        }
        Assert.Equal(1, archive.Count);
        Assert.Equal(9f, archive.Best!.Fitness);
        Assert.Equal(9d, archive.QdScore, 3);
        Assert.Single(archive.ElitesSnapshot());
    }
}
