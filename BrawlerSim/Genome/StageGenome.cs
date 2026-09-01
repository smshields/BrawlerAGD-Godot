using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>One platform: integer grid rect, position = bottom-left corner.
/// Thin (2026-09-01, FEATURES.md §Thin Platforms): a drop-through platform — solid
/// only when landed on from above; crouch-drops, upward and sideways motion pass
/// through. A structural gene (defaulted false, so pre-v12 files load solid); the
/// gene rect still reserves the full cell for placement/overlap rules, while the sim
/// collides with only a thin top slice (MatchConfig.ThinPlatformThickness).</summary>
public readonly record struct PlatformGene(int X, int Y, int XSize, int YSize, bool Thin = false)
{
    /// <summary>Mirror across x = 0 (Unity Platform.xMirror parity).</summary>
    public PlatformGene MirrorX() => this with { X = -X - XSize };
}

/// <summary>
/// The stage segment of a game genome: an ordered platform list plus, since Map Size
/// (2026-07-21, docs/features/map-size.md), a stage ParamSet — map dimensions, KO
/// margin, symmetry, and spawn genes. The params-less constructor binds the legacy
/// dimensions and derived spawns, which is what keeps pre-v7 files bit-identical.
/// </summary>
public sealed class StageGenome
{
    private readonly PlatformGene[] _platforms;

    public ParamSet Params { get; }

    /// <summary>Semantic stage theme gene (2026-09-01, M4d —
    /// docs/features/stage-tile-selection.md): an id into the tiles_v2 theme library.
    /// A structural gene exactly like CharacterGenome.SpriteId — children inherit a
    /// parent's look (50/50 coin at crossover), repaired only when it stops making
    /// sense against the child's salient stage traits. Null on pre-v13 files and when
    /// generation runs without a theme library; null renders the legacy v1 tiles.</summary>
    public string? ThemeId { get; }

    public StageGenome(IEnumerable<PlatformGene> platforms)
        : this(platforms, null)
    {
    }

    public StageGenome(IEnumerable<PlatformGene> platforms, ParamSet? stageParams,
        string? themeId = null)
    {
        _platforms = platforms.ToArray();
        if (_platforms.Length == 0)
        {
            throw new ArgumentException("A stage must have at least one platform.");
        }
        Params = stageParams ?? StageRules.LegacyParams(_platforms);
        ThemeId = themeId;
    }

    /// <summary>Copy with a different theme gene (selection/repair).</summary>
    public StageGenome WithThemeId(string? themeId) =>
        themeId == ThemeId ? this : new StageGenome(_platforms, Params, themeId);

    public IReadOnlyList<PlatformGene> Platforms => _platforms;

    /// <summary>
    /// Single-point crossover, Unity parity for the platform lists: point is drawn in
    /// [0, min(lenA, lenB)); the child is a's platforms before the point followed by
    /// b's platforms from the point onward (child length = b's length unless a is
    /// shorter). Since Map Size the stage params cross FIRST (standard single-point op)
    /// — draw order is part of the RNG stream contract (fingerprint golden).
    /// </summary>
    public static StageGenome SinglePointCrossover(StageGenome a, StageGenome b, Pcg32 rng)
    {
        ParamSet childParams = GenomeOps.SinglePointCrossover(a.Params, b.Params, rng);
        int point = rng.NextInt(Math.Min(a._platforms.Length, b._platforms.Length));
        var child = new List<PlatformGene>(point + b._platforms.Length - point);
        for (int i = 0; i < point; i++)
        {
            child.Add(a._platforms[i]);
        }
        for (int i = point; i < b._platforms.Length; i++)
        {
            child.Add(b._platforms[i]);
        }
        // Theme gene (2026-09-01, M4d): a 50/50 parent coin, the SpriteId pattern —
        // but the draw is RNG-GATED on a theme actually existing (theme-less
        // populations, including every pre-tile run, keep their stream bit-exact).
        string? themeId = a.ThemeId ?? b.ThemeId;
        if (a.ThemeId is not null && b.ThemeId is not null)
        {
            themeId = rng.NextInt(2) == 0 ? a.ThemeId : b.ThemeId;
        }
        // Platforms legal under a parent's box genes may violate the CHILD's playable
        // box (2026-08-13, designer containment rule) — clamp them in, then repair the
        // spawn genes against the repaired layout. Identity for legal stages; never
        // runs at sim time.
        child = StageRules.RepairPlatforms(child, childParams);
        return new StageGenome(child, StageRules.RepairSpawns(child, childParams), themeId);
    }
}
