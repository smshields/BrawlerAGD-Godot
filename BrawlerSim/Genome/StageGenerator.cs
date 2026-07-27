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

        // 2 + 3. Platform layout and spawns, with degenerate-layout retries.
        (List<PlatformGene> platforms, Vec2 spawn1, Vec2 spawn2) =
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

        (List<PlatformGene> platforms, Vec2 spawn1, Vec2 spawn2) =
            BuildLayoutAndSpawns(rng, mirrored, count, maxSize, gridW, gridH, visW, visH);
        return new StageGenome(platforms, stageParams.With(
            (StageParams.Spawn1X, spawn1.X), (StageParams.Spawn1Y, spawn1.Y),
            (StageParams.Spawn2X, spawn2.X), (StageParams.Spawn2Y, spawn2.Y)));
    }

    /// <summary>How many times a degenerate layout (no body-safe spawn column
    /// anywhere) is regrown before the embed fallback is accepted.</summary>
    public const int LayoutAttempts = 4;

    /// <summary>
    /// Grows a layout and draws both spawns, regrowing (bounded) when the layout has
    /// no body-safe spawn column — dense wall-clusters would otherwise ship embedded
    /// spawns that the axis-clamp physics ejects (seed-152 probe, 2026-07-21).
    /// Mirrored stages take spawn 2 as spawn 1's exact mirror (fair by symmetry;
    /// legal by symmetry — no repair pass, whose left-biased tie-breaking would stack
    /// both players on one column). Asymmetric stages draw independently on a
    /// forced-distinct platform when more than one exists.
    /// </summary>
    private (List<PlatformGene> Platforms, Vec2 Spawn1, Vec2 Spawn2) BuildLayoutAndSpawns(
        Pcg32 rng, bool mirrored, int count, int maxSize, int gridW, int gridH, float visW, float visH)
    {
        List<PlatformGene> platforms = null!;
        for (int attempt = 0; attempt < LayoutAttempts; attempt++)
        {
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

            if (!TryDrawSpawn(rng, platforms, rng.NextInt(platforms.Count), visW, visH, out Vec2 spawn1))
            {
                continue; // degenerate layout — regrow
            }
            if (mirrored)
            {
                return (platforms, spawn1, new Vec2(-spawn1.X, spawn1.Y));
            }
            int index = rng.NextInt(platforms.Count);
            if (platforms.Count > 1 && OnSamePlatform(spawn1, platforms, index))
            {
                index = (index + 1) % platforms.Count; // meaningful separation, no extra draw
            }
            if (TryDrawSpawn(rng, platforms, index, visW, visH, out Vec2 spawn2))
            {
                return (platforms, spawn1, spawn2);
            }
        }
        // Every attempt degenerate: accept the embed fallback on the last layout —
        // physics depenetration resolves it; extraordinarily rare by construction.
        Vec2 fallback = StageRules.RepairSpawn(new Vec2(0f, visH), platforms, visW, visH);
        return (platforms, fallback,
            mirrored ? new Vec2(-fallback.X, fallback.Y) : fallback);
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
    /// it can sit outside the BLAST box on low-margin maps — death on tick one) and
    /// body-clear of every platform. One draw picks the position fraction; starting
    /// from the drawn platform, candidates are scanned in deterministic ring order for
    /// a free spawn column (StageRules.TrySpawnOver). False = the layout has no safe
    /// column anywhere (the caller regrows).
    /// </summary>
    private static bool TryDrawSpawn(
        Pcg32 rng, List<PlatformGene> platforms, int index, float visW, float visH, out Vec2 spawn)
    {
        float fraction = rng.NextFloat();
        for (int offset = 0; offset < platforms.Count; offset++)
        {
            PlatformGene p = platforms[(index + offset) % platforms.Count];
            float preferredX = p.X + fraction * p.XSize;
            if (StageRules.TrySpawnOver(p, platforms, visW, visH, preferredX) is { } spot)
            {
                spawn = spot;
                return true;
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
