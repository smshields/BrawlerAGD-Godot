using System.Text.Json;

namespace BrawlerSim.Sprites;

/// <summary>
/// Stage theme selection tuning (M4d) — hot-editable as
/// godot/assets/tile_selection.json (sibling of sprite_selection.json, designer
/// 2026-09-01). Code defaults match the shipped file's starting values.
/// </summary>
public sealed record StageThemeConfig
{
    /// <summary>Probability mass pinned EXACTLY to the goofy channel (the brief's
    /// reserved 8%) in every pool holding both classes.</summary>
    public float GoofBudget { get; init; } = 0.08f;

    /// <summary>Vibes that count as the goofy channel ("beehive and similar
    /// vibe-tagged themes" — the library tags goofy + cute).</summary>
    public IReadOnlyList<string> GoofVibes { get; init; } = new[] { "goofy", "cute" };

    /// <summary>No-monopoly rule: after shaping, no single theme may hold more than
    /// this probability mass in one pool (the brief's "no theme above 12%").</summary>
    public float MaxThemeShare { get; init; } = 0.12f;

    /// <summary>Softmax temperature over candidate scores. Lower = greedier.</summary>
    public float SoftmaxTemperature { get; init; } = 0.35f;

    /// <summary>Repair rule floor (the SpriteId pattern): an inherited theme whose
    /// affinity score against the child's salient stage traits falls below this
    /// re-resolves. Only checked when the child HAS salient traits.</summary>
    public float RepairFloor { get; init; } = 0.05f;

    /// <summary>Register pools smaller than this fall back to the full library
    /// (the fey register has no themes and must not starve).</summary>
    public int RegisterPoolFloor { get; init; } = 6;

    /// <summary>Score subtracted per prior use of the same theme within one built
    /// game, so a four-stage lineup diverges.</summary>
    public float OverusePenalty { get; init; } = 0.5f;

    /// <summary>Ordered candidates sampled per selection.</summary>
    public int CandidateCount { get; init; } = 4;

    public static readonly StageThemeConfig Default = new();

    public static StageThemeConfig Parse(string json) =>
        JsonSerializer.Deserialize<StageThemeConfig>(json, Options)
            ?? throw new JsonException("stage theme config parsed to null.");

    public static StageThemeConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = Serialization.JsonOptions.Tuning;
}
