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

    /// <summary>An 8x8 soft dust mote — white, tinted per emit.</summary>
    public static ImageTexture PuffTexture => _puff ??= ImageTexture.CreateFromImage(Puff(8));

    private static Image Puff(int size)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / Mathf.Max(0.5f, c);
                float a = Mathf.Clamp(1.15f - d, 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        return image;
    }
}
