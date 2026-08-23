using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlerSim.Sprites;

/// <summary>One tagged sprite in the v2 character library. Tags and trait names are
/// OPAQUE DATA to the C# side (docs/features/sprite-selection.md): fixes are edits to
/// players_v2_slices.json (or tagging_rules.py + rebuild), never code.</summary>
public sealed record SpriteDef
{
    public string Id { get; init; } = "";
    /// <summary>Atlas rect: x, y, w, h in texture pixels.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>Bottom-center of the feet, in ATLAS coordinates.</summary>
    public float PivotX { get; init; }
    public float PivotY { get; init; }
    public IReadOnlyDictionary<string, float> TraitAffinity { get; init; } = new Dictionary<string, float>();
    public string BodyPlan { get; init; } = "biped";
    public string WeightClass { get; init; } = "medium";
    public string Vibe { get; init; } = "neutral";
    public IReadOnlyList<string> Registers { get; init; } = Array.Empty<string>();
    public string PaletteGroup { get; init; } = "";

    /// <summary>Sprite w/h as drawn (aspect matching against the genome's
    /// widthScalar/heightScalar ratio).</summary>
    public double Aspect => H > 0 ? (double)W / H : 1.0;
}

/// <summary>
/// The v2 character sprite library (2026-08-22, sprite-selection.md): parses
/// godot/assets/players_v2_slices.json and exposes sprites by id plus the trait
/// vocabulary. Rects/pivots only — no texture concerns (the Godot layer owns the
/// atlas texture; headless evaluation never touches pixels).
/// </summary>
public sealed class SpriteLibrary
{
    public IReadOnlyList<SpriteDef> Sprites { get; }
    public IReadOnlyList<string> TraitVocabulary { get; }
    public string Texture { get; }
    public int TextureWidth { get; }
    public int TextureHeight { get; }

    private readonly Dictionary<string, SpriteDef> _byId;

    private SpriteLibrary(string texture, int texW, int texH,
        List<string> vocabulary, List<SpriteDef> sprites)
    {
        Texture = texture;
        TextureWidth = texW;
        TextureHeight = texH;
        TraitVocabulary = vocabulary;
        Sprites = sprites;
        _byId = new Dictionary<string, SpriteDef>(sprites.Count, StringComparer.Ordinal);
        foreach (SpriteDef sprite in sprites)
        {
            _byId[sprite.Id] = sprite; // last wins; ids are unique by pipeline contract
        }
    }

    public SpriteDef? ById(string? id) =>
        id is not null && _byId.TryGetValue(id, out SpriteDef? sprite) ? sprite : null;

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    public static SpriteLibrary Parse(string json)
    {
        LibraryDoc doc = JsonSerializer.Deserialize<LibraryDoc>(json, Options)
            ?? throw new JsonException("sprite library parsed to null.");
        if (doc.Sprites is null || doc.Sprites.Count == 0)
        {
            throw new JsonException("sprite library has no sprites.");
        }
        var sprites = new List<SpriteDef>(doc.Sprites.Count);
        foreach (SpriteDoc s in doc.Sprites)
        {
            if (s.Id is null || s.Rect is not { Count: 4 })
            {
                throw new JsonException("sprite library entry is missing id or rect.");
            }
            sprites.Add(new SpriteDef
            {
                Id = s.Id,
                X = s.Rect[0], Y = s.Rect[1], W = s.Rect[2], H = s.Rect[3],
                PivotX = s.Pivot is { Count: 2 } ? s.Pivot[0] : s.Rect[0] + s.Rect[2] / 2f,
                PivotY = s.Pivot is { Count: 2 } ? s.Pivot[1] : s.Rect[1] + s.Rect[3],
                TraitAffinity = s.TraitAffinity ?? new Dictionary<string, float>(),
                BodyPlan = s.BodyPlan ?? "biped",
                WeightClass = s.WeightClass ?? "medium",
                Vibe = s.Vibe ?? "neutral",
                Registers = s.Register ?? new List<string>(),
                PaletteGroup = s.PaletteGroup ?? "",
            });
        }
        return new SpriteLibrary(
            doc.Texture ?? "",
            doc.TextureSize is { Count: 2 } ? doc.TextureSize[0] : 0,
            doc.TextureSize is { Count: 2 } ? doc.TextureSize[1] : 0,
            doc.TraitVocabulary ?? new List<string>(),
            sprites);
    }

    public static SpriteLibrary LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // DTOs mirror the v2 slices contract; unknown fields (source, license, interim,
    // tags — a convenience duplicate of traitAffinity's keys) are ignored on load.
    private sealed class LibraryDoc
    {
        public string? Texture { get; set; }
        public List<int>? TextureSize { get; set; }
        public List<string>? TraitVocabulary { get; set; }
        public List<SpriteDoc>? Sprites { get; set; }
    }

    private sealed class SpriteDoc
    {
        public string? Id { get; set; }
        public List<int>? Rect { get; set; }
        public List<float>? Pivot { get; set; }
        public Dictionary<string, float>? TraitAffinity { get; set; }
        public string? BodyPlan { get; set; }
        public string? WeightClass { get; set; }
        public string? Vibe { get; set; }
        public List<string>? Register { get; set; }
        public string? PaletteGroup { get; set; }
    }
}
