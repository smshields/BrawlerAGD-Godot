using System.Text.Json;

namespace BrawlerSim.Weather;

/// <summary>One per-layer emitter row of a preset. Density and opacityCap are the
/// budgeted quantities; sizeScale is authored depth dressing; the on-screen SPEED
/// multiplier comes from the layer's parallax factor at plan time (the brief's
/// depth-consistency rule), times the optional data speedScale.</summary>
public sealed record WeatherLayerRow(
    string Layer,          // "far" | "mid" | "action" | "near"
    float Density,
    float SizeScale,
    float SpeedScale,
    float OpacityCap,
    float Blur);

/// <summary>One weather preset: a material family x palette ramp x physics envelope
/// (blood rain = the rain family on the red ramp, gated horror+deadly — one table
/// row, not a separate effect).</summary>
public sealed record WeatherPreset(
    string Name,
    string Type,           // rain | snow | ash | embers | dust | fog | petals | sand | lightning
    string PaletteRamp,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> Registers,          // empty = any register
    IReadOnlyList<string> RequiresTraits,     // every one must be salient (blood: deadly)
    Envelope BaseDeg, Envelope DriftDegPerMin, Envelope OscAmpDeg, Envelope OscPeriodS,
    Envelope Speed, Envelope SpeedChangeEveryS, Envelope SpeedTweenDurS, IReadOnlyList<string> SpeedEasing,
    Envelope Intensity, Envelope IntensityChangeEveryS, Envelope IntensityTweenDurS, IReadOnlyList<string> IntensityEasing,
    bool Persistent, Envelope EpisodeDurS, Envelope EpisodeGapS,
    float RampInS, float RampOutS, string EpisodeEasing,
    float LayerPhaseOffsetS,
    IReadOnlyList<WeatherLayerRow> Layers);

/// <summary>
/// The weather tuning file (godot/assets/weather_presets.json, hot-editable): the
/// preset table, the trait → preset mapping, the clear-stage probability (weather is
/// seasoning: 60-75% of stages clear), and the global readability budget. Lightning
/// ships FLAG-DISABLED: full-screen luminance belongs to the M5 shader track.
/// </summary>
public sealed class WeatherConfig
{
    public float ClearProbability { get; init; } = 0.675f;
    public float SecondInstanceProbability { get; init; } = 0.30f;

    /// <summary>Global opacity x density budget across ALL layers of ALL instances,
    /// enforced from the schedules' static maxima at load (clamp + a debug note).</summary>
    public float GlobalBudget { get; init; } = 0.9f;

    /// <summary>The action band's share, capped hardest (the fighters' row).</summary>
    public float ActionBandBudget { get; init; } = 0.12f;

    /// <summary>The near/bokeh row's parallax factor (far/mid come from the stage's
    /// background layout; action is 1 by definition).</summary>
    public float NearFactor { get; init; } = 1.3f;

    /// <summary>M5 gate: lightning-class full-screen luminance stays off until the
    /// shader track settles flash vocabulary. The scheduler hook exists either way.</summary>
    public bool LightningEnabled { get; init; } = false;

    public IReadOnlyList<WeatherPreset> Presets { get; init; } = Array.Empty<WeatherPreset>();

    public static WeatherConfig Parse(string json)
    {
        Doc doc = JsonSerializer.Deserialize<Doc>(json, Options)
            ?? throw new JsonException("weather config parsed to null.");
        var presets = new List<WeatherPreset>();
        foreach (PresetDoc p in doc.Presets ?? new List<PresetDoc>())
        {
            var preset = new WeatherPreset(
                p.Name ?? throw new JsonException("weather preset without a name"),
                p.Type ?? "rain",
                p.PaletteRamp ?? "grey",
                p.Traits ?? new List<string>(),
                p.Registers ?? new List<string>(),
                p.RequiresTraits ?? new List<string>(),
                Env(p.BaseDeg, 250f, 290f),
                Env(p.DriftDegPerMin, 0f, 12f),
                Env(p.OscAmpDeg, 4f, 18f),
                Env(p.OscPeriodS, 6f, 14f),
                Env(p.Speed, 40f, 160f),
                Env(p.SpeedChangeEveryS, 4f, 20f),
                Env(p.SpeedTweenDurS, 1.5f, 6f),
                p.SpeedEasing ?? new List<string> { "sineInOut" },
                Env(p.Intensity, 0.3f, 1f),
                Env(p.IntensityChangeEveryS, 10f, 45f),
                Env(p.IntensityTweenDurS, 2f, 8f),
                p.IntensityEasing ?? new List<string> { "sineInOut" },
                p.Persistent ?? true,
                Env(p.EpisodeDurS, 20f, 70f),
                Env(p.EpisodeGapS, 8f, 40f),
                p.RampInS ?? 4f,
                p.RampOutS ?? 6f,
                p.EpisodeEasing ?? "cubicOut",
                p.LayerPhaseOffsetS ?? 0.8f,
                (p.Layers ?? new List<LayerDoc>()).Select(l => new WeatherLayerRow(
                    l.Layer ?? "mid", l.Density ?? 0.3f, l.SizeScale ?? 1f,
                    l.SpeedScale ?? 1f, l.OpacityCap ?? 0.35f, l.Blur ?? 0f)).ToList());
            foreach (string easing in preset.SpeedEasing.Concat(preset.IntensityEasing)
                .Append(preset.EpisodeEasing))
            {
                if (!EasingLibrary.Contains(easing))
                {
                    throw new JsonException($"preset {preset.Name}: unknown easing '{easing}'");
                }
            }
            presets.Add(preset);
        }
        return new WeatherConfig
        {
            ClearProbability = doc.ClearProbability ?? 0.675f,
            SecondInstanceProbability = doc.SecondInstanceProbability ?? 0.30f,
            GlobalBudget = doc.GlobalBudget ?? 0.9f,
            ActionBandBudget = doc.ActionBandBudget ?? 0.12f,
            NearFactor = doc.NearFactor ?? 1.3f,
            LightningEnabled = doc.LightningEnabled ?? false,
            Presets = presets,
        };
    }

    public static WeatherConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private static Envelope Env(List<float>? pair, float min, float max) =>
        pair is { Count: 2 } ? new Envelope(pair[0], pair[1]) : new Envelope(min, max);

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Tuning;

    private sealed class Doc
    {
        public float? ClearProbability { get; set; }
        public float? SecondInstanceProbability { get; set; }
        public float? GlobalBudget { get; set; }
        public float? ActionBandBudget { get; set; }
        public float? NearFactor { get; set; }
        public bool? LightningEnabled { get; set; }
        public List<PresetDoc>? Presets { get; set; }
    }

    private sealed class PresetDoc
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? PaletteRamp { get; set; }
        public List<string>? Traits { get; set; }
        public List<string>? Registers { get; set; }
        public List<string>? RequiresTraits { get; set; }
        public List<float>? BaseDeg { get; set; }
        public List<float>? DriftDegPerMin { get; set; }
        public List<float>? OscAmpDeg { get; set; }
        public List<float>? OscPeriodS { get; set; }
        public List<float>? Speed { get; set; }
        public List<float>? SpeedChangeEveryS { get; set; }
        public List<float>? SpeedTweenDurS { get; set; }
        public List<string>? SpeedEasing { get; set; }
        public List<float>? Intensity { get; set; }
        public List<float>? IntensityChangeEveryS { get; set; }
        public List<float>? IntensityTweenDurS { get; set; }
        public List<string>? IntensityEasing { get; set; }
        public bool? Persistent { get; set; }
        public List<float>? EpisodeDurS { get; set; }
        public List<float>? EpisodeGapS { get; set; }
        public float? RampInS { get; set; }
        public float? RampOutS { get; set; }
        public string? EpisodeEasing { get; set; }
        public float? LayerPhaseOffsetS { get; set; }
        public List<LayerDoc>? Layers { get; set; }
    }

    private sealed class LayerDoc
    {
        public string? Layer { get; set; }
        public float? Density { get; set; }
        public float? SizeScale { get; set; }
        public float? SpeedScale { get; set; }
        public float? OpacityCap { get; set; }
        public float? Blur { get; set; }
    }
}
