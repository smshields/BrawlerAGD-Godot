using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Derived stage values and post-generation constraints for the Map Size feature
/// (2026-07-21, FEATURES.md §Map Size; docs/features/map-size.md) — the stage
/// counterpart of MoveRules. Everything here is pure and deterministic: it runs at
/// genome load and SimWorld construction, so it is part of the replay contract.
/// </summary>
public static partial class StageRules
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

    // ── The playable box (2026-08-13, designer: Smash-style readable stages) ─────────
    // Every platform must sit COMPLETELY inside the kill (blast) box — any part beyond
    // a kill line is lethal, invisible ground — and the LOWEST platform must clear the
    // bottom kill line by enough that it stays readable above the HUD band at the
    // camera's widest zoom. The clearance is DERIVED PER MAP (designer): HUD fraction
    // × the map's maximum camera view height (full view inside the kill box, 16:9).

    /// <summary>The fixed 16:9 view aspect (AESTHETICS: 1280×720 design viewport).</summary>
    public const float ViewAspect = 16f / 9f;

    /// <summary>Fraction of the design view the bottom HUD band occupies: margin 8 +
    /// panel 100 + gap 4 + debug strip 62 = 174 of 720 px. Mirrors
    /// HudView.ReservedBottomPixels (godot/) — update BOTH if the HUD layout changes.</summary>
    public const float HudBottomFraction = 174f / 720f;

    /// <summary>The reserved no-platform gap above the bottom kill line for a map with
    /// these blast half extents: the HUD band's world height when the camera is at its
    /// widest legal zoom (view height capped by the kill box on BOTH axes at 16:9).</summary>
    public static float BottomClearance(float blastHalfX, float blastHalfY) =>
        HudBottomFraction * 2f * MathF.Min(blastHalfY, blastHalfX / ViewAspect);

    /// <summary>The rectangle platforms must lie completely inside, for a stage
    /// ParamSet: the blast box shrunk at the bottom by the readability clearance.</summary>
    public static (Vec2 Min, Vec2 Max) PlayableBox(ParamSet stageParams)
    {
        Vec2 blast = BlastHalfExtents(stageParams);
        return PlayableBoxFrom(blast.X, blast.Y);
    }

    /// <summary>Raw-gene variant for the generator, which draws its structure genes
    /// before the ParamSet exists.</summary>
    public static (Vec2 Min, Vec2 Max) PlayableBoxFrom(float blastHalfX, float blastHalfY) =>
        (new Vec2(-blastHalfX, -blastHalfY + BottomClearance(blastHalfX, blastHalfY)),
            new Vec2(blastHalfX, blastHalfY));

    public static bool PlatformInPlayableBox(in PlatformGene p, Vec2 min, Vec2 max) =>
        p.X >= min.X && p.X + p.XSize <= max.X && p.Y >= min.Y && p.Y + p.YSize <= max.Y;

    /// <summary>
    /// Genetic-ops repair (crossover mixes platforms across parents whose box genes
    /// differ): size-clamp then position-clamp each platform into the CHILD's playable
    /// box. Deterministic and the identity for already-legal platforms; stored games
    /// are never touched at sim time (same contract as RepairSpawns). A clamp can in
    /// rare cases introduce an overlap — the same tolerance crossover always had for
    /// mixed platform lists.
    /// </summary>
    public static List<PlatformGene> RepairPlatforms(
        IReadOnlyList<PlatformGene> platforms, ParamSet stageParams)
    {
        (Vec2 min, Vec2 max) = PlayableBox(stageParams);
        var repaired = new List<PlatformGene>(platforms.Count);
        foreach (PlatformGene p in platforms)
        {
            PlatformGene fixedUp = p;
            // Size-clamp to the INTEGER-FEASIBLE span (2026-08-17): platform coords are
            // integers, so the usable width is floor(max)-ceil(min), not the raw box
            // width. The old raw-width clamp let an 11-wide platform survive an
            // 11.96-wide box where no integer position contains it — the position
            // clamp below then parked it at loX sticking out the far side (the one
            // containment leak in 1,600 audited breedings, all pure-crossover).
            int maxXSize = Math.Max(1, (int)MathF.Floor(max.X) - (int)MathF.Ceiling(min.X));
            int maxYSize = Math.Max(1, (int)MathF.Floor(max.Y) - (int)MathF.Ceiling(min.Y));
            if (fixedUp.XSize > maxXSize)
            {
                fixedUp = fixedUp with { XSize = maxXSize };
            }
            if (fixedUp.YSize > maxYSize)
            {
                fixedUp = fixedUp with { YSize = maxYSize };
            }
            int loX = (int)MathF.Ceiling(min.X);
            int hiX = (int)MathF.Floor(max.X) - fixedUp.XSize;
            int loY = (int)MathF.Ceiling(min.Y);
            int hiY = (int)MathF.Floor(max.Y) - fixedUp.YSize;
            fixedUp = fixedUp with
            {
                X = Math.Clamp(fixedUp.X, loX, Math.Max(loX, hiX)),
                Y = Math.Clamp(fixedUp.Y, loY, Math.Max(loY, hiY)),
            };
            repaired.Add(fixedUp);
        }
        // Thin Platforms (2026-09-01): crossover may combine two thin-heavy lists
        // into an all-thin child — the at-least-one-solid rule repairs it here.
        return EnsureSolidPlatform(repaired);
    }

    public static bool IsMirrored(ParamSet stageParams) =>
        stageParams.Get(StageParams.Mirrored) >= 0.5f;

    /// <summary>false = left half is the mirror source, true = right.</summary>
    public static bool MirrorSideRight(ParamSet stageParams) =>
        stageParams.Get(StageParams.MirrorSide) >= 0.5f;

    // The int-gene clamp bounds, shared with the DefaultSchemas generation ranges.
    // DELIBERATELY not driven by per-run range overrides: an override widens what
    // evolution may DRAW, while these bound what a stage may BE (out-of-clamp genes
    // saturate, exactly as any beyond-domain gene does).
    public const int PlatformCountMin = 2;
    public const int PlatformCountMax = 16;
    public const int MaxPlatformSizeMin = 3;
    public const int MaxPlatformSizeMax = 14;

    public static int PlatformCountOf(ParamSet stageParams) =>
        IntGene(stageParams.Get(StageParams.PlatformCount), PlatformCountMin, PlatformCountMax);

    public static int MaxPlatformSizeOf(ParamSet stageParams) =>
        IntGene(stageParams.Get(StageParams.MaxPlatformSize), MaxPlatformSizeMin, MaxPlatformSizeMax);

    /// <summary>The generator's layout grid: one cell per world unit of the visible
    /// half extents, floored, with 3×2 minimums. One home for what was derived
    /// independently in Generate and Regenerate.</summary>
    public static (int W, int H) GridExtents(float visibleHalfWidth, float visibleHalfHeight) =>
        (Math.Max(3, (int)MathF.Floor(visibleHalfWidth)),
         Math.Max(2, (int)MathF.Floor(visibleHalfHeight)));

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
    /// it play bit-identically to the pre-feature sim (spawns 3/4 are new genes the
    /// 2P sim never reads — see DeriveExtraSpawns). The schema parameter exists for
    /// range-override runs, whose genomes must all bind the run's rebuilt schema
    /// instance (GenomeOps.RequireSameSchema).</summary>
    public static ParamSet LegacyParams(IReadOnlyList<PlatformGene> platforms, ParamSchema? schema = null)
    {
        Vec2 spawn1 = DeriveLegacySpawn(platforms);
        Vec2 spawn2 = LegacySafeSpawn(new Vec2(-spawn1.X, spawn1.Y), platforms);
        (Vec2 spawn3, Vec2 spawn4) = DeriveExtraSpawns(
            platforms, spawn1, spawn2, LegacyVisibleHalfWidth, LegacyVisibleHalfHeight);
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
            spawn3.X, spawn3.Y,
            spawn4.X, spawn4.Y,
            0f, // thinPlatformFraction — all-solid (2026-09-01, pre-v12 parity)
        });
    }

    /// <summary>
    /// Deterministic spawns 3/4 for stages that predate the four-spawn rule
    /// (2026-08-12, docs/features/four-player.md): both prefer the stage center at
    /// spawn 1's height, and the repair's occupied blocking pushes each to the free
    /// column nearest that preference — spawn 3 clear of spawns 1/2, spawn 4 clear of
    /// all three. Only ever read by 3/4-player matches, so pre-v9 2P artifacts replay
    /// bit-identically regardless of what this derives.
    /// </summary>
    public static (Vec2 Spawn3, Vec2 Spawn4) DeriveExtraSpawns(
        IReadOnlyList<PlatformGene> platforms, Vec2 spawn1, Vec2 spawn2, float visW, float visH)
    {
        Vec2 preferred = new(0f, spawn1.Y);
        Vec2 spawn3 = RepairSpawn(preferred, platforms, visW, visH, new[] { spawn1, spawn2 });
        Vec2 spawn4 = RepairSpawn(preferred, platforms, visW, visH, new[] { spawn1, spawn2, spawn3 });
        return (spawn3, spawn4);
    }

    /// <summary>Thin Platforms (2026-09-01): the generation fraction gene, clamped to
    /// [0, 1] (range overrides may exceed it — the coin only needs a probability).</summary>
    public static float ThinFractionOf(ParamSet stageParams) =>
        Math.Clamp(stageParams.Get(StageParams.ThinPlatformFraction), 0f, 1f);

    /// <summary>
    /// The at-least-one-solid rule (2026-09-01, designer: the thin fraction may run
    /// very high SO LONG AS one solid platform is preserved). Generation guarantees it
    /// structurally (the initial platform never rolls thin); this repair covers the
    /// breeding products that can lose it — a crossover mixing thin-heavy lists, or a
    /// mirror transform whose source half held no solid platform. Deterministic and
    /// RNG-free: when no platform is solid, the WIDEST one flips solid (ties → first
    /// in list order — the main-stage read), together with its exact mirror twin when
    /// one exists so symmetric layouts stay symmetric. Identity when a solid exists.
    /// </summary>
    public static List<PlatformGene> EnsureSolidPlatform(List<PlatformGene> platforms)
    {
        int widest = -1;
        for (int i = 0; i < platforms.Count; i++)
        {
            if (!platforms[i].Thin)
            {
                return platforms;
            }
            if (widest < 0 || platforms[i].XSize > platforms[widest].XSize)
            {
                widest = i;
            }
        }
        if (widest < 0)
        {
            return platforms;
        }
        PlatformGene mirrorTwin = platforms[widest].MirrorX();
        platforms[widest] = platforms[widest] with { Thin = false };
        for (int i = 0; i < platforms.Count; i++)
        {
            if (i != widest && platforms[i] == mirrorTwin)
            {
                platforms[i] = platforms[i] with { Thin = false };
                break;
            }
        }
        return platforms;
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
}
