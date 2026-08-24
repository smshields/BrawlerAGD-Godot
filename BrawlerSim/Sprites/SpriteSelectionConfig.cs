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

    /// <summary>Melee attack sprite selection tuning (2026-08-23, M4b —
    /// docs/features/attack-sprite-selection.md).</summary>
    public MoveSelectionConfig Moves { get; init; } = new();

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

/// <summary>Register → melee flavor mapping row (attack-sprite-selection.md step 3):
/// a sprite earns the register bonus when its element is in Elements OR its
/// attackClass is in Classes.</summary>
public sealed record RegisterAffinityDef
{
    public IReadOnlyList<string> Elements { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();
}

/// <summary>Melee attack sprite selection knobs — hot-editable with the rest of
/// godot/assets/sprite_selection.json (the "moves" section).</summary>
public sealed record MoveSelectionConfig
{
    /// <summary>Probability mass pinned EXACTLY to attackClass "object" in every pool
    /// holding both objects and non-objects (the banana must remain possible — the
    /// moves.png hearts-and-dice lineage; designer: goofy stays decently likely).</summary>
    public float ObjectBudget { get; init; } = 0.10f;

    public float SoftmaxTemperature { get; init; } = 0.35f;

    /// <summary>Selection is TWO-STAGE (2026-08-23, the cloud-saturation lesson): the
    /// wields list picks the attack CLASS, the move's params pick the sprite WITHIN
    /// it. Single-stage scoring let evolved moves' extreme trait vectors out-vote the
    /// wields signal on every repair re-pick, and burst — in every wields list —
    /// absorbed 82% of an evolved population. Class stage: wields bonus (first entry
    /// full, later entries decaying) + class register affinity + sweep shape + a
    /// muted mean-trait voice; sprite stage: trait dot + element register affinity +
    /// palette − cross-character duplicates.</summary>
    public float WieldsFirstBonus { get; init; } = 0.6f;
    public float WieldsOtherBonus { get; init; } = 0.35f;
    public float WieldsMissingPenalty { get; init; } = 0.4f;

    /// <summary>Later wields entries decay: bonus = WieldsOtherBonus × decay^(pos−1),
    /// so the universal fallback classes (burst sits in everyone's list) don't rival
    /// the character's signature class.</summary>
    public float WieldsPositionDecay { get; init; } = 0.5f;

    /// <summary>How loudly the move's traits speak in the CLASS stage (mean member
    /// trait dot × this). Full volume within the class; muted across classes, so a
    /// brutal move picks the brutal blade, not the blade-shaped cloud.</summary>
    public float ClassTraitWeight { get; init; } = 0.5f;

    /// <summary>Bonus when the sprite fits the shared register per RegisterAffinities.</summary>
    public float RegisterBonus { get; init; } = 0.2f;

    /// <summary>Bonus when the sprite's paletteGroup matches the character sprite's,
    /// or is one of NeutralPalettes.</summary>
    public float PaletteBonus { get; init; } = 0.1f;
    public IReadOnlyList<string> NeutralPalettes { get; init; } = new[] { "grey", "bone", "black" };

    /// <summary>Penalty per use of the sprite by OTHER characters in the same game
    /// (within one character, duplication is a HARD exclusion, not a penalty).</summary>
    public float CrossDuplicatePenalty { get; init; } = 0.3f;

    /// <summary>Sweep-shape bonus weight: wide hitboxes favor horizontal classes
    /// (blade/polearm/whip), tall ones favor slam classes (blunt/impact).</summary>
    public float SweepBonus { get; init; } = 0.15f;

    /// <summary>Repair floor on the move-trait dot product: an inherited attack
    /// sprite re-resolves when its class left the character sprite's wields AND its
    /// semantic score falls below this.</summary>
    public float RepairFloor { get; init; } = 0.05f;

    public IReadOnlyDictionary<string, RegisterAffinityDef> RegisterAffinities { get; init; } =
        new Dictionary<string, RegisterAffinityDef>
        {
            ["fantasy"] = new() { Elements = new[] { "fire", "ice", "chaos" } },
            ["scifi"] = new() { Elements = new[] { "electric" } },
            ["horror"] = new() { Elements = new[] { "spectral", "poison" }, Classes = new[] { "impact" } },
            ["normal"] = new() { Classes = new[] { "blunt", "object", "natural" } },
            ["fey"] = new() { Elements = new[] { "chaos" }, Classes = new[] { "object", "natural" } },
        };
}
