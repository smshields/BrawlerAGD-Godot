using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Derived stage values and post-generation constraints for the Map Size feature
/// (2026-07-21, FEATURES.md §Map Size; docs/features/map-size.md) — the stage
/// counterpart of MoveRules. Everything here is pure and deterministic: it runs at
/// genome load and SimWorld construction, so it is part of the replay contract.
/// </summary>
public static class StageRules
{
    /// <summary>
    /// Legacy visible half extents. Width is 11·(16/9)/2 — the blast width WITHOUT its
    /// second 1.1 factor (the preserved Unity aspect quirk), NOT the true camera half
    /// width (8.889): that choice makes visible × (1 + LegacyKoMargin) reproduce
    /// MatchConfig's legacy blast zone bit-exactly (regression-tested; power-of-two
    /// division commutes with float rounding, so (a/2)·1.1f == (a·1.1f)/2).
    /// </summary>
    public const float LegacyVisibleHalfWidth = 11f * (16f / 9f) / 2f;
    public const float LegacyVisibleHalfHeight = 5f;
    public const float LegacyKoMargin = 0.1f;
    public const float LegacyPlatformCount = 6f;   // post-mirror mean of the Unity generator
    public const float LegacyMaxPlatformSize = 6f; // Unity MapGenerator(2, 2, 3, 6)

    /// <summary>Spawn clearance from the visible edge and above a platform top —
    /// roughly a player half extent, keeping repaired spawns clear of immediate KO.</summary>
    public const float SpawnEdgeClearance = 0.5f;

    public static bool IsMirrored(ParamSet stageParams) =>
        stageParams.Get(StageParams.Mirrored) >= 0.5f;

    /// <summary>false = left half is the mirror source, true = right.</summary>
    public static bool MirrorSideRight(ParamSet stageParams) =>
        stageParams.Get(StageParams.MirrorSide) >= 0.5f;

    public static int PlatformCountOf(ParamSet stageParams) =>
        IntGene(stageParams.Get(StageParams.PlatformCount), 2, 16);

    public static int MaxPlatformSizeOf(ParamSet stageParams) =>
        IntGene(stageParams.Get(StageParams.MaxPlatformSize), 3, 14);

    /// <summary>Int-as-float gene: floor, clamped (a gene exactly at the range top
    /// floors to the top value, not one past it).</summary>
    public static int IntGene(float value, int min, int max) =>
        Math.Clamp((int)MathF.Floor(value), min, max);

    public static Vec2 BlastHalfExtents(ParamSet stageParams)
    {
        float margin = 1f + stageParams.Get(StageParams.KoMarginFraction);
        return new Vec2(
            stageParams.Get(StageParams.VisibleHalfWidth) * margin,
            stageParams.Get(StageParams.VisibleHalfHeight) * margin);
    }

    /// <summary>The legacy stage ParamSet for a platform list: pre-v7 dimensions and
    /// the pre-feature derived spawns. Loading any pre-v7 game.json through this makes
    /// it play bit-identically to the pre-feature sim. The schema parameter exists for
    /// range-override runs, whose genomes must all bind the run's rebuilt schema
    /// instance (GenomeOps.RequireSameSchema).</summary>
    public static ParamSet LegacyParams(IReadOnlyList<PlatformGene> platforms, ParamSchema? schema = null)
    {
        Vec2 spawn1 = DeriveLegacySpawn(platforms);
        Vec2 spawn2 = LegacySafeSpawn(new Vec2(-spawn1.X, spawn1.Y), platforms);
        return new ParamSet(schema ?? DefaultSchemas.Stage, new[]
        {
            LegacyVisibleHalfWidth,
            LegacyVisibleHalfHeight,
            LegacyKoMargin,
            LegacyPlatformCount,
            LegacyMaxPlatformSize,
            1f, // mirrored — the Unity generator always mirrored
            0f, // mirrorSide — left half was the source
            spawn1.X, spawn1.Y,
            spawn2.X, spawn2.Y,
            0f, // platformSpawnDuration — spawning feature OFF (2026-07-22, pre-v8 parity)
            0f, // spawnInvulnDuration — off
        });
    }

    public static float PlatformSpawnSeconds(ParamSet stageParams) =>
        stageParams.Get(StageParams.PlatformSpawnDuration);

    public static float SpawnInvulnSeconds(ParamSet stageParams) =>
        stageParams.Get(StageParams.SpawnInvulnDuration);

    /// <summary>The spawning feature is a per-level (stage) property: active when
    /// either duration gene is positive. Off ⇒ the sim is byte-for-byte pre-feature.</summary>
    public static bool SpawnFeatureActive(ParamSet stageParams) =>
        PlatformSpawnSeconds(stageParams) > 0f || SpawnInvulnSeconds(stageParams) > 0f;

    /// <summary>
    /// Unity spawn rule (previously SimWorld.ComputeSpawn, moved verbatim): player 1
    /// spawns centered above the initial platform, +2 above its top, nudged upward
    /// while inside any platform.
    /// </summary>
    public static Vec2 DeriveLegacySpawn(IReadOnlyList<PlatformGene> platforms)
    {
        PlatformGene initial = platforms[0];
        int x = initial.X + (initial.XSize + 1) / 2;
        int y = initial.Y + initial.YSize + 2;
        return LegacySafeSpawn(new Vec2(x, y), platforms);
    }

    public static Vec2 LegacySafeSpawn(Vec2 candidate, IReadOnlyList<PlatformGene> platforms)
    {
        float y = candidate.Y;
        while (InsideAnyPlatform(candidate.X, y, platforms))
        {
            y += 1f;
        }
        return new Vec2(candidate.X, y);
    }

    private static bool InsideAnyPlatform(float x, float y, IReadOnlyList<PlatformGene> platforms)
    {
        foreach (PlatformGene p in platforms)
        {
            if (x >= p.X && x <= p.X + p.XSize && y >= p.Y && y <= p.Y + p.YSize)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Repairs both spawn genes of a stage ParamSet against a platform layout. Runs at
    /// the GENETIC-OPS layer (generation, crossover, the mutation transform) — NEVER at
    /// SimWorld construction: pre-v7 artifacts store spawns exactly as the old sim
    /// derived them (including ones the old sim let fall to their death on
    /// crossover-broken asymmetric layouts), and replaying those bit-identically means
    /// the sim must consume genes untouched save for the legacy upward nudge.
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
            platforms, visW, visH);
        return stageParams.With(
            (StageParams.Spawn1X, s1.X), (StageParams.Spawn1Y, s1.Y),
            (StageParams.Spawn2X, s2.X), (StageParams.Spawn2Y, s2.Y));
    }

    /// <summary>
    /// Makes a raw spawn gene legal for a layout (designer rules 2026-07-21: never in
    /// immediate KO bounds, always over a platform). Deterministic, and the IDENTITY
    /// for already-legal spawns:
    /// 1. clamp inside the visible box (KO sits outside it) with edge clearance;
    /// 2. already over a platform, and the legacy inside-a-platform nudge stays inside
    ///    the box → keep the (nudged) position;
    /// 3. otherwise scan every platform for a FREE spawn column (airspace no other
    ///    platform occupies, so the nudge cannot fire) and take the legal spot nearest
    ///    the gene, ties to the earliest platform in list order;
    /// 4. degenerate layouts with no free column anywhere (a fully roofed wall)
    ///    embed the spawn just above the lowest platform top, capped at the box edge —
    ///    physics depenetration resolves it; never a KO-zone spawn.
    /// </summary>
    public static Vec2 RepairSpawn(
        Vec2 gene, IReadOnlyList<PlatformGene> platforms, float visW, float visH)
    {
        float edgeY = visH - SpawnEdgeClearance;
        float x = Math.Clamp(gene.X, -visW + SpawnEdgeClearance, visW - SpawnEdgeClearance);
        float y = Math.Clamp(gene.Y, -visH + SpawnEdgeClearance, edgeY);

        if (OverAnyPlatform(x, y, platforms))
        {
            Vec2 nudged = LegacySafeSpawn(new Vec2(x, y), platforms);
            if (nudged.Y <= edgeY && !BodyEmbedded(nudged, platforms))
            {
                return nudged;
            }
        }

        Vec2? best = null;
        float bestDist = float.MaxValue;
        foreach (PlatformGene p in platforms)
        {
            Vec2? spot = TrySpawnOver(p, platforms, visW, visH, x);
            if (spot is { } s)
            {
                float dist = MathF.Abs(s.X - x);
                if (dist < bestDist)
                {
                    best = s;
                    bestDist = dist;
                }
            }
        }
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
    /// x lands as close to preferredX as the free intervals allow. Deterministic.
    /// </summary>
    public static Vec2? TrySpawnOver(
        PlatformGene platform, IReadOnlyList<PlatformGene> platforms,
        float visW, float visH, float preferredX)
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
            if (FindFreeColumn(platform, platforms, xLo, xHi, y, preferredX) is { } spot)
            {
                return spot;
            }
        }
        return null;
    }

    /// <summary>Free intervals of [xLo, xHi] after subtracting body-padded spans of
    /// platforms intersecting the body's vertical band at hover height y; returns the
    /// point nearest preferredX, or null. List order is fixed; the interval walk is
    /// deterministic.</summary>
    private static Vec2? FindFreeColumn(
        PlatformGene platform, IReadOnlyList<PlatformGene> platforms,
        float xLo, float xHi, float y, float preferredX)
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
            float blockLo = q.X - SpawnBodyHalfWidth - 0.03f;
            float blockHi = q.X + q.XSize + SpawnBodyHalfWidth + 0.03f;
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

    /// <summary>
    /// The asymmetric→mirrored mutation transform (designer 2026-07-21): keep the
    /// chosen half's platforms (clamping any that cross x = 0 to the axis), then
    /// reflect them. Degenerate case — no platform mass on the chosen side — returns
    /// null and the caller falls back to full regeneration.
    /// </summary>
    public static List<PlatformGene>? MirrorTransform(
        IReadOnlyList<PlatformGene> platforms, bool rightSideIsSource)
    {
        var source = new List<PlatformGene>();
        foreach (PlatformGene p in platforms)
        {
            int left = p.X;
            int right = p.X + p.XSize;
            // Clamp to the source half; drop platforms fully on the other side.
            int clampedLeft = rightSideIsSource ? Math.Max(left, 0) : left;
            int clampedRight = rightSideIsSource ? right : Math.Min(right, 0);
            if (clampedRight - clampedLeft < 1)
            {
                continue;
            }
            source.Add(p with { X = clampedLeft, XSize = clampedRight - clampedLeft });
        }
        if (source.Count == 0)
        {
            return null;
        }
        var result = new List<PlatformGene>(source.Count * 2);
        result.AddRange(source);
        foreach (PlatformGene p in source)
        {
            PlatformGene mirror = p.MirrorX();
            // A platform ending exactly on the axis mirrors to one starting on it —
            // adjacency, not overlap; only skip exact duplicates (zero-width source
            // halves cannot occur, the clamp guarantees XSize ≥ 1).
            if (mirror != p)
            {
                result.Add(mirror);
            }
        }
        return result;
    }

    /// <summary>
    /// The abstract traversability check (designer option a, 2026-07-21): platforms
    /// form a connected graph where two platforms are adjacent when their rects,
    /// expanded by the jump grid reach, overlap. Used by generator property tests and
    /// available as the option-b hook's baseline.
    /// </summary>
    public static bool IsConnected(IReadOnlyList<PlatformGene> platforms, int jumpHeight, int jumpLength)
    {
        if (platforms.Count <= 1)
        {
            return true;
        }
        // Reach: legacy Left placement allows a horizontal gap up to jumpLength and a
        // vertical offset up to jumpHeight + platform thickness; Above allows a rise
        // of jumpHeight above the parent top. Expansion by (jumpLength, jumpHeight + 2)
        // is the loosest rect containment of those legacy placements.
        int reachX = jumpLength;
        int reachY = jumpHeight + 2;
        var visited = new bool[platforms.Count];
        var queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);
        int seen = 1;
        while (queue.Count > 0)
        {
            PlatformGene a = platforms[queue.Dequeue()];
            for (int i = 0; i < platforms.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }
                PlatformGene b = platforms[i];
                // Non-strict: a legacy mirrored stage's seam gap is exactly jumpLength
                // (midGap per side), and jumpLength IS the reachable hop by definition.
                bool xTouch = a.X - reachX <= b.X + b.XSize && b.X - reachX <= a.X + a.XSize;
                bool yTouch = a.Y - reachY <= b.Y + b.YSize && b.Y - reachY <= a.Y + a.YSize;
                if (xTouch && yTouch)
                {
                    visited[i] = true;
                    seen++;
                    queue.Enqueue(i);
                }
            }
        }
        return seen == platforms.Count;
    }

    /// <summary>A layout is symmetric when every platform's mirror is also present
    /// (as a set — generation order does not matter).</summary>
    public static bool IsSymmetric(IReadOnlyList<PlatformGene> platforms)
    {
        var set = new HashSet<PlatformGene>(platforms);
        foreach (PlatformGene p in platforms)
        {
            if (!set.Contains(p.MirrorX()))
            {
                return false;
            }
        }
        return true;
    }

    public static bool Overlaps(in PlatformGene a, in PlatformGene b) =>
        a.X < b.X + b.XSize && b.X < a.X + a.XSize
        && a.Y < b.Y + b.YSize && b.Y < a.Y + a.YSize;

    public static bool OverlapsAny(in PlatformGene candidate, IReadOnlyList<PlatformGene> platforms)
    {
        foreach (PlatformGene p in platforms)
        {
            if (Overlaps(candidate, p))
            {
                return true;
            }
        }
        return false;
    }

    // ── Per-character platform fit (2026-07-22, FEATURES.md §Map Size follow-up) ──────
    // The abstract jump grid (jumpHeight/jumpLength) is character-BLIND, so a stage can
    // let one character move between platforms while the other cannot, and a gap can be
    // fall-through-passable for a small body but a wall for a large one. The designer's
    // fix: after the stage AND both characters are known, MOVE platforms so BOTH
    // characters can traverse every intended edge and no gap is asymmetrically passable
    // — deterministically, never by re-rolling (which would desync the RNG stream).

    /// <summary>A character's hop parameters, mirroring PlatformGraph.HopFeasible (the
    /// agent's ACTUAL reachability model) EXACTLY — jump forces, air speed, and scaled
    /// gravity, no dash. The dash is deliberately excluded because the agent's
    /// pathfinder ignores it: fitting to a dash-inclusive reach would still leave the
    /// agent unable to route the gap, which is the very asymmetry being fixed.</summary>
    public readonly struct CharHop
    {
        public readonly float V1, V2, Air, G;
        public CharHop(CharacterGenome c, float gravity)
        {
            ParamSet p = c.Params;
            V1 = p.Get(CharacterParams.GroundJumpForce);
            V2 = p.Get(CharacterParams.AirJumpForce);
            Air = p.Get(CharacterParams.MaxAirSpeed);
            G = MathF.Max(0.01f, gravity * p.Get(CharacterParams.GravityScalar));
        }

        public float MaxRise => (V1 * V1 + V2 * V2) / (2f * G);

        /// <summary>Horizontal reach when the target sits <paramref name="dy"/> above
        /// (PlatformGraph.HopFeasible's ascent+descent flight time × air speed × 0.9).</summary>
        public float HorizReach(float dy)
        {
            float ascent = (V1 + V2) / G;
            float descent = MathF.Sqrt(2f * MathF.Max(0.1f, MaxRise - dy) / G);
            return Air * (ascent + descent) * 0.9f;
        }

        public bool CanHop(in PlatformGene from, in PlatformGene to)
        {
            float dy = (to.Y + to.YSize) - (from.Y + from.YSize);
            if (dy > MaxRise)
            {
                return false;
            }
            float gap = MathF.Max(0f, MathF.Max(to.X - (from.X + from.XSize), from.X - (to.X + to.XSize)));
            return gap <= HorizReach(dy);
        }
    }

    /// <summary>Body half extents in world units (for the fall-through gap check).</summary>
    private static float BodyWidth(CharacterGenome c, float baseWidth) =>
        baseWidth * c.Params.Get(CharacterParams.WidthScalar);

    /// <summary>A body passes a vertical corridor when the gap clears its width by
    /// this slack; below it the corridor is a wall for that body.</summary>
    private const float GapPassSlack = 0.1f;

    /// <summary>
    /// Adjusts a stage's platforms so BOTH characters can reach every platform from the
    /// first one, and no horizontal gap is fall-through-passable for one body but a wall
    /// for the other. Deterministic, integer moves, overlap-guarded. Rewritten
    /// 2026-07-27 (designer: asymmetric gaps still appeared in play): the body-fit is
    /// now an ITERATIVE solver — it re-scans after every repositioning (a fix can open
    /// a new violation elsewhere), rotates through five strategies per violating pair
    /// (widen right / widen left / dock right / dock left / vertical separation), and
    /// is loop-proof by construction: per-pair attempt counters pick a DIFFERENT
    /// strategy each revisit, a pair that exhausts its attempts is force-resolved by
    /// docking (a contiguous wall is symmetric for every body), and a pair that cannot
    /// even dock is marked unresolvable and skipped — so every pass strictly reduces
    /// open work and the loop always terminates. Connectivity and body-fit alternate
    /// until a full round changes nothing. Spawns are re-repaired against the adjusted
    /// layout. Returns the stage unchanged (same instance) when the layout already
    /// satisfies both characters — the common case.
    /// </summary>
    public static StageGenome FitToCharacters(
        StageGenome stage, CharacterGenome a, CharacterGenome b, float gravity, float playerBaseWidth)
    {
        var ha = new CharHop(a, gravity);
        var hb = new CharHop(b, gravity);
        float wa = BodyWidth(a, playerBaseWidth);
        float wb = BodyWidth(b, playerBaseWidth);
        float smallW = MathF.Min(wa, wb);
        float largeW = MathF.Max(wa, wb);

        var plats = stage.Platforms.ToArray();
        bool changed = false;
        var attempts = new Dictionary<(int, int), int>();
        var unresolvable = new HashSet<(int, int)>();

        // Alternate the two phases until a full round is quiet: body-fit moves can
        // break connectivity and connectivity pulls can open new corridors. The round
        // cap is a backstop — per-pair attempt counters and the unresolvable set make
        // the body-fit's open work strictly shrink, so rounds go quiet on their own.
        for (int round = 0; round < 8; round++)
        {
            bool moved = ConnectPhase(plats, ha, hb);
            moved |= BodyFitPhase(plats, ha, hb, smallW, largeW, attempts, unresolvable);
            changed |= moved;
            if (!moved)
            {
                break;
            }
        }

        if (!changed)
        {
            return stage;
        }
        var list = plats.ToList();
        return new StageGenome(list, RepairSpawns(list, stage.Params));
    }

    /// <summary>Phase 1 — connectivity for BOTH characters: grow a connected set from
    /// platform 0 over edges BOTH can hop (PlatformGraph's model). Each stalled round,
    /// pull the nearest still-unreachable platform toward its best connector until both
    /// can hop it — integer moves, overlap-guarded.</summary>
    private static bool ConnectPhase(PlatformGene[] plats, in CharHop ha, in CharHop hb)
    {
        bool changed = false;
        var connected = new bool[plats.Length];
        connected[0] = true;
        int connectedCount = 1;
        int guard = plats.Length * plats.Length + 8;
        while (connectedCount < plats.Length && guard-- > 0)
        {
            // Any platform already both-hop-reachable from the connected set joins free.
            bool grew = false;
            for (int u = 0; u < plats.Length; u++)
            {
                if (connected[u])
                {
                    continue;
                }
                for (int c = 0; c < plats.Length && !connected[u]; c++)
                {
                    if (connected[c] && BothCanHop(plats[c], plats[u], ha, hb))
                    {
                        connected[u] = true;
                        connectedCount++;
                        grew = true;
                    }
                }
            }
            if (grew)
            {
                continue;
            }
            // None reachable as-is: pick the unreachable platform nearest the connected
            // set, then try to pull it to EACH connector (nearest first) until one takes.
            int bestU = -1;
            float bestDist = float.MaxValue;
            for (int u = 0; u < plats.Length; u++)
            {
                if (connected[u])
                {
                    continue;
                }
                for (int c = 0; c < plats.Length; c++)
                {
                    if (connected[c])
                    {
                        float dist = CenterDistanceSq(plats[c], plats[u]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestU = u;
                        }
                    }
                }
            }
            if (bestU < 0)
            {
                break;
            }
            foreach (int c in ConnectorsByDistance(plats, connected, bestU))
            {
                if (TryPullWithinHop(ref plats[bestU], plats[c], plats, bestU, ha, hb)
                    && BothCanHop(plats[c], plats[bestU], ha, hb))
                {
                    changed = true;
                    break;
                }
            }
            connected[bestU] = true; // bounded progress even if every connector was blocked
            connectedCount++;
        }
        return changed;
    }

    /// <summary>Phase 2 — the iterative asymmetric-gap solver (see FitToCharacters).</summary>
    private static bool BodyFitPhase(
        PlatformGene[] plats, in CharHop ha, in CharHop hb, float smallW, float largeW,
        Dictionary<(int, int), int> attempts, HashSet<(int, int)> unresolvable)
    {
        const int MaxPasses = 12;
        const int StrategyCount = 5;
        bool movedAny = false;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            List<(int I, int J)> violations = FindAsymmetricGaps(plats, smallW, largeW, unresolvable);
            if (violations.Count == 0)
            {
                return movedAny;
            }
            bool progress = false;
            foreach ((int i, int j) in violations)
            {
                int tried = attempts.GetValueOrDefault((i, j));
                attempts[(i, j)] = tried + 1;
                if (tried >= StrategyCount)
                {
                    // This pair has cycled every strategy across passes: force it.
                    if (ForceResolve(plats, i, j))
                    {
                        progress = true;
                        movedAny = true;
                    }
                    else
                    {
                        unresolvable.Add((i, j));
                    }
                    continue;
                }
                // Rotate the starting strategy by the attempt count, so a pair that
                // reappears is attacked DIFFERENTLY each time (the loop-prevention).
                for (int s = 0; s < StrategyCount; s++)
                {
                    if (TryGapStrategy((tried + s) % StrategyCount, plats, i, j, ha, hb, largeW))
                    {
                        progress = true;
                        movedAny = true;
                        break;
                    }
                }
            }
            if (!progress)
            {
                // Nothing moved this pass: force the first open violation so the pass
                // loop strictly shrinks its work, else retire it as unresolvable.
                (int i, int j) = violations[0];
                if (ForceResolve(plats, i, j))
                {
                    movedAny = true;
                }
                else
                {
                    unresolvable.Add((i, j));
                }
            }
        }
        return movedAny;
    }

    /// <summary>Pairs forming a vertical corridor (side-by-side with vertical overlap,
    /// i left of j) whose gap the SMALLER body passes but the larger cannot.</summary>
    private static List<(int I, int J)> FindAsymmetricGaps(
        PlatformGene[] plats, float smallW, float largeW, HashSet<(int, int)> unresolvable)
    {
        var violations = new List<(int, int)>();
        for (int i = 0; i < plats.Length; i++)
        {
            for (int j = 0; j < plats.Length; j++)
            {
                if (i == j || !VerticallyOverlap(plats[i], plats[j]))
                {
                    continue;
                }
                float gap = plats[j].X - (plats[i].X + plats[i].XSize); // j right of i
                if (gap <= 0f || unresolvable.Contains((i, j)))
                {
                    continue;
                }
                bool smallPasses = gap >= smallW + GapPassSlack;
                bool largePasses = gap >= largeW + GapPassSlack;
                if (smallPasses && !largePasses)
                {
                    violations.Add((i, j));
                }
            }
        }
        return violations;
    }

    /// <summary>One repositioning strategy for the corridor between i (left) and j.
    /// Prefers WIDENING (both bodies pass — "gaps navigable by all characters") and
    /// falls back to docking (a contiguous wall, symmetric) and vertical separation
    /// (no corridor at all). A strategy is accepted only when it introduces no overlap
    /// and does not shrink both-character connectivity.</summary>
    private static bool TryGapStrategy(
        int strategy, PlatformGene[] plats, int i, int j, in CharHop ha, in CharHop hb, float largeW)
    {
        float gap = plats[j].X - (plats[i].X + plats[i].XSize);
        int widen = (int)MathF.Ceiling(largeW + GapPassSlack + 0.4f - gap);
        (int idx, PlatformGene cand) = strategy switch
        {
            // Widen so BOTH bodies clear the corridor.
            0 => (j, plats[j] with { X = plats[j].X + widen }),
            1 => (i, plats[i] with { X = plats[i].X - widen }),
            // Dock contiguous: gap 0 is a wall for every body — symmetric.
            2 => (j, plats[j] with { X = plats[i].X + plats[i].XSize }),
            3 => (i, plats[i] with { X = plats[j].X - plats[i].XSize }),
            // Separate vertically: no vertical overlap ⇒ no corridor. Smaller shift wins.
            _ => (j, VerticalSeparation(plats[i], plats[j])),
        };
        if (OverlapsAnyExcept(cand, plats, idx))
        {
            return false;
        }
        PlatformGene saved = plats[idx];
        int before = BothConnectedCount(plats, ha, hb);
        plats[idx] = cand;
        if (BothConnectedCount(plats, ha, hb) < before)
        {
            plats[idx] = saved; // never trade a gap fix for lost traversability
            return false;
        }
        return true;
    }

    /// <summary>Last-resort resolution, overlap-guarded only: dock contiguous (either
    /// side), then vertical separation. False only when every option overlaps.</summary>
    private static bool ForceResolve(PlatformGene[] plats, int i, int j)
    {
        Span<(int Idx, PlatformGene Cand)> options = stackalloc (int, PlatformGene)[]
        {
            (j, plats[j] with { X = plats[i].X + plats[i].XSize }),
            (i, plats[i] with { X = plats[j].X - plats[i].XSize }),
            (j, VerticalSeparation(plats[i], plats[j])),
        };
        foreach ((int idx, PlatformGene cand) in options)
        {
            if (!OverlapsAnyExcept(cand, plats, idx))
            {
                plats[idx] = cand;
                return true;
            }
        }
        return false;
    }

    /// <summary>j shifted vertically just clear of i's span (up or down, whichever is
    /// the smaller move; +1 clearance so they no longer share a corridor band).</summary>
    private static PlatformGene VerticalSeparation(in PlatformGene i, in PlatformGene j)
    {
        int upShift = (i.Y + i.YSize) - j.Y + 1;   // move j up above i's top
        int downShift = (j.Y + j.YSize) - i.Y + 1; // move j down below i's bottom
        return upShift <= downShift
            ? j with { Y = j.Y + upShift }
            : j with { Y = j.Y - downShift };
    }

    /// <summary>Platforms reachable from platform 0 over BOTH-hop edges.</summary>
    private static int BothConnectedCount(PlatformGene[] plats, in CharHop ha, in CharHop hb)
    {
        var visited = new bool[plats.Length];
        visited[0] = true;
        var queue = new Queue<int>();
        queue.Enqueue(0);
        int seen = 1;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int u = 0; u < plats.Length; u++)
            {
                if (!visited[u] && BothCanHop(plats[c], plats[u], ha, hb))
                {
                    visited[u] = true;
                    seen++;
                    queue.Enqueue(u);
                }
            }
        }
        return seen;
    }

    /// <summary>Connected platform indices, nearest to <paramref name="u"/> first.</summary>
    private static IEnumerable<int> ConnectorsByDistance(PlatformGene[] plats, bool[] connected, int u)
    {
        var list = new List<int>();
        for (int c = 0; c < plats.Length; c++)
        {
            if (connected[c])
            {
                list.Add(c);
            }
        }
        list.Sort((x, y) => CenterDistanceSq(plats[x], plats[u]).CompareTo(CenterDistanceSq(plats[y], plats[u])));
        return list;
    }

    private static bool BothCanHop(in PlatformGene from, in PlatformGene to, in CharHop a, in CharHop b) =>
        a.CanHop(from, to) && b.CanHop(from, to) && a.CanHop(to, from) && b.CanHop(to, from);

    private static float CenterDistanceSq(in PlatformGene a, in PlatformGene b)
    {
        float dx = (a.X + a.XSize * 0.5f) - (b.X + b.XSize * 0.5f);
        float dy = (a.Y + a.YSize * 0.5f) - (b.Y + b.YSize * 0.5f);
        return dx * dx + dy * dy;
    }

    /// <summary>Move u toward connector c — lower it until the rise is within BOTH
    /// characters' max, then shrink the horizontal gap until both can hop — in integer
    /// steps, leaving ≥ 1 unit and never overlapping (reverts on overlap).</summary>
    private static bool TryPullWithinHop(
        ref PlatformGene u, in PlatformGene c, PlatformGene[] all, int uIndex, in CharHop a, in CharHop b)
    {
        PlatformGene start = u;
        float minMaxRise = MathF.Min(a.MaxRise, b.MaxRise);
        for (int step = 0; step < 40 && !BothCanHop(c, u, a, b); step++)
        {
            float dyUp = (u.Y + u.YSize) - (c.Y + c.YSize);   // u above c
            float dyDown = (c.Y + c.YSize) - (u.Y + u.YSize); // c above u
            if (dyUp > minMaxRise)
            {
                u = u with { Y = u.Y - 1 }; // lower u so the climb onto it is reachable
            }
            else if (dyDown > minMaxRise)
            {
                u = u with { Y = u.Y + 1 }; // raise u so the climb back onto c is reachable
            }
            else
            {
                // Heights ok — close the horizontal gap by one unit toward c (min 1 gap).
                float gap = MathF.Max(u.X - (c.X + c.XSize), c.X - (u.X + u.XSize));
                if (gap <= 1f)
                {
                    break; // adjacent already; cannot close further without overlap
                }
                u = u.X > c.X ? u with { X = u.X - 1 } : u with { X = u.X + 1 };
            }
            if (OverlapsAnyExcept(u, all, uIndex))
            {
                u = start; // never introduce an overlap
                return false;
            }
        }
        // Last resort for a very weak character (reach < 1 unit): dock u CONTIGUOUS to c
        // at the same top — a continuous surface both can simply WALK across, feasible
        // for any reach. Only if it introduces no overlap.
        if (!BothCanHop(c, u, a, b))
        {
            int top = c.Y + c.YSize;
            var docked = u with
            {
                Y = top - u.YSize,
                X = u.X >= c.X ? c.X + c.XSize : c.X - u.XSize,
            };
            if (!OverlapsAnyExcept(docked, all, uIndex))
            {
                u = docked;
            }
        }
        return u.X != start.X || u.Y != start.Y;
    }

    private static bool VerticallyOverlap(in PlatformGene a, in PlatformGene b) =>
        a.Y < b.Y + b.YSize && b.Y < a.Y + a.YSize;

    private static bool OverlapsAnyExcept(in PlatformGene candidate, PlatformGene[] all, int except)
    {
        for (int i = 0; i < all.Length; i++)
        {
            if (i != except && Overlaps(candidate, all[i]))
            {
                return true;
            }
        }
        return false;
    }
}
