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

    public static readonly BackgroundSelectionConfig Default = new();

    public static BackgroundSelectionConfig Parse(string json) =>
        JsonSerializer.Deserialize<BackgroundSelectionConfig>(json, Options)
            ?? throw new JsonException("background selection config parsed to null.");

    public static BackgroundSelectionConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Tuning;
}
