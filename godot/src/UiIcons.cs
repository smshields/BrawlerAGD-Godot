using Godot;

namespace BrawlerGodot;

/// <summary>
/// Procedural white line-art icon textures for UI buttons (evolve transport
/// controls, the preview save button). Rendered from inline SVG at first use and
/// cached — no image assets, no emojis, matches the app's minimalist line-art
/// aesthetic. All icons share a 24-unit viewBox drawn in pure white; buttons
/// tint/dim them through the normal theme color path.
/// </summary>
public static class UiIcons
{
    private const int ViewBox = 24;
    private static readonly System.Collections.Generic.Dictionary<string, ImageTexture> Cache = new();

    /// <summary>Right-pointing filled triangle (start).</summary>
    public static Texture2D Play(int size = 22) => FromSvg("play", size,
        "<polygon points='7,4.5 20,12 7,19.5' fill='white'/>");

    /// <summary>Two vertical bars (pause).</summary>
    public static Texture2D Pause(int size = 22) => FromSvg("pause", size,
        "<rect x='6' y='4.5' width='4' height='15' fill='white'/>" +
        "<rect x='14' y='4.5' width='4' height='15' fill='white'/>");

    /// <summary>Circular arrow (reset).</summary>
    public static Texture2D Reset(int size = 22) => FromSvg("reset", size,
        "<path d='M12 4.5 A 7.5 7.5 0 1 0 19.5 12' fill='none' stroke='white' stroke-width='2.6'/>" +
        "<polygon points='12,0.5 12,8.5 17.5,4.5' fill='white'/>");

    /// <summary>Trash can (delete).</summary>
    public static Texture2D Trash(int size = 22) => FromSvg("trash", size,
        "<path d='M4.5 6 h15' fill='none' stroke='white' stroke-width='2.2'/>" +
        "<path d='M9 6 v-2.5 h6 v2.5' fill='none' stroke='white' stroke-width='2.2'/>" +
        "<path d='M6.5 6 l1 14.5 h9 l1 -14.5' fill='none' stroke='white' stroke-width='2.2'/>" +
        "<path d='M10 9.5 v8 M14 9.5 v8' fill='none' stroke='white' stroke-width='1.8'/>");

    /// <summary>Arrow down into a tray (save to storage).</summary>
    public static Texture2D Save(int size = 22) => FromSvg("save", size,
        "<path d='M12 3 v8' fill='none' stroke='white' stroke-width='2.6'/>" +
        "<polygon points='7,10.5 17,10.5 12,16.5' fill='white'/>" +
        "<path d='M4.5 15.5 v5 h15 v-5' fill='none' stroke='white' stroke-width='2.6'/>");

    private static ImageTexture FromSvg(string key, int size, string body)
    {
        string cacheKey = $"{key}:{size}";
        if (Cache.TryGetValue(cacheKey, out ImageTexture? cached))
        {
            return cached;
        }
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {ViewBox} {ViewBox}'>{body}</svg>";
        var image = new Image();
        image.LoadSvgFromString(svg, size / (float)ViewBox);
        var texture = ImageTexture.CreateFromImage(image);
        Cache[cacheKey] = texture;
        return texture;
    }
}
