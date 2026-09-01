using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlerSim.Sprites;

/// <summary>An atlas rect (texture pixels).</summary>
public readonly record struct TileRect(int X, int Y, int W, int H);

/// <summary>One decorative prop in a theme (cosmetic only, no collision).</summary>
public sealed record ThemeProp(string Kind, TileRect Rect);

/// <summary>One tagged tile theme in the v2 stage library (M4d, 2026-09-01 —
/// docs/features/stage-tile-selection.md). Tags and trait names are OPAQUE DATA to
/// the C# side, exactly like the character library: fixes are edits to
/// tiles_v2_slices.json (or build_tiles2.py + rebuild), never code.</summary>
public sealed record ThemeDef
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Registers { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, float> TraitAffinity { get; init; } = new Dictionary<string, float>();
    public string Vibe { get; init; } = "neutral";
    public string PaletteGroup { get; init; } = "";

    /// <summary>The 16-piece exposure set: key = vertical state {T,M,B,S} + horizontal
    /// state {L,M,R,S}; each key maps to 1..n variant rects (the renderer picks one
    /// deterministically per cell).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TileRect>> Tiles { get; init; } =
        new Dictionary<string, IReadOnlyList<TileRect>>();

    /// <summary>Drop-through slab pieces, keyed L/M/R/S (horizontal exposure only —
    /// the slab is always one row of the thin collision slice).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TileRect>> DropTiles { get; init; } =
        new Dictionary<string, IReadOnlyList<TileRect>>();

    public IReadOnlyList<ThemeProp> Props { get; init; } = Array.Empty<ThemeProp>();
}

/// <summary>
/// The v2 stage tile library: parses godot/assets/tiles_v2_slices.json (contract
/// v2.1-variants-props) and exposes themes by name plus the stage trait vocabulary.
/// Rects only — the Godot layer owns the atlas texture; headless evaluation never
/// touches pixels.
/// </summary>
public sealed class StageThemeLibrary
{
    public IReadOnlyList<ThemeDef> Themes { get; }
    public IReadOnlyList<string> StageTraitVocabulary { get; }
    public string Texture { get; }
    public int TileSize { get; }

    /// <summary>Drop-slab art height in atlas pixels; DropHeight / TileSize is the
    /// world-unit slab thickness MatchConfig.ThinPlatformThickness matches.</summary>
    public int DropHeight { get; }

    private readonly Dictionary<string, ThemeDef> _byName;

    private StageThemeLibrary(string texture, int tileSize, int dropHeight,
        List<string> vocabulary, List<ThemeDef> themes)
    {
        Texture = texture;
        TileSize = tileSize;
        DropHeight = dropHeight;
        StageTraitVocabulary = vocabulary;
        Themes = themes;
        _byName = new Dictionary<string, ThemeDef>(themes.Count, StringComparer.Ordinal);
        foreach (ThemeDef theme in themes)
        {
            _byName[theme.Name] = theme;
        }
    }

    public ThemeDef? ByName(string? name) =>
        name is not null && _byName.TryGetValue(name, out ThemeDef? theme) ? theme : null;

    public bool Contains(string? name) => name is not null && _byName.ContainsKey(name);

    public static StageThemeLibrary Parse(string json)
    {
        LibraryDoc doc = JsonSerializer.Deserialize<LibraryDoc>(json, Options)
            ?? throw new JsonException("stage theme library parsed to null.");
        if (doc.Themes is null || doc.Themes.Count == 0)
        {
            throw new JsonException("stage theme library has no themes.");
        }
        var themes = new List<ThemeDef>(doc.Themes.Count);
        foreach (ThemeDoc t in doc.Themes)
        {
            if (t.Name is null || t.Tiles is null)
            {
                throw new JsonException("stage theme entry is missing name or tiles.");
            }
            themes.Add(new ThemeDef
            {
                Name = t.Name,
                Registers = t.Register ?? new List<string>(),
                TraitAffinity = t.TraitAffinity ?? new Dictionary<string, float>(),
                Vibe = t.Vibe ?? "neutral",
                PaletteGroup = t.PaletteGroup ?? "",
                Tiles = ParseRectLists(t.Tiles, t.Name),
                DropTiles = ParseRectLists(t.DropTiles ?? new(), t.Name),
                Props = (t.Props ?? new List<PropDoc>()).Select(p => new ThemeProp(
                    p.Kind ?? "prop", ToRect(p.Rect, t.Name))).ToList(),
            });
        }
        return new StageThemeLibrary(
            doc.Texture ?? "",
            doc.TileSize,
            doc.DropHeight,
            doc.StageTraitVocabulary ?? new List<string>(),
            themes);
    }

    public static StageThemeLibrary LoadFile(string path) => Parse(File.ReadAllText(path));

    private static IReadOnlyDictionary<string, IReadOnlyList<TileRect>> ParseRectLists(
        Dictionary<string, List<List<int>>> raw, string theme)
    {
        var result = new Dictionary<string, IReadOnlyList<TileRect>>(raw.Count, StringComparer.Ordinal);
        foreach ((string key, List<List<int>> rects) in raw)
        {
            if (rects.Count == 0)
            {
                throw new JsonException($"theme '{theme}' piece '{key}' has no rects.");
            }
            result[key] = rects.Select(r => ToRect(r, theme)).ToList();
        }
        return result;
    }

    private static TileRect ToRect(List<int>? r, string theme) =>
        r is { Count: 4 }
            ? new TileRect(r[0], r[1], r[2], r[3])
            : throw new JsonException($"theme '{theme}' has a malformed rect.");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // DTOs mirror the v2.1 contract; unknown fields (source, license, styleFamily,
    // kind, contract, textureSize) are ignored on load.
    private sealed class LibraryDoc
    {
        public string? Texture { get; set; }
        public int TileSize { get; set; } = 32;
        public int DropHeight { get; set; } = 12;
        public List<string>? StageTraitVocabulary { get; set; }
        public List<ThemeDoc>? Themes { get; set; }
    }

    private sealed class ThemeDoc
    {
        public string? Name { get; set; }
        public List<string>? Register { get; set; }
        public Dictionary<string, float>? TraitAffinity { get; set; }
        public string? Vibe { get; set; }
        public string? PaletteGroup { get; set; }
        public Dictionary<string, List<List<int>>>? Tiles { get; set; }
        public Dictionary<string, List<List<int>>>? DropTiles { get; set; }
        public List<PropDoc>? Props { get; set; }
    }

    private sealed class PropDoc
    {
        public string? Kind { get; set; }
        public List<int>? Rect { get; set; }
    }
}
