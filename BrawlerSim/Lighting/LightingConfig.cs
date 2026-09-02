using System.Text.Json;

namespace BrawlerSim.Lighting;

/// <summary>
/// Stage light rig tuning (backgrounds track Phase 4, 2026-09-02 — hot-editable as
/// godot/assets/lighting.json). Hard constraint from the style spec: runtime lighting
/// is TINT AND ADDITIVE ACCENT ONLY — the baked upper-left key light is never
/// re-shaded, no normal maps, and near-black outlines are never tinted (the
/// readability contract). Code defaults match the shipped file.
/// </summary>
public sealed record LightingConfig
{
    /// <summary>How far tile pixels multiply toward the ambient color.</summary>
    public float AmbientStrength { get; init; } = 0.35f;

    /// <summary>How far BRIGHT tile pixels (the lit walking caps under the baked
    /// key light) shift toward the cap-light color — "platforms lit by the sky".</summary>
    public float CapTintStrength { get; init; } = 0.45f;

    /// <summary>Fighters' global ambient tint cap (working number 0.15): identity
    /// outlines and accent bands never muddy.</summary>
    public float FighterTintCap { get; init; } = 0.15f;

    /// <summary>Pixels whose max channel sits at or below this (0-255) are the
    /// near-black outline range — exempt from ALL tinting, bit-identical.</summary>
    public int OutlineValueMax { get; init; } = 40;

    /// <summary>The luminance band (0-1) that reads as a lit cap: the cap shift
    /// ramps in between these knees.</summary>
    public float CapLumKneeLow { get; init; } = 0.55f;
    public float CapLumKneeHigh { get; init; } = 0.85f;

    /// <summary>The action-band contrast floor between lit caps and the (dimmed)
    /// backdrop; the rig scales itself back until it passes (never below what the
    /// rig-off baseline could achieve — the dim layer owns base separation).</summary>
    public float ContrastFloor { get; init; } = 0.16f;

    /// <summary>The runtime dim the backdrop renders under (mirror of
    /// background_selection.json baseDim; used only for the contrast estimate).</summary>
    public float AssumedBackdropDim { get; init; } = 0.85f;

    /// <summary>The typical lit-cap luminance of the tile art (2-3 value steps,
    /// caps at the top) the clamp measures against.</summary>
    public float CapBaseLum { get; init; } = 0.82f;

    /// <summary>Point accents: max additive glows per stage and their intensity.</summary>
    public int AccentBudget { get; init; } = 3;
    public float AccentIntensity { get; init; } = 0.4f;

    /// <summary>Weather may nudge ambient intensity within this amplitude (embers
    /// warm it, rain/ash darken it); the rig is otherwise static during play.</summary>
    public float WeatherAmplitudeCap { get; init; } = 0.08f;

    public static readonly LightingConfig Default = new();

    public static LightingConfig Parse(string json) =>
        JsonSerializer.Deserialize<LightingConfig>(json, Serialization.JsonOptions.Tuning)
            ?? throw new JsonException("lighting config parsed to null.");

    public static LightingConfig LoadFile(string path) => Parse(File.ReadAllText(path));
}
