using System.Text.Json;

namespace BrawlerSim.Backgrounds;

/// <summary>
/// Background selection tuning (backgrounds track Phase 1, 2026-09-02) — hot-editable
/// as godot/assets/background_selection.json (sibling of tile_selection.json, the
/// established pattern). Code defaults match the shipped file's starting values.
/// </summary>
public sealed record BackgroundSelectionConfig
{
    /// <summary>Softmax temperature over candidate scores. Lower = greedier.</summary>
    public float SoftmaxTemperature { get; init; } = 0.35f;

    /// <summary>No-monopoly rule: after shaping, no single entry may hold more than
    /// this probability mass in one pool (the brief's spread test: no entry above
    /// 2% of picks over 2,000 stages).</summary>
    public float MaxEntryShare { get; init; } = 0.02f;

    /// <summary>Repair rule floor (the SpriteId/ThemeId pattern): an inherited
    /// background whose trait affinity against the child's salient stage traits falls
    /// below this re-resolves. Only checked when the child HAS salient traits.</summary>
    public float RepairFloor { get; init; } = 0.05f;

    /// <summary>Register pools smaller than this fall back to the full library (the
    /// brief's rule, mirroring sprite selection: fey has no backgrounds, and the
    /// horror full-scene pool is 7 entries in the v0.2 corpus).</summary>
    public int RegisterPoolFloor { get; init; } = 20;

    /// <summary>Score subtracted per prior use of the same entry within one built
    /// game, so a four-stage lineup diverges.</summary>
    public float OverusePenalty { get; init; } = 0.5f;

    /// <summary>Ordered candidates sampled per selection.</summary>
    public int CandidateCount { get; init; } = 4;

    /// <summary>Within one built game, no two stages may sit closer than this in
    /// perceptual-descriptor distance (normalized L1 over the 8x8 Lab thumbnails;
    /// corpus nearest-neighbor median is ~0.046, so 0.05 filters near-duplicates
    /// without starving pools).</summary>
    public float DescriptorMinDistance { get; init; } = 0.05f;

    /// <summary>Palette harmony bonus when the (post-remap) background group equals
    /// the tile theme's group.</summary>
    public float HarmonySameBonus { get; init; } = 0.30f;

    /// <summary>Palette harmony bonus when the groups are adjacent per the remap
    /// transition table (either direction).</summary>
    public float HarmonyAdjacentBonus { get; init; } = 0.15f;

    /// <summary>Action-band busyness penalty: entries whose stored contrastBand
    /// metric exceeds the ceiling are penalized (the brief's value-collision term,
    /// adapted — the shipped theme library carries no per-theme value metrics, so the
    /// penalty is absolute busyness rather than theme-relative). Corpus full-role
    /// median is 18.9, p90 is 40.</summary>
    public float ContrastBandCeiling { get; init; } = 32f;

    /// <summary>Penalty weight per (contrastBand − ceiling)/ceiling of excess.</summary>
    public float ContrastPenaltyWeight { get; init; } = 0.25f;

    /// <summary>Slight preference for keeping an entry's native palette when a remap
    /// buys no extra harmony ("no remap" is always a candidate).</summary>
    public float RemapNoneBonus { get; init; } = 0.05f;

    /// <summary>Score penalty on style == "texture" entries (designer 2026-09-10:
    /// too much sameness — a third of stages drew flat texture-field backdrops).
    /// Scene art outranks texture walls wherever the pool offers any; pools that are
    /// ALL texture (the far role is 95% texture until the corpus grows) are
    /// unaffected, since softmax only sees relative scores.</summary>
    public float TextureStylePenalty { get; init; } = 0.25f;

    /// <summary>No-monopoly rule at the SOURCE level (designer 2026-09-03: cityscape
    /// packs were dominating): no single source pack may hold more than this
    /// probability mass in one pool — the per-entry cap cannot police family share
    /// when one pack contributes half the corpus.</summary>
    public float SourceShareCap { get; init; } = 0.25f;

    /// <summary>Maximum world-pixels-per-image-pixel a layer may stretch to (the
    /// policy-D design density is ~2.67 at 720p; 8 = three design steps). A layer
    /// that cannot cover the kill box within this density MIRROR-TILES to cover the
    /// entire camera space instead (designer 2026-09-03) — in the arena, the evolve
    /// preview, and the stage-select thumbs alike.</summary>
    public float LayerMaxScale { get; init; } = 8f;

    /// <summary>Tile-theme paletteGroup names that do not exist in the background
    /// group vocabulary, aliased for harmony scoring (art direction, tunable).</summary>
    public IReadOnlyDictionary<string, string> ThemeGroupAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["black"] = "night",
            ["bone"] = "grey",
        };

    /// <summary>Vertical band (fraction of the crop height) the entry's horizonY is
    /// anchored into when present — keeps the horizon near the arena's action band.</summary>
    public float HorizonBandMin { get; init; } = 0.35f;
    public float HorizonBandMax { get; init; } = 0.60f;

    /// <summary>Seeded per-stage brightness/contrast jitter half-widths (multipliers
    /// around 1.0), inside the pipeline's guardrail envelope.</summary>
    public float BrightnessJitter { get; init; } = 0.06f;
    public float ContrastJitter { get; init; } = 0.04f;

    /// <summary>Seeded per-stage tilt-shift blur strength multiplier range.</summary>
    public float BlurScaleMin { get; init; } = 0.8f;
    public float BlurScaleMax { get; init; } = 1.2f;

    /// <summary>Runtime dim multiplier over the (already pipeline-compressed)
    /// background — platforms and fighters must always win local contrast. The M-BG1
    /// coherence experiment validated 0.88 on bare compositions; the live arena adds
    /// a HUD band, so ship slightly darker.</summary>
    public float BaseDim { get; init; } = 0.85f;

    /// <summary>Maximum tilt-shift blur radius in SOURCE-texture pixels at full
    /// defocus (scaled by the variant's BlurScale; 0 inside the focal band).
    /// Intensified 2.5 -> 5.5 on 2026-09-10 (designer: sell the miniature harder).</summary>
    public float BlurMaxRadius { get; init; } = 5.5f;

    /// <summary>Extra value falloff at full defocus — the depth half of the
    /// tilt-shift read; backdrop layers only, never gameplay elements.</summary>
    public float OutOfBandDim { get; init; } = 0.18f;

    /// <summary>World-unit margin added around the platform envelope when the view
    /// derives the sharp focal band (tightened 1.5 -> 1.0 on 2026-09-10 so the
    /// sharp zone hugs the action).</summary>
    public float FocalMarginWorld { get; init; } = 1.0f;

    // ── Phase 2: parallax recombination (brief §Phase 2; remediation §2 made the
    // far + mid + near stack the DEFAULT — the single-image path is the extreme
    // fallback, so the old recombination probability is gone) ──────────────────

    /// <summary>The detail floor (remediation §3): every stack needs at least one
    /// large layer with metrics.detail at or above this (the far, or failing that a
    /// non-boxRisk mid); boxRisk fulls are excluded from the single path.</summary>
    public float DetailFloor { get; init; } = 0.25f;

    /// <summary>Score weight on metrics.detail — the softmax prefers detailed
    /// layers (remediation §3), on top of the hard floor above.</summary>
    public float DetailWeight { get; init; } = 0.3f;

    /// <summary>Seeded chance a recombining stage enters the GOOF LANE: the register
    /// -intersection predicate is waived (a nebula over a sunny meadow) and pair
    /// scoring PREFERS scene-tag distance. Predicates (b)/(c) and pairExclude hold
    /// in both lanes — a blocklist entry means broken, not funny.</summary>
    public float GoofBudget { get; init; } = 0.10f;

    /// <summary>Goof-lane score bonus per unit of scene-tag distance (1 = fully
    /// disjoint scene tags) — the budget buys maximum surrealism per slot.</summary>
    public float SceneDistanceBonus { get; init; } = 0.2f;

    /// <summary>Atmospheric-ordering tolerance on stored valMean: the far layer
    /// should read at least this close to as-light-as the mid; pairs that violate it
    /// raise the seam haze instead of being rejected (predicate c).</summary>
    public float AtmosphericEpsilon { get; init; } = 0.03f;

    /// <summary>Score bonus for pairs that satisfy atmospheric ordering outright.</summary>
    public float OrderingBonus { get; init; } = 0.1f;

    /// <summary>Parallax factor ranges per layer (seeded per stage; remediation §2
    /// bands: far &lt;= 0.15, mid 0.35-0.55, near &gt;= 0.75). Foreground = 1.</summary>
    public float FarFactorMin { get; init; } = 0.05f;
    public float FarFactorMax { get; init; } = 0.15f;
    public float MidFactorMin { get; init; } = 0.35f;
    public float MidFactorMax { get; init; } = 0.55f;

    /// <summary>Minimum parallax-factor spread between adjacent layers — under this
    /// two layers read as one plane (remediation §2). Layout clamps draws to it.</summary>
    public float LayerSpreadMin { get; init; } = 0.25f;

    /// <summary>Each layer's vertical parallax factor as a fraction of its
    /// horizontal one (remediation §2: y-factor = 0.6 x x-factor — a large
    /// readability win under a pan+zoom camera).</summary>
    public float VerticalParallaxRatio { get; init; } = 0.6f;

    /// <summary>The near bokeh layer (remediation §2 made it the mandatory third
    /// plane; the gene owns WHICH element): factor range, scale range (fraction of
    /// the kill-box height), and its opacity cap — sparse, heavily blurred.</summary>
    /// <remarks>Near elements must be DISCRETE props (planets, clouds, trees —
    /// roughly square): elements whose aspect ratio exceeds NearMaxAspect are scene
    /// strips or prop SHEETS and never join the bokeh plane (2026-09-03 — a
    /// full-width foreground slice read as a floating billboard).</remarks>
    public float NearMaxAspect { get; init; } = 1.6f;
    public float NearFactorMin { get; init; } = 0.75f;
    public float NearFactorMax { get; init; } = 1.3f;
    public float NearScaleMin { get; init; } = 0.12f;
    public float NearScaleMax { get; init; } = 0.28f;
    public float NearOpacity { get; init; } = 0.5f;

    /// <summary>Near opacity when the platform envelope reaches into the bokeh band:
    /// the plane stays (three layers minimum) but drops below readability — the
    /// remediation's "never over the platform envelope at readable opacity".</summary>
    public float NearOverActionOpacity { get; init; } = 0.18f;

    /// <summary>Seam haze strengths at the mid skyline: the base value, and the
    /// forced value when atmospheric ordering is violated (predicate c's remedy).</summary>
    public float SeamHazeBase { get; init; } = 0.25f;
    public float SeamHazeForced { get; init; } = 0.55f;

    /// <summary>Mid-layer blur as a fraction of the far layer's blur (far reads
    /// blurriest); the near layer's fixed heavy blur radius in texture pixels.</summary>
    public float MidBlurFraction { get; init; } = 0.45f;
    public float NearBlurRadius { get; init; } = 5f;

    public static readonly BackgroundSelectionConfig Default = new();

    public static BackgroundSelectionConfig Parse(string json) =>
        JsonSerializer.Deserialize<BackgroundSelectionConfig>(json, Options)
            ?? throw new JsonException("background selection config parsed to null.");

    public static BackgroundSelectionConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Tuning;
}
