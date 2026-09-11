using BrawlerSim.Genome;

namespace BrawlerSim.Evolution;

/// <summary>One archive elite: the genome, its (aggregated) fitness, the RAW
/// descriptor 4-vector it was binned by (kept so a COPY can be re-binned later —
/// re-binning a live archive is forbidden), and a caller-defined monotone counter
/// identifying which evaluation produced the fitness.</summary>
public sealed record ArchiveEntry(GameGenome Genome, float Fitness, float[] Descriptor, int Candidate);

/// <summary>
/// The MAP-Elites-style quality-diversity grid archive (2026-09-10,
/// docs/features/map-elites.md): one elite per cell, standard insertion (empty cell,
/// or strictly greater fitness). Since 2026-09-11 (designer: the standalone
/// MAP-Elites ALGORITHM was removed pending an algorithmic rethink) this is a pure
/// visualization container — the Evolve screen's shadow binning of GA runs and the
/// QUALITY EXPLORATION screen's saved-game archive. Iteration order is always
/// ascending cell index, so no dictionary-order iteration ever escapes.
/// </summary>
public sealed class MapElitesArchive
{
    private readonly SortedDictionary<int, ArchiveEntry> _cells = new();

    public DescriptorBins Bins { get; }

    /// <summary>Values outside the pilot's observed range so far — the re-pilot
    /// signal (§5: clamp into the end bins, count, never re-bin the current run).
    /// Internal setter: checkpoint restore only.</summary>
    public int OutOfPilotRangeCount { get; internal set; }

    public MapElitesArchive(DescriptorBins bins)
    {
        Bins = bins;
    }

    public int Count => _cells.Count;

    public float Coverage => _cells.Count / (float)DescriptorBins.CellCount;

    /// <summary>Ascending cell index — THE deterministic elite ordering.</summary>
    public IEnumerable<KeyValuePair<int, ArchiveEntry>> Cells => _cells;

    public bool TryGet(int cell, out ArchiveEntry entry) => _cells.TryGetValue(cell, out entry!);

    /// <summary>QD-score: sum of elite fitness floored to 0 (so a filled cell never
    /// subtracts from the archive's quality-diversity total).</summary>
    public double QdScore
    {
        get
        {
            double sum = 0;
            foreach (ArchiveEntry entry in _cells.Values)
            {
                sum += Math.Max(0f, entry.Fitness);
            }
            return sum;
        }
    }

    /// <summary>Best raw fitness in the archive; null when empty.</summary>
    public ArchiveEntry? Best
    {
        get
        {
            ArchiveEntry? best = null;
            foreach (ArchiveEntry entry in _cells.Values)
            {
                if (best is null || entry.Fitness > best.Fitness)
                {
                    best = entry;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Standard MAP-Elites insertion: the entry takes its cell when the cell is empty
    /// or the entry's fitness is STRICTLY greater than the incumbent's. Returns the
    /// cell index when accepted (replaced tells which case), −1 when rejected.
    /// </summary>
    public int Offer(ArchiveEntry entry, out bool replaced)
    {
        if (Bins.IsOutsidePilotRange(entry.Descriptor))
        {
            OutOfPilotRangeCount++;
        }
        int cell = Bins.CellIndex(entry.Descriptor);
        if (_cells.TryGetValue(cell, out ArchiveEntry? incumbent))
        {
            if (entry.Fitness <= incumbent.Fitness)
            {
                replaced = false;
                return -1;
            }
            _cells[cell] = entry;
            replaced = true;
            return cell;
        }
        _cells.Add(cell, entry);
        replaced = false;
        return cell;
    }

    /// <summary>Checkpoint restore: place an entry verbatim (no comparison, counter
    /// untouched). The loader owns consistency.</summary>
    public void Restore(int cell, ArchiveEntry entry)
    {
        _cells[cell] = entry;
    }

    /// <summary>Elites as a dense array in ascending cell order — the deterministic
    /// parent pool for selection.</summary>
    public ArchiveEntry[] ElitesSnapshot()
    {
        var elites = new ArchiveEntry[_cells.Count];
        int i = 0;
        foreach (ArchiveEntry entry in _cells.Values)
        {
            elites[i++] = entry;
        }
        return elites;
    }
}
