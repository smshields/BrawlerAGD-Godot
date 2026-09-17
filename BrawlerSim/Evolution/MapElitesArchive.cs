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
    private readonly SortedDictionary<int, CellReservoir> _cells = new();

    /// <summary>Default member capacity for the galaxy view (2026-09-16): how many
    /// NON-elite occupants a cell retains as orbiting planets. Memory, not geometry,
    /// sets this — the system envelope is capped independently of the planet count
    /// (docs/features/galaxy-view.md §Geometry), but a retained genome is ~4.4 KB and
    /// a paper-scale run offers ~30,000 of them.</summary>
    public const int DefaultMemberCapacity = 8;

    /// <summary>Safety valve: once this many members are retained archive-wide, cells
    /// stop opening NEW member slots (a full cell still swaps for a better-spread
    /// occupant — that costs nothing). 20,000 x ~4.4 KB is ~88 MB.</summary>
    public const int DefaultMaxRetainedMembers = 20_000;

    public DescriptorBins Bins { get; }

    /// <summary>Per-cell member capacity; 0 (the default) is the pre-galaxy archive
    /// exactly — elites only, no reservoir, no extra retention.</summary>
    public int MemberCapacity { get; }

    public int MaxRetainedMembers { get; }

    /// <summary>Members retained archive-wide — the memory readout, and what the
    /// safety valve counts.</summary>
    public int RetainedMembers { get; private set; }

    /// <summary>Values outside the pilot's observed range so far — the re-pilot
    /// signal (§5: clamp into the end bins, count, never re-bin the current run).
    /// Internal setter: checkpoint restore only.</summary>
    public int OutOfPilotRangeCount { get; internal set; }

    public MapElitesArchive(DescriptorBins bins, int memberCapacity = 0,
        int maxRetainedMembers = DefaultMaxRetainedMembers)
    {
        Bins = bins;
        MemberCapacity = Math.Max(0, memberCapacity);
        MaxRetainedMembers = Math.Max(0, maxRetainedMembers);
    }

    public int Count => _cells.Count;

    public float Coverage => _cells.Count / (float)DescriptorBins.CellCount;

    /// <summary>Ascending cell index — THE deterministic elite ordering.</summary>
    public IEnumerable<KeyValuePair<int, ArchiveEntry>> Cells
    {
        get
        {
            foreach (KeyValuePair<int, CellReservoir> kv in _cells)
            {
                yield return new KeyValuePair<int, ArchiveEntry>(kv.Key, kv.Value.Elite);
            }
        }
    }

    /// <summary>Ascending cell index over the full occupancy — elite plus retained
    /// members. The galaxy view reads this one (star plus its planets).</summary>
    public IEnumerable<KeyValuePair<int, CellReservoir>> Reservoirs => _cells;

    public bool TryGet(int cell, out ArchiveEntry entry)
    {
        if (_cells.TryGetValue(cell, out CellReservoir? reservoir))
        {
            entry = reservoir.Elite;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>The cell's non-elite occupants, spread across the cell by
    /// CellReservoir; empty when the archive retains no members.</summary>
    public IReadOnlyList<ArchiveEntry> MembersOf(int cell) =>
        _cells.TryGetValue(cell, out CellReservoir? reservoir)
            ? reservoir.Members
            : Array.Empty<ArchiveEntry>();

    /// <summary>QD-score: sum of elite fitness floored to 0 (so a filled cell never
    /// subtracts from the archive's quality-diversity total).</summary>
    public double QdScore
    {
        get
        {
            double sum = 0;
            foreach (CellReservoir reservoir in _cells.Values)
            {
                sum += Math.Max(0f, reservoir.Elite.Fitness);
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
            foreach (CellReservoir reservoir in _cells.Values)
            {
                if (best is null || reservoir.Elite.Fitness > best.Fitness)
                {
                    best = reservoir.Elite;
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
        if (_cells.TryGetValue(cell, out CellReservoir? reservoir))
        {
            bool takesElite = entry.Fitness > reservoir.Elite.Fitness;
            int before = reservoir.Members.Count;
            // Past the archive-wide member budget a full pool may still SWAP for a
            // better-spread occupant (no new memory), but no cell opens a new slot —
            // including the slot a displaced elite would otherwise fall into.
            reservoir.Offer(entry, allowNewMembers: RetainedMembers < MaxRetainedMembers);
            RetainedMembers += reservoir.Members.Count - before;
            replaced = takesElite;
            return takesElite ? cell : -1;
        }
        _cells.Add(cell, new CellReservoir(entry, MemberCapacity));
        replaced = false;
        return cell;
    }

    /// <summary>Checkpoint restore: place an entry verbatim (no comparison, counter
    /// untouched). The loader owns consistency.</summary>
    public void Restore(int cell, ArchiveEntry entry)
    {
        if (_cells.TryGetValue(cell, out CellReservoir? reservoir))
        {
            RetainedMembers -= reservoir.Members.Count;
        }
        _cells[cell] = new CellReservoir(entry, MemberCapacity);
    }

    /// <summary>Elites as a dense array in ascending cell order — the deterministic
    /// parent pool for selection.</summary>
    public ArchiveEntry[] ElitesSnapshot()
    {
        var elites = new ArchiveEntry[_cells.Count];
        int i = 0;
        foreach (CellReservoir reservoir in _cells.Values)
        {
            elites[i++] = reservoir.Elite;
        }
        return elites;
    }
}
