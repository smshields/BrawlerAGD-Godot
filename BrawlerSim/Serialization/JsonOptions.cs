using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlerSim.Serialization;

/// <summary>
/// The three JSON serializer flavors used across the codebase, each previously
/// copy-pasted per serializer (8 copies, 2026-09-01 dedupe). NOT here on purpose:
/// BuiltGame.ContentKeyOptions — the content-key/naming-seed serializer must stay
/// pinned independently of any document-format style change.
/// </summary>
public static class JsonOptions
{
    /// <summary>Persisted documents (game.json, built-game.json, run.json):
    /// camelCase, indented for humans, nulls omitted.</summary>
    public static readonly JsonSerializerOptions Document = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Read-only asset libraries (sprite/theme slice manifests).</summary>
    public static readonly JsonSerializerOptions Library = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Hand-edited tuning files (sprite/tile selection configs):
    /// comments allowed so the designer can annotate values in place.</summary>
    public static readonly JsonSerializerOptions Tuning = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
