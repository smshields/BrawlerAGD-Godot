using Godot;
using BrawlerSim.Vfx;

namespace BrawlerGodot;

/// <summary>
/// Ground FX sources (particle prototype, 2026-09-14): the hot-editable tuning
/// config plus the one procedurally generated puff texture — same no-corpus,
/// no-license-exposure mechanism as WeatherBank's particle textures.
/// </summary>
public static class GroundFxBank
{
    private static GroundFxConfig? _config;
    private static ImageTexture? _puff;

    public static GroundFxConfig Config => _config ??= GroundFxConfig.Parse(
        FileAccess.GetFileAsString("res://assets/ground_fx.json"));

    /// <summary>Source resolution of the puff texture. Big enough that a dust
    /// cloud scaled up over the arena stays SOFT — an 8 px source under the
    /// project's nearest filtering read as chunky octagons, not dust
    /// (2026-09-15). GroundFxView normalizes size against this, so the tuning
    /// file's scale numbers keep their meaning.</summary>
    public const int PuffTexturePx = 32;

    /// <summary>A soft round dust mote — white, tinted per emit.</summary>
    public static ImageTexture PuffTexture =>
        _puff ??= ImageTexture.CreateFromImage(Puff(PuffTexturePx));

    private static Image Puff(int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / Mathf.Max(0.5f, c);
                // Smootherstep falloff to a feathered edge: no hard rim to alias.
                float t = Mathf.Clamp(1f - d, 0f, 1f);
                float a = t * t * (3f - 2f * t);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        return image;
    }
}
