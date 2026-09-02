using Godot;
using BrawlerSim.Weather;

namespace BrawlerGodot;

/// <summary>
/// Weather sources (backgrounds track Phase 3, 2026-09-02): the shared planner on
/// the hot-editable preset table, plus the procedural particle textures — fully
/// generated (no corpus, no license exposure) per the corpus plan's mechanism-1
/// choice. Ramp tinting rides the background palette machinery, so weather is
/// quantization-coherent with the backdrop behind it.
/// </summary>
public static class WeatherBank
{
    private static WeatherPlanner? _planner;
    private static readonly System.Collections.Generic.Dictionary<string, ImageTexture> Textures = new();

    public static WeatherPlanner Planner => _planner ??= new WeatherPlanner(
        WeatherConfig.Parse(FileAccess.GetFileAsString("res://assets/weather_presets.json")));

    /// <summary>The material family's base color, tinted toward the preset's palette
    /// ramp and snapped to the master+ramp pool (the remap transform with a neutral
    /// source group — grey sources re-anchor exactly onto the target hue).</summary>
    public static Color ParticleColor(string type, string ramp)
    {
        (byte r, byte g, byte b) = BaseColor(type);
        if (ramp != "grey" || type is "rain" or "petals" or "embers")
        {
            (r, g, b) = BackgroundBank.Palette.RemapColor((r, g, b), "grey", ramp);
        }
        return new Color(r / 255f, g / 255f, b / 255f);
    }

    private static (byte, byte, byte) BaseColor(string type) => type switch
    {
        "rain" => ((byte)186, (byte)198, (byte)224),
        "snow" => ((byte)238, (byte)240, (byte)245),
        "ash" => ((byte)150, (byte)146, (byte)140),
        "embers" => ((byte)255, (byte)176, (byte)84),
        "dust" => ((byte)226, (byte)210, (byte)160),
        "fog" => ((byte)198, (byte)200, (byte)210),
        "petals" => ((byte)226, (byte)168, (byte)196),
        "sand" => ((byte)222, (byte)196, (byte)130),
        _ => ((byte)220, (byte)220, (byte)220),
    };

    /// <summary>One generated texture per material family: streaks for rain, soft
    /// blobs for fog, small rounded motes for the rest — white, tinted per emitter.</summary>
    public static ImageTexture TextureFor(string type)
    {
        if (Textures.TryGetValue(type, out ImageTexture? cached))
        {
            return cached;
        }
        Image image = type switch
        {
            "rain" => Streak(2, 9),
            "fog" => SoftBlob(48, 32),
            "snow" => Mote(4, soft: true),
            "embers" => Mote(3, soft: false),
            "dust" => Mote(2, soft: true),
            "petals" => Mote(4, soft: false),
            "sand" => Streak(3, 2),
            _ => Mote(3, soft: false), // ash and friends
        };
        var texture = ImageTexture.CreateFromImage(image);
        Textures[type] = texture;
        return texture;
    }

    private static Image Streak(int w, int h)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float fade = 0.35f + 0.65f * (y / (float)Mathf.Max(1, h - 1));
            for (int x = 0; x < w; x++)
            {
                image.SetPixel(x, y, new Color(1f, 1f, 1f, fade));
            }
        }
        return image;
    }

    private static Image Mote(int size, bool soft)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / Mathf.Max(0.5f, c);
                float a = soft ? Mathf.Clamp(1.2f - d, 0f, 1f) : (d <= 1.01f ? 1f : 0f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        return image;
    }

    private static Image SoftBlob(int w, int h)
    {
        var image = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = (x - cx) / cx, dy = (y - cy) / cy;
                float a = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy), 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * 0.9f));
            }
        }
        return image;
    }
}
