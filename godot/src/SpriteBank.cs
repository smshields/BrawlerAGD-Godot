using Godot;
using System.Text.Json;

namespace BrawlerGodot;

/// <summary>
/// The Kenney 1-bit sheets sliced exactly as Unity sliced them (rect tables extracted
/// from the Unity .meta files), so a genome's spriteIndex renders the same glyph it did
/// in the original build.
/// </summary>
public static class SpriteBank
{
    private static AtlasTexture[]? _players;
    private static AtlasTexture[]? _moves;

    public static AtlasTexture Player(int index) => Get(ref _players, "players")[Wrap(index, _players!.Length)];

    public static AtlasTexture Move(int index) => Get(ref _moves, "moves")[Wrap(index, _moves!.Length)];

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private static AtlasTexture[] Get(ref AtlasTexture[]? cache, string kind)
    {
        if (cache != null)
        {
            return cache;
        }
        var texture = GD.Load<Texture2D>($"res://assets/{kind}.png");
        string json = FileAccess.GetFileAsString($"res://assets/{kind}_slices.json");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement rects = doc.RootElement.GetProperty("rects");
        var slices = new AtlasTexture[rects.GetArrayLength()];
        int i = 0;
        foreach (JsonElement rect in rects.EnumerateArray())
        {
            slices[i++] = new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(
                    rect[0].GetSingle(), rect[1].GetSingle(), rect[2].GetSingle(), rect[3].GetSingle()),
            };
        }
        cache = slices;
        return cache;
    }
}
