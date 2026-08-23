using System.Text.Json;

namespace BrawlerSim.Sprites;

/// <summary>One tagged melee attack sprite (2026-08-23, M4b —
/// docs/features/attack-sprite-selection.md). Classes, elements, and plans are
/// OPAQUE DATA strings, same contract as the character library.</summary>
public sealed record MoveSpriteDef
{
    public string Id { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>blade|axe|blunt|polearm|whip|staff|natural|burst|impact|object.</summary>
    public string AttackClass { get; init; } = "";
    /// <summary>fire|ice|poison|electric|chaos|spectral — or null for mundane steel.</summary>
    public string? Element { get; init; }
    /// <summary>Body plans this sprite may be wielded by (HARD filter).</summary>
    public IReadOnlyList<string> CompatiblePlans { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, float> TraitAffinity { get; init; } = new Dictionary<string, float>();
    public string Vibe { get; init; } = "neutral";
    public string PaletteGroup { get; init; } = "";
}

/// <summary>
/// The v2 MELEE attack sprite library: parses godot/assets/moves_v2_slices.json.
/// Rects only — the Godot layer owns the atlas texture. Projectile sprites are a
/// later track; this library deliberately contains no ranged anything.
/// </summary>
public sealed class MoveSpriteLibrary
{
    public IReadOnlyList<MoveSpriteDef> Sprites { get; }
    public string Texture { get; }

    private readonly Dictionary<string, MoveSpriteDef> _byId;

    private MoveSpriteLibrary(string texture, List<MoveSpriteDef> sprites)
    {
        Texture = texture;
        Sprites = sprites;
        _byId = new Dictionary<string, MoveSpriteDef>(sprites.Count, StringComparer.Ordinal);
        foreach (MoveSpriteDef sprite in sprites)
        {
            _byId[sprite.Id] = sprite;
        }
    }

    public MoveSpriteDef? ById(string? id) =>
        id is not null && _byId.TryGetValue(id, out MoveSpriteDef? sprite) ? sprite : null;

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    public static MoveSpriteLibrary Parse(string json)
    {
        LibraryDoc doc = JsonSerializer.Deserialize<LibraryDoc>(json, Options)
            ?? throw new JsonException("move sprite library parsed to null.");
        if (doc.Sprites is null || doc.Sprites.Count == 0)
        {
            throw new JsonException("move sprite library has no sprites.");
        }
        var sprites = new List<MoveSpriteDef>(doc.Sprites.Count);
        foreach (SpriteDoc s in doc.Sprites)
        {
            if (s.Id is null || s.Rect is not { Count: 4 } || s.AttackClass is null)
            {
                throw new JsonException("move sprite entry is missing id, rect, or attackClass.");
            }
            sprites.Add(new MoveSpriteDef
            {
                Id = s.Id,
                X = s.Rect[0], Y = s.Rect[1], W = s.Rect[2], H = s.Rect[3],
                AttackClass = s.AttackClass,
                Element = s.Element,
                CompatiblePlans = s.CompatiblePlans ?? new List<string>(),
                TraitAffinity = s.TraitAffinity ?? new Dictionary<string, float>(),
                Vibe = s.Vibe ?? "neutral",
                PaletteGroup = s.PaletteGroup ?? "",
            });
        }
        return new MoveSpriteLibrary(doc.Texture ?? "", sprites);
    }

    public static MoveSpriteLibrary LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class LibraryDoc
    {
        public string? Texture { get; set; }
        public List<SpriteDoc>? Sprites { get; set; }
    }

    private sealed class SpriteDoc
    {
        public string? Id { get; set; }
        public List<int>? Rect { get; set; }
        public string? AttackClass { get; set; }
        public string? Element { get; set; }
        public List<string>? CompatiblePlans { get; set; }
        public Dictionary<string, float>? TraitAffinity { get; set; }
        public string? Vibe { get; set; }
        public string? PaletteGroup { get; set; }
    }
}
