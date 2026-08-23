using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrawlerSim.Sprites;

/// <summary>
/// Sprite-selection tuning (2026-08-22, sprite-selection.md decision 5): every
/// threshold ships as JSON next to the slices (godot/assets/sprite_selection.json),
/// hot-editable without recompiling. Code defaults below match the shipped file's
/// starting values.
/// </summary>
public sealed record SpriteSelectionConfig
{
    /// <summary>Probability mass pinned to vibe == "goofy" sprites in every pool that
    /// has both goofy and non-goofy candidates — regardless of trait score. Pinned
    /// EXACTLY (floor and cap), so goofy-heavy pools (fey) don't overshoot.</summary>
    public float GoofBudget { get; init; } = 0.12f;

    /// <summary>Maximum probability mass for bodyPlan == "biped" (the library is 73%
    /// biped; the cap is enforced here, not in the data).</summary>
    public float BipedCap { get; init; } = 0.55f;

    /// <summary>Softmax temperature over candidate scores. Lower = greedier.</summary>
    public float SoftmaxTemperature { get; init; } = 0.35f;

    /// <summary>Repair rule floor (decision 2): an inherited sprite whose affinity
    /// score against the child's salient traits falls below this re-resolves. Only
    /// checked when the child HAS salient traits — a neutral genome contradicts
    /// nothing.</summary>
    public float RepairFloor { get; init; } = 0.05f;

    /// <summary>Ordered candidates sampled for the name negotiation loop.</summary>
    public int CandidateCount { get; init; } = 6;

    /// <summary>Bound on the name negotiation loop (step 6).</summary>
    public int MaxNegotiationIterations { get; init; } = 6;

    /// <summary>Name/sprite compatibility acceptance threshold (step 6).</summary>
    public float CompatibilityThreshold { get; init; } = 0.2f;

    /// <summary>Weight of the sprite-aspect vs genome width/height ratio bonus.</summary>
    public float AspectBonusWeight { get; init; } = 0.15f;

    /// <summary>Score subtracted per prior use of the same sprite id within one
    /// built game / generated game, so duplicate fighters diverge.</summary>
    public float OverusePenalty { get; init; } = 0.5f;

    /// <summary>Register pools smaller than this fall back to the full library
    /// (small registers like scifi must not starve).</summary>
    public int RegisterPoolFloor { get; init; } = 20;

    /// <summary>No-monopoly rule: after shaping, no single sprite may hold more than
    /// this probability mass in one pool (excess redistributes proportionally). Guards
    /// the "no sprite above 2% of picks" target against rare double-dip affinity
    /// profiles that would otherwise win a large slice of genome space (the
    /// giant_newt lesson, 2026-08-22).</summary>
    public float MaxSpriteShare { get; init; } = 0.04f;

    /// <summary>Compatibility weight of a name-part morpheme tag that is not also a
    /// salient trait (salient traits weigh in at their salience score).</summary>
    public float PartTagWeight { get; init; } = 0.3f;

    public static readonly SpriteSelectionConfig Default = new();

    public static SpriteSelectionConfig Parse(string json) =>
        JsonSerializer.Deserialize<SpriteSelectionConfig>(json, Options)
            ?? throw new JsonException("sprite selection config parsed to null.");

    public static SpriteSelectionConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
