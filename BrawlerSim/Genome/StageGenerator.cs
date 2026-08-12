using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Procedural stage generation. Rewritten for Map Size (2026-07-21, FEATURES.md §Map
/// Size; docs/features/map-size.md) — the original Unity port (grow a platform tree
/// Left/Above from an initial platform, mirror across x = 0) generalizes to:
///
/// - map dimensions, KO margin, platform budget, max platform size, and symmetry are
///   drawn from the stage schema (so the evolve menu's range overrides steer them);
/// - growth goes Left/Right/Above/Below, each child within jump-grid reach of its
///   parent (traversability by construction — designer option a), rejected and
///   redrawn (bounded attempts) on overlap or when it would miss the visible box
///   entirely (platforms MAY extend past the KO line, rule 6, but must be reachable);
/// - mirrored stages grow only the x ≤ 0 half and reflect it (mirror copies cannot
///   overlap sources); asymmetric stages grow anywhere in the visible box;
/// - spawn genes are drawn over generated platforms (mirrored stages mirror spawn 1
///   for spawn 2 — fairness by symmetry; asymmetric stages draw independently on a
///   forced-distinct platform when more than one exists).
///
/// The Unity generator's abstract jump grid (jumpHeight/jumpLength, still 2/2 from
/// GenerationConfig) and its parent-relative Above sizing fix are preserved. All draws
/// come from one Pcg32 stream in fixed order — the stream shape is part of the
/// population-fingerprint golden.
/// </summary>
public sealed class StageGenerator
{
    public const int MinThickness = 1; // legacy MinWidth/MaxWidth (platform ySize)
    public const int MaxThickness = 2;

    /// <summary>Redraw attempts per child before the direction is skipped.</summary>
    public const int PlacementAttempts = 8;

    private readonly int _jumpHeight;
    private readonly int _jumpLength;
    private readonly ParamSchema _schema;

    public StageGenerator(int jumpHeight, int jumpLength, ParamSchema stageSchema)
    {
        _jumpHeight = jumpHeight;
        _jumpLength = jumpLength;
        _schema = stageSchema;
    }

    public StageGenome Generate(Pcg32 rng)
    {
        // 1. Structure genes, in schema order (fixed draw order = fingerprint contract).
        float visWidthGene = Draw(rng, StageParams.VisibleHalfWidth);
        float visHeightGene = Draw(rng, StageParams.VisibleHalfHeight);
        float koMarginGene = Draw(rng, StageParams.KoMarginFraction);
        float countGene = Draw(rng, StageParams.PlatformCount);
        float maxSizeGene = Draw(rng, StageParams.MaxPlatformSize);
        float mirroredGene = Draw(rng, StageParams.Mirrored);
        float mirrorSideGene = Draw(rng, StageParams.MirrorSide);
        // Spawning Behaviors (2026-07-22): two duration genes drawn with the other
        // structure genes (fixed draw order = fingerprint contract).
        float platformSpawnGene = Draw(rng, StageParams.PlatformSpawnDuration);
        float spawnInvulnGene = Draw(rng, StageParams.SpawnInvulnDuration);

        bool mirrored = mirroredGene >= 0.5f;
        int count = StageRules.IntGene(countGene, 2, 16);
        int maxSize = StageRules.IntGene(maxSizeGene, 3, 14);
        int gridW = Math.Max(3, (int)MathF.Floor(visWidthGene));
        int gridH = Math.Max(2, (int)MathF.Floor(visHeightGene));

        // 2 + 3. Platform layout and spawns, with degenerate-layout retries. FOUR
        // spawns per stage since 2026-08-12 (docs/features/four-player.md).
        (List<PlatformGene> platforms, Vec2 spawn1, Vec2 spawn2, Vec2 spawn3, Vec2 spawn4) =
            BuildLayoutAndSpawns(rng, mirrored, count, maxSize, gridW, gridH, visWidthGene, visHeightGene);

        var values = new float[_schema.Count];
        values[_schema.IndexOf(StageParams.VisibleHalfWidth)] = visWidthGene;
        values[_schema.IndexOf(StageParams.VisibleHalfHeight)] = visHeightGene;
        values[_schema.IndexOf(StageParams.KoMarginFraction)] = koMarginGene;
        values[_schema.IndexOf(StageParams.PlatformCount)] = countGene;
        values[_schema.IndexOf(StageParams.MaxPlatformSize)] = maxSizeGene;
        values[_schema.IndexOf(StageParams.Mirrored)] = mirroredGene;
        values[_schema.IndexOf(StageParams.MirrorSide)] = mirrorSideGene;
        values[_schema.IndexOf(StageParams.Spawn1X)] = spawn1.X;
        values[_schema.IndexOf(StageParams.Spawn1Y)] = spawn1.Y;
        values[_schema.IndexOf(StageParams.Spawn2X)] = spawn2.X;
        values[_schema.IndexOf(StageParams.Spawn2Y)] = spawn2.Y;
        values[_schema.IndexOf(StageParams.PlatformSpawnDuration)] = platformSpawnGene;
        values[_schema.IndexOf(StageParams.SpawnInvulnDuration)] = spawnInvulnGene;
        values[_schema.IndexOf(StageParams.Spawn3X)] = spawn3.X;
        values[_schema.IndexOf(StageParams.Spawn3Y)] = spawn3.Y;
        values[_schema.IndexOf(StageParams.Spawn4X)] = spawn4.X;
        values[_schema.IndexOf(StageParams.Spawn4Y)] = spawn4.Y;
        return new StageGenome(platforms, new ParamSet(_schema, values));
    }

    /// <summary>Regenerates ONLY the platform layout + spawns for an existing (already
    /// mutated) stage ParamSet — the mutation path (docs/features/map-size.md).</summary>
    public StageGenome Regenerate(ParamSet stageParams, Pcg32 rng)
    {
        bool mirrored = StageRules.IsMirrored(stageParams);
        int count = StageRules.PlatformCountOf(stageParams);
        int maxSize = StageRules.MaxPlatformSizeOf(stageParams);
        float visW = stageParams.Get(StageParams.VisibleHalfWidth);
        float visH = stageParams.Get(StageParams.VisibleHalfHeight);
        int gridW = Math.Max(3, (int)MathF.Floor(visW));
        int gridH = Math.Max(2, (int)MathF.Floor(visH));

        (List<PlatformGene> platforms, Vec2 spawn1, Vec2 spawn2, Vec2 spawn3, Vec2 spawn4) =
            BuildLayoutAndSpawns(rng, mirrored, count, maxSize, gridW, gridH, visW, visH);
        return new StageGenome(platforms, stageParams.With(
            (StageParams.Spawn1X, spawn1.X), (StageParams.Spawn1Y, spawn1.Y),
            (StageParams.Spawn2X, spawn2.X), (StageParams.Spawn2Y, spawn2.Y),
            (StageParams.Spawn3X, spawn3.X), (StageParams.Spawn3Y, spawn3.Y),
            (StageParams.Spawn4X, spawn4.X), (StageParams.Spawn4Y, spawn4.Y)));
    }

    /// <summary>How many times a degenerate layout (no body-safe spawn column
    /// anywhere) is regrown before the embed fallback is accepted.</summary>
    public const int LayoutAttempts = 4;

    /// <summary>Extra STRICT regrow attempts (2026-08-12, four-player.md) spent before
    /// the bare-tolerant LayoutAttempts: these accept only layouts where all four
    /// spawns separate (the designer non-overlap rule). They sit IN FRONT of the
    /// original budget, so the embed fallback is reached no more readily than
    /// pre-feature — separation degrades before legality ever does.</summary>
    public const int SeparationAttempts = 4;

    /// <summary>
    /// Grows a layout and draws all FOUR spawns (2026-08-12, four-player.md — every
    /// stage carries four regardless of player count), regrowing (bounded) when the
    /// layout has no body-safe spawn column — dense wall-clusters would otherwise ship
    /// embedded spawns that the axis-clamp physics ejects (seed-152 probe, 2026-07-21).
    /// Mirrored stages take spawns 2/4 as the exact mirrors of spawns 1/3 (fair by
    /// symmetry; legal by symmetry — no repair pass, whose left-biased tie-breaking
    /// would stack players on one column), with an axis-clearance band so no spawn
    /// overlaps its own mirror. Asymmetric stages draw each spawn independently on a
    /// forced-distinct platform when one is free. Every spawn treats the earlier ones
    /// as occupied columns (designer non-overlap rule). Draws per successful attempt
    /// are fixed: 2 per drawn spawn (platform index + position fraction).
    /// </summary>
    private (List<PlatformGene> Platforms, Vec2 Spawn1, Vec2 Spawn2, Vec2 Spawn3, Vec2 Spawn4)
        BuildLayoutAndSpawns(
            Pcg32 rng, bool mirrored, int count, int maxSize, int gridW, int gridH, float visW, float visH)
    {
        float axisClear = mirrored ? StageRules.SpawnBodyHalfWidth : 0f;
        List<PlatformGene> platforms = null!;
        for (int attempt = 0; attempt < SeparationAttempts + LayoutAttempts; attempt++)
        {
            // The first SeparationAttempts regrows accept only fully-separated spawn
            // sets; the remaining (original) budget may degrade to a bare
            // (platform-blockers-only) column — overlap beats the embed fallback,
            // but a regrown layout that separates beats both.
            bool allowBare = attempt >= SeparationAttempts;
            platforms = Grow(rng, mirrored, count, maxSize, gridW, gridH);
            if (mirrored)
            {
                int unmirrored = platforms.Count;
                for (int i = 0; i < unmirrored; i++)
                {
                    PlatformGene mirror = platforms[i].MirrorX();
                    if (mirror != platforms[i])
                    {
                        platforms.Add(mirror);
                    }
                }
            }

            if (!TryDrawSpawn(rng, platforms, rng.NextInt(platforms.Count), visW, visH,
                    null, axisClear, allowBare, out Vec2 spawn1))
            {
                continue; // degenerate layout — regrow
            }
            if (mirrored)
            {
                Vec2 spawn2 = new(-spawn1.X, spawn1.Y);
                int index3 = DistinctIndex(platforms, rng.NextInt(platforms.Count),
                    new[] { spawn1, spawn2 });
                if (TryDrawSpawn(rng, platforms, index3, visW, visH,
                        new[] { spawn1, spawn2 }, axisClear, allowBare, out Vec2 spawn3))
                {
                    return (platforms, spawn1, spawn2, spawn3, new Vec2(-spawn3.X, spawn3.Y));
                }
                continue;
            }
            var placed = new List<Vec2>(3) { spawn1 };
            for (int s = 1; s < 4; s++)
            {
                int index = DistinctIndex(platforms, rng.NextInt(platforms.Count), placed);
                if (!TryDrawSpawn(rng, platforms, index, visW, visH, placed, 0f, allowBare,
                        out Vec2 spawn))
                {
                    break; // no clear column for this spawn anywhere — regrow
                }
                placed.Add(spawn);
            }
            if (placed.Count == 4)
            {
                return (platforms, placed[0], placed[1], placed[2], placed[3]);
            }
        }
        // Every attempt degenerate: accept the embed fallback on the last layout —
        // physics depenetration resolves it; extraordinarily rare by construction.
        Vec2 f1 = StageRules.RepairSpawn(new Vec2(0f, visH), platforms, visW, visH);
        Vec2 f2 = mirrored ? new Vec2(-f1.X, f1.Y) : f1;
        (Vec2 f3, Vec2 f4) = StageRules.DeriveExtraSpawns(platforms, f1, f2, visW, visH);
        return (platforms, f1, f2, f3, f4);
    }

    /// <summary>Advances the drawn platform index (draw-free) to the first platform
    /// hosting none of the already-placed spawns — meaningful separation when the
    /// layout allows it; the original index when every platform is taken.</summary>
    private static int DistinctIndex(
        List<PlatformGene> platforms, int index, IReadOnlyList<Vec2> placed)
    {
        for (int step = 0; step < platforms.Count; step++)
        {
            int candidate = (index + step) % platforms.Count;
            bool hosts = false;
            foreach (Vec2 s in placed)
            {
                if (OnSamePlatform(s, platforms, candidate))
                {
                    hosts = true;
                    break;
                }
            }
            if (!hosts)
            {
                return candidate;
            }
        }
        return index; // every platform hosts a spawn — occupied blocking still separates
    }

    private float Draw(Pcg32 rng, string key)
    {
        ParamSpec spec = _schema[_schema.IndexOf(key)];
        return rng.NextFloat(spec.Min, spec.Max);
    }

    private List<PlatformGene> Grow(
        Pcg32 rng, bool mirrored, int count, int maxSize, int gridW, int gridH)
    {
        // Mirrored stages spend half the budget on the source half (reflection restores
        // the total); the legacy generator's target of 3 pre-mirror ≈ budget 6.
        int target = mirrored ? Math.Max(1, count / 2) : count;

        var all = new List<PlatformGene>();
        var stack = new Stack<PlatformGene>();
        PlatformGene initial = Initial(rng, mirrored, maxSize, gridW, gridH);
        all.Add(initial);
        stack.Push(initial);

        Span<Direction> directions = mirrored
            ? stackalloc[] { Direction.Left, Direction.Above, Direction.Below }
            : stackalloc[] { Direction.Left, Direction.Right, Direction.Above, Direction.Below };

        // The legacy tree died whenever the stack emptied (25% of stages were a lone
        // mirrored pair; budgets under-filled badly on tall maps). Re-seed from a
        // random existing platform instead, bounded so a saturated neighborhood
        // (nowhere left to place) still terminates. The budget stays a CAP — layouts
        // may end smaller when placement keeps failing, never larger.
        int popsLeft = target * 8;
        while (all.Count < target && popsLeft-- > 0)
        {
            if (stack.Count == 0)
            {
                stack.Push(all[rng.NextInt(all.Count)]);
            }
            PlatformGene parent = stack.Pop();
            foreach (Direction direction in directions)
            {
                if (all.Count >= target)
                {
                    break;
                }
                if (rng.NextInt(2) != 1)
                {
                    continue;
                }
                if (TryPlace(rng, direction, parent, all, mirrored, maxSize, gridW, gridH,
                        out PlatformGene child))
                {
                    all.Add(child);
                    stack.Push(child);
                }
            }
        }
        return all;
    }

    private PlatformGene Initial(Pcg32 rng, bool mirrored, int maxSize, int gridW, int gridH)
    {
        int ySize = rng.NextInt(MinThickness, MaxThickness + 1);
        // Start in the lower half of the visible box (legacy y was the fixed -3), with
        // the top capped at gridH − 1 so a spawn with headroom exists (DrawSpawn).
        int yLo = -gridH + 1;
        int y = rng.NextInt(yLo, Math.Max(yLo + 1, Math.Min(1, gridH - ySize)));
        int xSize = rng.NextInt(2, maxSize + 1);
        if (mirrored)
        {
            // Right edge lands within midGap of the axis (legacy parity: the seam gap
            // after mirroring is at most 2 · jumpLength/2 = jumpLength — hop-able).
            int rightEdge = -rng.NextInt(0, Math.Max(1, _jumpLength / 2) + 1);
            return new PlatformGene(rightEdge - xSize, y, xSize, ySize);
        }
        // A narrow map can be thinner than maxSize allows — the initial platform must
        // fit the visible box (children may extend past it, the seed cannot).
        xSize = Math.Min(xSize, 2 * gridW);
        int x = rng.NextInt(-gridW, gridW - xSize + 1);
        return new PlatformGene(x, y, xSize, ySize);
    }

    private enum Direction
    {
        Left,
        Right,
        Above,
        Below,
    }

    private bool TryPlace(
        Pcg32 rng, Direction direction, PlatformGene parent, List<PlatformGene> all,
        bool mirrored, int maxSize, int gridW, int gridH, out PlatformGene child)
    {
        for (int attempt = 0; attempt < PlacementAttempts; attempt++)
        {
            child = direction switch
            {
                Direction.Left => Beside(rng, parent, maxSize, left: true),
                Direction.Right => Beside(rng, parent, maxSize, left: false),
                Direction.Above => Above(rng, parent),
                _ => Below(rng, parent, maxSize),
            };
            if (IntersectsVisible(child, gridW, gridH)
                && (!mirrored || child.X + child.XSize <= 0)
                && !StageRules.OverlapsAny(child, all))
            {
                return true;
            }
        }
        child = default;
        return false;
    }

    /// <summary>Legacy Left, generalized to both sides: gap within jump reach, bottom
    /// within ±jumpHeight of the parent's.</summary>
    private PlatformGene Beside(Pcg32 rng, PlatformGene parent, int maxSize, bool left)
    {
        int y = rng.NextInt(parent.Y - _jumpHeight, parent.Y + _jumpHeight + 1);
        int ySize = rng.NextInt(MinThickness, MaxThickness + 1);
        int xSize = rng.NextInt(2, maxSize + 1);
        int gap = rng.NextInt(1, Math.Max(2, _jumpLength));
        int x = left ? parent.X - gap - xSize : parent.X + parent.XSize + gap;
        return new PlatformGene(x, y, xSize, ySize);
    }

    /// <summary>Legacy Above verbatim (parent-relative sizing fix preserved): rises
    /// 2..jumpHeight above the parent top, spans a sub-range of the parent.</summary>
    private PlatformGene Above(Pcg32 rng, PlatformGene parent)
    {
        int platformTop = parent.Y + parent.YSize;
        int y = platformTop + rng.NextInt(2, _jumpHeight + 1);
        int ySize = rng.NextInt(MinThickness, Math.Max(MinThickness + 1, Math.Min(MaxThickness, y - platformTop)));
        int x = rng.NextInt(parent.X + 1, parent.X + parent.XSize);
        int xSize = rng.NextInt(2, Math.Max(3, parent.XSize - (x - parent.X) + 1));
        return new PlatformGene(x, y, xSize, ySize);
    }

    /// <summary>New with Map Size: a platform whose TOP sits 1..jumpHeight below the
    /// parent's bottom (the way back up stays a single hop), horizontally within jump
    /// reach of the parent's span.</summary>
    private PlatformGene Below(Pcg32 rng, PlatformGene parent, int maxSize)
    {
        int drop = rng.NextInt(1, _jumpHeight + 1);
        int ySize = rng.NextInt(MinThickness, MaxThickness + 1);
        int y = parent.Y - drop - ySize;
        int xSize = rng.NextInt(2, maxSize + 1);
        int lo = parent.X - _jumpLength;
        int hi = Math.Max(lo + 1, parent.X + parent.XSize + _jumpLength - xSize + 1);
        int x = rng.NextInt(lo, hi);
        return new PlatformGene(x, y, xSize, ySize);
    }

    private static bool IntersectsVisible(in PlatformGene p, int gridW, int gridH) =>
        p.X < gridW && p.X + p.XSize > -gridW && p.Y < gridH && p.Y + p.YSize > -gridH;

    /// <summary>
    /// Draws a spawn over a platform, guaranteed inside the visible box (a spawn above
    /// it can sit outside the BLAST box on low-margin maps — death on tick one),
    /// body-clear of every platform, clear of already-placed spawns, and (mirrored
    /// stages) clear of the mirror axis. One draw picks the position fraction;
    /// starting from the drawn platform, candidates are scanned in deterministic ring
    /// order for a free spawn column (StageRules.TrySpawnOver). False = the layout has
    /// no safe column anywhere (the caller regrows).
    /// </summary>
    private static bool TryDrawSpawn(
        Pcg32 rng, List<PlatformGene> platforms, int index, float visW, float visH,
        IReadOnlyList<Vec2>? occupied, float axisClearHalfWidth, bool allowBare, out Vec2 spawn)
    {
        float fraction = rng.NextFloat();
        // Separation/axis clearance is BEST-EFFORT (2026-08-12): a layout whose only
        // columns violate them (narrow mirrored maps hug the axis) must still spawn
        // legally rather than fall through to the embed fallback — so when the caller
        // is out of regrow attempts (allowBare) scan constrained first, then bare.
        // Draw count is fixed either way (the fraction above).
        for (int pass = 0; pass < 2; pass++)
        {
            bool constrained = pass == 0;
            if (!constrained && (!allowBare || (occupied is null && axisClearHalfWidth <= 0f)))
            {
                break; // bare pass disallowed, or would repeat the constrained one
            }
            for (int offset = 0; offset < platforms.Count; offset++)
            {
                PlatformGene p = platforms[(index + offset) % platforms.Count];
                float preferredX = p.X + fraction * p.XSize;
                if (StageRules.TrySpawnOver(p, platforms, visW, visH, preferredX,
                        constrained ? occupied : null,
                        constrained ? axisClearHalfWidth : 0f) is { } spot)
                {
                    spawn = spot;
                    return true;
                }
            }
        }
        spawn = default;
        return false;
    }

    private static bool OnSamePlatform(Vec2 spawn, List<PlatformGene> platforms, int index)
    {
        PlatformGene p = platforms[index];
        return spawn.X >= p.X && spawn.X <= p.X + p.XSize;
    }
}
