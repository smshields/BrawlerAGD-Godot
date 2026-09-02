using System.Text.Json;

namespace BrawlerSim.Backgrounds;

/// <summary>
/// The policy-D palette data + the engine-side paletteGroup remap transform
/// (backgrounds track, 2026-09-02). The handoff ships master_palette.json (64),
/// background_ramps_bidir.json (311 bidirectional extensions) and
/// remap_transition_table.json, but NOT the brief's named per-group LUT file — so the
/// remap is DERIVED here as a pure color transform: re-anchor the color's hue offset
/// (relative to the entry's source paletteGroup hue center) onto the target group's
/// center, compressed; grey/night are sat/val transforms instead; every result snaps
/// to the nearest master+ramp pool color so remapped pixels stay policy-D coherent.
/// Deterministic everywhere: pure function of (rgb, sourceGroup, targetGroup, pool).
/// Per-entry viable targets remain the index's `remaps` lists (pipeline-validated);
/// this class only says WHAT a target looks like, never WHETHER it is legal.
/// Prototype proof: scratchpad remap_proof.png, reviewed 2026-09-02.
/// </summary>
public sealed class BackgroundPalette
{
    /// <summary>Fraction of the source hue offset that survives re-anchoring — keeps
    /// accent hues (a warm sun in a blue sky) distinct without leaving the target family.</summary>
    private const double HueCompress = 0.35;

    /// <summary>Colors below this saturation stay untouched by hue re-anchors
    /// (protects outlines and neutral greys — the readability contract).</summary>
    private const double NeutralSat = 0.06;

    private readonly (byte R, byte G, byte B)[] _pool;
    private readonly Dictionary<string, IReadOnlyList<string>> _transitions;

    /// <summary>Hue centers (degrees) of the named paletteGroups. Grey and night are
    /// handled as transforms, but night still carries an anchor hue.</summary>
    private static readonly Dictionary<string, double> HueCenter = new(StringComparer.Ordinal)
    {
        ["red"] = 0, ["orange"] = 28, ["gold"] = 48, ["green"] = 120,
        ["teal"] = 172, ["blue"] = 218, ["violet"] = 278, ["night"] = 230,
    };

    private BackgroundPalette((byte, byte, byte)[] pool,
        Dictionary<string, IReadOnlyList<string>> transitions)
    {
        _pool = pool;
        _transitions = transitions;
    }

    /// <summary>Allowed remap targets for a source paletteGroup (art direction,
    /// QA-tunable data). Unknown source groups (an entry tagged outside the table)
    /// may remap nowhere.</summary>
    public IReadOnlyList<string> TransitionsFor(string sourceGroup) =>
        _transitions.TryGetValue(sourceGroup, out IReadOnlyList<string>? t)
            ? t
            : Array.Empty<string>();

    /// <summary>Remap one RGB color from its entry's source group toward a target
    /// group, snapped to the master+ramp pool.</summary>
    public (byte R, byte G, byte B) RemapColor(
        (byte R, byte G, byte B) color, string sourceGroup, string targetGroup)
    {
        (double h, double s, double v) = RgbToHsv(color);
        (double h2, double s2, double v2) = targetGroup switch
        {
            "grey" => (h, s * 0.08, v),
            "night" => (
                Wrap(HueCenter["night"] / 360.0
                    + Offset(h, sourceGroup) * HueCompress),
                Math.Min(s * 0.6 + 0.08, 1.0),
                v * 0.5 + 0.03),
            _ when s < NeutralSat => (h, s, v),
            _ => (
                Wrap(HueCenter.GetValueOrDefault(targetGroup, h * 360.0) / 360.0
                    + Offset(h, sourceGroup) * HueCompress),
                s,
                v),
        };
        return Snap(HsvToRgb(h2, s2, v2));
    }

    /// <summary>The dominant light color of an entry AFTER a remap (Phase 4's light
    /// rig reads this; "none" remap returns the stored color unchanged).</summary>
    public (byte R, byte G, byte B) RemapDomLight(BackgroundEntry entry, string? targetGroup)
    {
        var dom = (
            (byte)Math.Clamp(entry.Metrics.DomLightColor[0], 0, 255),
            (byte)Math.Clamp(entry.Metrics.DomLightColor[1], 0, 255),
            (byte)Math.Clamp(entry.Metrics.DomLightColor[2], 0, 255));
        return targetGroup is null ? dom : RemapColor(dom, entry.PaletteGroup, targetGroup);
    }

    private static double Offset(double h, string sourceGroup)
    {
        double src = HueCenter.GetValueOrDefault(sourceGroup, h * 360.0) / 360.0;
        double d = h - src + 0.5;
        return d - Math.Floor(d) - 0.5;
    }

    private static double Wrap(double h) => h - Math.Floor(h);

    /// <summary>Nearest pool color by green-weighted squared RGB distance (cheap,
    /// deterministic, matches the reviewed prototype).</summary>
    private (byte, byte, byte) Snap((byte R, byte G, byte B) c)
    {
        (byte, byte, byte) best = _pool[0];
        long bd = long.MaxValue;
        foreach ((byte r, byte g, byte b) in _pool)
        {
            long dr = c.R - r;
            long dg = c.G - g;
            long db = c.B - b;
            long d = 3 * dr * dr + 6 * dg * dg + db * db;
            if (d < bd)
            {
                bd = d;
                best = (r, g, b);
            }
        }
        return best;
    }

    private static (double H, double S, double V) RgbToHsv((byte R, byte G, byte B) c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h;
        if (d <= 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = ((g - b) / d % 6 + 6) % 6 / 6.0;
        }
        else if (max == g)
        {
            h = ((b - r) / d + 2) / 6.0;
        }
        else
        {
            h = ((r - g) / d + 4) / 6.0;
        }
        return (h, max <= 0 ? 0 : d / max, max);
    }

    private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
    {
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        h = Wrap(h) * 6.0;
        int i = (int)Math.Floor(h) % 6;
        double f = h - Math.Floor(h);
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        (double r, double g, double b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>Parse the three sibling data files of the index (master palette, the
    /// bidirectional ramp extension, the paletteGroup transition table).</summary>
    public static BackgroundPalette Parse(string masterJson, string rampsJson, string transitionsJson)
    {
        var pool = new List<(byte, byte, byte)>();
        AddColors(pool, masterJson, "master palette");
        AddColors(pool, rampsJson, "ramp extension");
        Dictionary<string, List<string>> table =
            JsonSerializer.Deserialize<Dictionary<string, List<string>>>(transitionsJson, Options)
                ?? throw new JsonException("remap transition table parsed to null.");
        var transitions = table.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
        return new BackgroundPalette(pool.ToArray(), transitions);
    }

    public static BackgroundPalette LoadFiles(string masterPath, string rampsPath, string transitionsPath) =>
        Parse(File.ReadAllText(masterPath), File.ReadAllText(rampsPath), File.ReadAllText(transitionsPath));

    private static void AddColors(List<(byte, byte, byte)> pool, string json, string what)
    {
        List<List<int>> colors = JsonSerializer.Deserialize<List<List<int>>>(json, Options)
            ?? throw new JsonException($"{what} parsed to null.");
        foreach (List<int> c in colors)
        {
            if (c.Count != 3)
            {
                throw new JsonException($"{what} holds a non-RGB entry.");
            }
            pool.Add(((byte)Math.Clamp(c[0], 0, 255), (byte)Math.Clamp(c[1], 0, 255),
                (byte)Math.Clamp(c[2], 0, 255)));
        }
    }

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Library;
}
