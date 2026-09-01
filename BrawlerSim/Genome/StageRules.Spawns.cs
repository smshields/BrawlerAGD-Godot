using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>StageRules — the four-spawn repair machinery (2026-08-12,
/// four-player.md). Split from StageRules.cs 2026-09-01, pure text move.</summary>
public static partial class StageRules
{
    /// <summary>
    /// Repairs all FOUR spawn genes of a stage ParamSet against a platform layout
    /// (four since 2026-08-12, docs/features/four-player.md). Runs at the GENETIC-OPS
    /// layer (generation, crossover, the mutation transform) — NEVER at SimWorld
    /// construction: pre-v7 artifacts store spawns exactly as the old sim derived them
    /// (including ones the old sim let fall to their death on crossover-broken
    /// asymmetric layouts), and replaying those bit-identically means the sim must
    /// consume genes untouched save for the legacy upward nudge. Spawns repair in
    /// index order, each treating the earlier ones as occupied (designer rule:
    /// spawn points must not overlap each other).
    /// </summary>
    public static ParamSet RepairSpawns(IReadOnlyList<PlatformGene> platforms, ParamSet stageParams)
    {
        float visW = stageParams.Get(StageParams.VisibleHalfWidth);
        float visH = stageParams.Get(StageParams.VisibleHalfHeight);
        Vec2 s1 = RepairSpawn(
            new Vec2(stageParams.Get(StageParams.Spawn1X), stageParams.Get(StageParams.Spawn1Y)),
            platforms, visW, visH);
        Vec2 s2 = RepairSpawn(
            new Vec2(stageParams.Get(StageParams.Spawn2X), stageParams.Get(StageParams.Spawn2Y)),
            platforms, visW, visH, new[] { s1 });
        Vec2 s3 = RepairSpawn(
            new Vec2(stageParams.Get(StageParams.Spawn3X), stageParams.Get(StageParams.Spawn3Y)),
            platforms, visW, visH, new[] { s1, s2 });
        Vec2 s4 = RepairSpawn(
            new Vec2(stageParams.Get(StageParams.Spawn4X), stageParams.Get(StageParams.Spawn4Y)),
            platforms, visW, visH, new[] { s1, s2, s3 });
        return stageParams.With(
            (StageParams.Spawn1X, s1.X), (StageParams.Spawn1Y, s1.Y),
            (StageParams.Spawn2X, s2.X), (StageParams.Spawn2Y, s2.Y),
            (StageParams.Spawn3X, s3.X), (StageParams.Spawn3Y, s3.Y),
            (StageParams.Spawn4X, s4.X), (StageParams.Spawn4Y, s4.Y));
    }

    /// <summary>The stored spawn point for player <paramref name="index"/> (0-based).
    /// Spawns 1/2 predate Map Size; 3/4 exist on every stage since 2026-08-12.</summary>
    public static Vec2 SpawnOf(ParamSet stageParams, int index) => index switch
    {
        0 => new Vec2(stageParams.Get(StageParams.Spawn1X), stageParams.Get(StageParams.Spawn1Y)),
        1 => new Vec2(stageParams.Get(StageParams.Spawn2X), stageParams.Get(StageParams.Spawn2Y)),
        2 => new Vec2(stageParams.Get(StageParams.Spawn3X), stageParams.Get(StageParams.Spawn3Y)),
        3 => new Vec2(stageParams.Get(StageParams.Spawn4X), stageParams.Get(StageParams.Spawn4Y)),
        _ => throw new ArgumentOutOfRangeException(nameof(index), "Stages carry four spawn points."),
    };

    /// <summary>Two spawn points "overlap" when the conservative spawn BODIES would
    /// intersect — the designer's non-overlap rule (2026-08-12) in body terms.</summary>
    public static bool SpawnsOverlap(Vec2 a, Vec2 b) =>
        MathF.Abs(a.X - b.X) < 2f * SpawnBodyHalfWidth
        && MathF.Abs(a.Y - b.Y) < 2f * SpawnBodyHalfHeight;

    private static bool OverlapsAnySpawn(Vec2 point, IReadOnlyList<Vec2>? occupied)
    {
        if (occupied is null)
        {
            return false;
        }
        foreach (Vec2 s in occupied)
        {
            if (SpawnsOverlap(point, s))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Makes a raw spawn gene legal for a layout (designer rules 2026-07-21: never in
    /// immediate KO bounds, always over a platform; 2026-08-12: clear of every
    /// already-placed spawn in <paramref name="occupied"/>). Deterministic, and the
    /// IDENTITY for already-legal spawns:
    /// 1. clamp inside the visible box (KO sits outside it) with edge clearance;
    /// 2. already over a platform, and the legacy inside-a-platform nudge stays inside
    ///    the box → keep the (nudged) position;
    /// 3. otherwise scan every platform for a FREE spawn column (airspace no other
    ///    platform occupies, so the nudge cannot fire) and take the legal spot nearest
    ///    the gene, ties to the earliest platform in list order;
    /// 4. degenerate layouts with no free column anywhere (a fully roofed wall)
    ///    embed the spawn just above the lowest platform top, capped at the box edge —
    ///    physics depenetration resolves it; never a KO-zone spawn. (This last resort
    ///    ignores occupied spawns — an embedded stack is survivable, a KO spawn is not.)
    /// </summary>
    public static Vec2 RepairSpawn(
        Vec2 gene, IReadOnlyList<PlatformGene> platforms, float visW, float visH,
        IReadOnlyList<Vec2>? occupied = null)
    {
        float edgeY = visH - SpawnEdgeClearance;
        float x = Math.Clamp(gene.X, -visW + SpawnEdgeClearance, visW - SpawnEdgeClearance);
        float y = Math.Clamp(gene.Y, -visH + SpawnEdgeClearance, edgeY);

        if (OverAnyPlatform(x, y, platforms))
        {
            Vec2 nudged = LegacySafeSpawn(new Vec2(x, y), platforms);
            if (nudged.Y <= edgeY && !BodyEmbedded(nudged, platforms)
                && !OverlapsAnySpawn(nudged, occupied))
            {
                return nudged;
            }
        }

        Vec2? best = ScanColumns(platforms, visW, visH, x, occupied);
        // Spawn separation is BEST-EFFORT (2026-08-12): when no column clears the
        // occupied spawns, a stacked spawn beats an embedded one — rescan bare.
        best ??= occupied is { Count: > 0 } ? ScanColumns(platforms, visW, visH, x, null) : null;
        if (best is { } found)
        {
            return found;
        }

        PlatformGene lowest = platforms[0];
        foreach (PlatformGene p in platforms)
        {
            if (p.Y + p.YSize < lowest.Y + lowest.YSize)
            {
                lowest = p;
            }
        }
        float cx = Math.Clamp(
            lowest.X + lowest.XSize * 0.5f,
            -visW + SpawnEdgeClearance, visW - SpawnEdgeClearance);
        return new Vec2(cx, MathF.Min(lowest.Y + lowest.YSize + SpawnEdgeClearance, edgeY));
    }

    /// <summary>Best free spawn column across every platform, nearest to preferredX;
    /// null when the layout offers none under the given occupied constraints.</summary>
    private static Vec2? ScanColumns(
        IReadOnlyList<PlatformGene> platforms, float visW, float visH, float preferredX,
        IReadOnlyList<Vec2>? occupied)
    {
        Vec2? best = null;
        float bestDist = float.MaxValue;
        foreach (PlatformGene p in platforms)
        {
            Vec2? spot = TrySpawnOver(p, platforms, visW, visH, preferredX, occupied);
            if (spot is { } s)
            {
                float dist = MathF.Abs(s.X - preferredX);
                if (dist < bestDist)
                {
                    best = s;
                    bestDist = dist;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Conservative player-BODY half extents for spawn placement: the largest possible
    /// body (PlayerBaseWidth/Height × the max width/height scalars, halved) plus slack.
    /// A spawn column must clear blockers by these — a merely point-legal spawn can
    /// EMBED the body in a platform edge, and the axis-clamp physics then ejects an
    /// embedded body to the platform's far side on the first held direction, which on
    /// a narrow map is straight out of the blast zone (a death per tick; found by the
    /// tall-narrow showcase probe, 2026-07-21).
    /// </summary>
    public const float SpawnBodyHalfWidth = 0.62f;  // 0.74289274 × 1.5 / 2 ≈ 0.557 + slack
    public const float SpawnBodyHalfHeight = 0.83f; // 1 × 1.5 / 2 = 0.75 + slack

    /// <summary>
    /// A legal spawn spot over this platform, or null: the spawn hovers at the legacy
    /// +2 above the platform top (capped at the visible-box top edge, never closer to
    /// the top than a body half height), in a column of the platform's span where no
    /// other platform intersects the BODY's box — so the safe-spawn nudge never fires,
    /// the body embeds in nothing, and the spot is exactly where the player appears.
    /// Since 2026-08-12 already-placed spawns block columns the same way (the
    /// non-overlap rule), and <paramref name="axisClearHalfWidth"/> &gt; 0 blocks a
    /// band around x = 0 (mirrored generation: a spawn too close to the axis would
    /// overlap its own mirror). x lands as close to preferredX as the free intervals
    /// allow. Deterministic.
    /// </summary>
    public static Vec2? TrySpawnOver(
        PlatformGene platform, IReadOnlyList<PlatformGene> platforms,
        float visW, float visH, float preferredX,
        IReadOnlyList<Vec2>? occupied = null, float axisClearHalfWidth = 0f)
    {
        float edgeY = visH - SpawnEdgeClearance;
        float top = platform.Y + platform.YSize;
        if (top + SpawnBodyHalfHeight > edgeY)
        {
            return null; // no headroom inside the visible box
        }
        float xLo = MathF.Max(platform.X, -visW + SpawnEdgeClearance);
        float xHi = MathF.Min(platform.X + platform.XSize, visW - SpawnEdgeClearance);
        if (xHi < xLo)
        {
            return null; // span entirely outside the visible box
        }

        // Hover candidates, highest preference first: the legacy +2 hover, then lower
        // hovers down to just clear of the platform — dense lattices often roof the +2
        // band while a lower one is open (seed-94 probe, 2026-07-21).
        float preferred = MathF.Min(top + 2f, edgeY);
        float minimal = top + SpawnBodyHalfHeight + 0.02f;
        Span<float> hovers = stackalloc[] { preferred, (preferred + minimal) * 0.5f, minimal };
        foreach (float y in hovers)
        {
            if (FindFreeColumn(platform, platforms, xLo, xHi, y, preferredX,
                    occupied, axisClearHalfWidth) is { } spot)
            {
                return spot;
            }
        }
        return null;
    }

    /// <summary>Free intervals of [xLo, xHi] after subtracting body-padded spans of
    /// platforms intersecting the body's vertical band at hover height y — plus
    /// occupied-spawn bands and the mirror-axis band (2026-08-12); returns the
    /// point nearest preferredX, or null. List order is fixed; the interval walk is
    /// deterministic.</summary>
    private static Vec2? FindFreeColumn(
        PlatformGene platform, IReadOnlyList<PlatformGene> platforms,
        float xLo, float xHi, float y, float preferredX,
        IReadOnlyList<Vec2>? occupied = null, float axisClearHalfWidth = 0f)
    {
        var free = new List<(float Lo, float Hi)> { (xLo, xHi) };
        foreach (PlatformGene q in platforms)
        {
            if (q == platform
                || q.Y > y + SpawnBodyHalfHeight || q.Y + q.YSize < y - SpawnBodyHalfHeight)
            {
                continue;
            }
            // The extra 0.03 keeps a column clamped exactly to an interval edge from
            // grazing the block boundary after float rounding.
            SubtractInterval(free, q.X - SpawnBodyHalfWidth - 0.03f,
                q.X + q.XSize + SpawnBodyHalfWidth + 0.03f);
        }
        if (occupied is not null)
        {
            foreach (Vec2 s in occupied)
            {
                if (MathF.Abs(s.Y - y) >= 2f * SpawnBodyHalfHeight)
                {
                    continue; // different band — bodies cannot intersect
                }
                SubtractInterval(free, s.X - 2f * SpawnBodyHalfWidth - 0.03f,
                    s.X + 2f * SpawnBodyHalfWidth + 0.03f);
            }
        }
        if (axisClearHalfWidth > 0f)
        {
            SubtractInterval(free, -axisClearHalfWidth - 0.03f, axisClearHalfWidth + 0.03f);
        }

        Vec2? best = null;
        float bestDist = float.MaxValue;
        foreach ((float lo, float hi) in free)
        {
            if (hi - lo < SpawnEdgeClearance)
            {
                continue; // sliver — not a spawnable column
            }
            float x = Math.Clamp(preferredX, lo, hi);
            float dist = MathF.Abs(x - preferredX);
            if (dist < bestDist)
            {
                best = new Vec2(x, y);
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>Removes [blockLo, blockHi] from a free-interval list in place.</summary>
    private static void SubtractInterval(List<(float Lo, float Hi)> free, float blockLo, float blockHi)
    {
        for (int i = free.Count - 1; i >= 0; i--)
        {
            (float lo, float hi) = free[i];
            if (blockHi <= lo || blockLo >= hi)
            {
                continue;
            }
            free.RemoveAt(i);
            if (lo < blockLo)
            {
                free.Insert(i, (lo, blockLo));
            }
            if (blockHi < hi)
            {
                free.Insert(i, (blockHi, hi));
            }
        }
    }

    /// <summary>The conservative body box at this point intersects some platform —
    /// a point-legal spawn the physics would eject (see SpawnBodyHalfWidth).</summary>
    private static bool BodyEmbedded(Vec2 point, IReadOnlyList<PlatformGene> platforms)
    {
        foreach (PlatformGene p in platforms)
        {
            if (point.X + SpawnBodyHalfWidth > p.X && point.X - SpawnBodyHalfWidth < p.X + p.XSize
                && point.Y + SpawnBodyHalfHeight > p.Y && point.Y - SpawnBodyHalfHeight < p.Y + p.YSize)
            {
                return true;
            }
        }
        return false;
    }

    private static bool OverAnyPlatform(float x, float y, IReadOnlyList<PlatformGene> platforms)
    {
        foreach (PlatformGene p in platforms)
        {
            if (x >= p.X && x <= p.X + p.XSize && y >= p.Y + p.YSize)
            {
                return true;
            }
        }
        return false;
    }
}
