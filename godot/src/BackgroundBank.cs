using Godot;
using BrawlerSim.Backgrounds;

namespace BrawlerGodot;

/// <summary>
/// Background sources (backgrounds track Phase 1, 2026-09-02 —
/// docs/background-implementation-brief.md): the bg-v1 index + palette data + the
/// app's shared background selector, mirroring ThemeBank/SpriteBank. Stages with a
/// BackgroundId gene render through this; null-gene (pre-v14) stages keep the blank
/// legacy backdrop. Remapped textures are derived ONCE per (entry, remap) at load —
/// the corpus is palette-quantized, so a remap is a small color-cache pass, not an
/// image filter (BackgroundPalette owns the transform).
/// </summary>
public static class BackgroundBank
{
    private static BackgroundLibrary? _library;
    private static BackgroundPalette? _palette;
    private static BackgroundSelector? _selector;
    private static readonly System.Collections.Generic.Dictionary<string, ImageTexture> RemapCache = new();

    /// <summary>The bg-v1 index, FileAccess-loaded so it works inside exported .pck
    /// files too. Loader-refused entries (license/attribution violations) never reach
    /// the renderer or the credits by construction.</summary>
    public static BackgroundLibrary Library => _library ??= BackgroundLibrary.Parse(
        FileAccess.GetFileAsString("res://assets/backgrounds_v1/backgrounds_v1_index.json"));

    /// <summary>Master-64 + bidirectional ramps + the remap transition table.</summary>
    public static BackgroundPalette Palette => _palette ??= BackgroundPalette.Parse(
        FileAccess.GetFileAsString("res://assets/backgrounds_v1/master_palette.json"),
        FileAccess.GetFileAsString("res://assets/backgrounds_v1/background_ramps_bidir.json"),
        FileAccess.GetFileAsString("res://assets/backgrounds_v1/remap_transition_table.json"));

    /// <summary>The app's shared selector, on the shipped hot-editable tuning file;
    /// the tile theme library feeds palette harmony (backgrounds follow tiles).</summary>
    public static BackgroundSelector Selector => _selector ??= new BackgroundSelector(
        Library, Palette,
        BackgroundSelectionConfig.Parse(
            FileAccess.GetFileAsString("res://assets/background_selection.json")),
        ThemeBank.Library);

    /// <summary>The entry's texture under a remap target (null = native, straight from
    /// the import). Remapped variants are computed once and cached for the session.</summary>
    public static Texture2D TextureFor(BackgroundEntry entry, string? remap)
    {
        var native = GD.Load<Texture2D>($"res://assets/backgrounds_v1/{entry.File}");
        if (remap is null)
        {
            return native;
        }
        string key = $"{entry.Id}|{remap}";
        if (RemapCache.TryGetValue(key, out ImageTexture? cached))
        {
            return cached;
        }
        Image image = native.GetImage();
        image.Convert(Image.Format.Rgba8);
        var colorCache = new System.Collections.Generic.Dictionary<uint, Color>();
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color c = image.GetPixel(x, y);
                if (c.A <= 0f)
                {
                    continue;
                }
                uint packed = c.ToRgba32();
                if (!colorCache.TryGetValue(packed, out Color mapped))
                {
                    (byte r, byte g, byte b) = Palette.RemapColor(
                        ((byte)Mathf.RoundToInt(c.R * 255f),
                         (byte)Mathf.RoundToInt(c.G * 255f),
                         (byte)Mathf.RoundToInt(c.B * 255f)),
                        entry.PaletteGroup, remap);
                    mapped = new Color(r / 255f, g / 255f, b / 255f, c.A);
                    colorCache[packed] = mapped;
                }
                image.SetPixel(x, y, mapped with { A = c.A });
            }
        }
        var texture = ImageTexture.CreateFromImage(image);
        RemapCache[key] = texture;
        return texture;
    }
}
