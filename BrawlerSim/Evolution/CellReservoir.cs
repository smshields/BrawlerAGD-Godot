namespace BrawlerSim.Evolution;

/// <summary>
/// One archive cell's occupants: the ELITE (highest fitness — the galaxy view's star)
/// plus up to <see cref="Capacity"/> MEMBERS chosen to spread across the cell (the
/// planets orbiting it). 2026-09-16, docs/features/galaxy-view.md.
///
/// Members are maintained by streaming farthest-point insertion over
/// <see cref="GenomeDistance"/>: a cell in a converged run collects thousands of
/// near-clones, and showing the runners-up by fitness alone would orbit a star with
/// copies of itself. Spreading them instead makes a system worth flying into — it
/// shows what ELSE lives in this behavioral niche, which is the quality-diversity
/// read the archive exists to give.
///
/// Determinism: the result is a pure function of the OFFER SEQUENCE, which is itself
/// deterministic at both call sites (the Evolve shadow archive offers in generation
/// order; QUALITY EXPLORATION offers in sorted scan order). Like
/// <see cref="MapElitesArchive.Offer"/>'s strict-greater elite rule, it is not
/// permutation-independent — a different arrival order can retain a different (never
/// a worse-spread) member set. No RNG, no wall clock, no dictionary-order iteration.
///
/// MEMORY is the binding constraint on <see cref="Capacity"/>, not geometry: a
/// retained genome measures ~4.4 KB (2026-09-16 probe, 2p and 4p alike), and a
/// paper-scale run offers ~30,000 of them. See MapElitesArchive.MemberCapacity.
/// </summary>
public sealed class CellReservoir
{
    private readonly List<ArchiveEntry> _members = new();

    /// <summary>Pairwise distances over [elite, members...] — index 0 is the elite.
    /// Cached because the pool is re-scored on every offer once it is full, and
    /// GenomeDistance walks every param of every move of every character.</summary>
    private float[,] _distance;

    public int Capacity { get; }

    public ArchiveEntry Elite { get; private set; }

    /// <summary>Members in retention order (the order they took their slots) — a
    /// stable, deterministic sequence the view maps to orbit indices.</summary>
    public IReadOnlyList<ArchiveEntry> Members => _members;

    public CellReservoir(ArchiveEntry elite, int capacity)
    {
        Elite = elite;
        Capacity = Math.Max(0, capacity);
        _distance = new float[Capacity + 1, Capacity + 1];
    }

    /// <summary>Total occupants (elite + members) — the star plus its planets.</summary>
    public int Count => 1 + _members.Count;

    /// <summary>
    /// Offer an entry to this cell. A strictly fitter entry takes the elite slot and
    /// the displaced elite falls back into the member pool (it is, by construction,
    /// the best thing this cell had). Otherwise the entry competes for a member slot.
    /// Returns true when the entry was retained in either role.
    /// </summary>
    /// <param name="allowNewMembers">False once the archive-wide member budget is
    /// spent: the pool may still SWAP an occupant for a better-spread one (that costs
    /// no memory), but it may not open a new slot. An elite takeover always happens —
    /// the elite is the cell, not an extra.</param>
    public bool Offer(ArchiveEntry entry, bool allowNewMembers = true)
    {
        if (entry.Fitness > Elite.Fitness)
        {
            ArchiveEntry displaced = Elite;
            Elite = entry;
            RescoreAll();
            AdmitMember(displaced, allowNewMembers);
            return true;
        }
        return AdmitMember(entry, allowNewMembers);
    }

    /// <summary>
    /// Farthest-point admission: below capacity the entry simply takes a slot. At
    /// capacity it replaces the most CROWDED member — the one sitting closest to
    /// another occupant — but only when doing so genuinely opens the pool up, i.e.
    /// the candidate's nearest neighbour is farther than the incumbent's. Ties keep
    /// the incumbent, so an offer sequence of identical genomes churns nothing.
    /// </summary>
    private bool AdmitMember(ArchiveEntry entry, bool allowNewMembers)
    {
        if (Capacity == 0)
        {
            return false;
        }
        if (_members.Count < Capacity)
        {
            if (!allowNewMembers)
            {
                return false;
            }
            _members.Add(entry);
            RescoreMember(_members.Count - 1);
            return true;
        }

        // Candidate's nearest occupant.
        float candidateNearest = GenomeDistance.Normalized(entry.Genome, Elite.Genome);
        for (int i = 0; i < _members.Count; i++)
        {
            candidateNearest = Math.Min(
                candidateNearest, GenomeDistance.Normalized(entry.Genome, _members[i].Genome));
        }

        // The most crowded incumbent (lowest nearest-occupant distance); first index
        // wins a tie, so the choice never depends on iteration luck.
        int crowded = -1;
        float crowdedNearest = float.MaxValue;
        for (int i = 0; i < _members.Count; i++)
        {
            float nearest = NearestFor(i + 1);
            if (nearest < crowdedNearest)
            {
                crowdedNearest = nearest;
                crowded = i;
            }
        }

        if (crowded < 0 || candidateNearest <= crowdedNearest)
        {
            return false;
        }
        _members[crowded] = entry;
        RescoreMember(crowded);
        return true;
    }

    /// <summary>Distance from occupant `slot` to its nearest OTHER occupant.</summary>
    private float NearestFor(int slot)
    {
        float nearest = float.MaxValue;
        int occupants = Count;
        for (int other = 0; other < occupants; other++)
        {
            if (other != slot)
            {
                nearest = Math.Min(nearest, _distance[slot, other]);
            }
        }
        return nearest;
    }

    private void RescoreMember(int member)
    {
        int slot = member + 1;
        for (int other = 0; other < Count; other++)
        {
            if (other == slot)
            {
                continue;
            }
            ArchiveEntry a = other == 0 ? Elite : _members[other - 1];
            float d = GenomeDistance.Normalized(_members[member].Genome, a.Genome);
            _distance[slot, other] = d;
            _distance[other, slot] = d;
        }
    }

    private void RescoreAll()
    {
        for (int i = 0; i < _members.Count; i++)
        {
            RescoreMember(i);
        }
    }
}
