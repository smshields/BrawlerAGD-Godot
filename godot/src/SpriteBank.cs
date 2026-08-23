using Godot;
using System.Text.Json;
using BrawlerSim.Genome;
using BrawlerSim.Sprites;

namespace BrawlerGodot;

/// <summary>
/// Sprite sources. Two player generations coexist (2026-08-22, sprite selection —
/// docs/features/sprite-selection.md):
/// - v2: the tagged 490-sprite DCSS-derived library (players_v2.png), addressed by
///   SpriteId. Sprites stretch to the body box like the v1 convention; the data's
///   feet pivots are exactly each rect's bottom-center (verified over the whole
///   library), so centered drawing IS pivot-correct — the pivot field exists for the
///   pipeline/QA and for future untrimmed art.
/// - v1: the Kenney 1-bit sheets sliced exactly as Unity sliced them (rect tables from
///   the Unity .meta files), addressed by SpriteIndex — the fallback for null-gene
///   (pre-v10) characters. Move sprites still come from the v1 moves sheet.
/// </summary>
public static class SpriteBank
{
    private static AtlasTexture[]? _players;
    private static AtlasTexture[]? _moves;
    private static SpriteLibrary? _library;
    private static SpriteSelector? _selector;
    private static Texture2D? _playersV2;
    private static readonly System.Collections.Generic.Dictionary<string, AtlasTexture> _v2Cache = new();

    public static AtlasTexture Player(int index) => Get(ref _players, "players")[Wrap(index, _players!.Length)];

    public static AtlasTexture Move(int index) => Get(ref _moves, "moves")[Wrap(index, _moves!.Length)];

    /// <summary>The v2 library (rects + tags), FileAccess-loaded so it works inside
    /// exported .pck files too.</summary>
    public static SpriteLibrary Library =>
        _library ??= SpriteLibrary.Parse(FileAccess.GetFileAsString("res://assets/players_v2_slices.json"));

    /// <summary>The app's shared selector, on the shipped hot-editable tuning file.</summary>
    public static SpriteSelector Selector =>
        _selector ??= new SpriteSelector(Library,
            SpriteSelectionConfig.Parse(FileAccess.GetFileAsString("res://assets/sprite_selection.json")));

    /// <summary>The v2 sprite for an id; null when the id is null/unknown.</summary>
    public static AtlasTexture? PlayerById(string? id)
    {
        SpriteDef? def = Library.ById(id);
        if (def is null)
        {
            return null;
        }
        if (_v2Cache.TryGetValue(def.Id, out AtlasTexture? cached))
        {
            return cached;
        }
        _playersV2 ??= GD.Load<Texture2D>("res://assets/players_v2.png");
        var slice = new AtlasTexture
        {
            Atlas = _playersV2,
            Region = new Rect2(def.X, def.Y, def.W, def.H),
        };
        _v2Cache[def.Id] = slice;
        return slice;
    }

    /// <summary>The one resolution rule every view uses: the semantic v2 sprite when
    /// the character carries a known gene, else the legacy v1 glyph by index.</summary>
    public static Texture2D PlayerFor(CharacterGenome character) =>
        PlayerById(character.SpriteId) ?? (Texture2D)Player(character.SpriteIndex);

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
