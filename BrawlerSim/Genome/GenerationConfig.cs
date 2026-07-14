using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>What a composed button slot may hold (2026-07-14,
/// docs/features/evolve-composition-and-ranges.md). Random = the type is a structural
/// gene: drawn at generation and re-rollable by mutation (TypeRerollRate).</summary>
public enum SlotSpec
{
    Attack,
    Shield,
    Dash,
    Random,
}

/// <summary>One user-adjusted generation range (advanced evolve menu / CLI --range).
/// Schema is the ParamSchema name ("character", "move", "shield", "dash").</summary>
public readonly record struct RangeOverride(string Schema, string Key, float Min, float Max);

/// <summary>
/// Everything the generator needs to create a fresh game genome. Defaults reproduce the
/// Unity setup — MapGenerator(2, 2, 3, 6), 3 stocks, the Kenney sprite-sheet slice
/// counts (players 87, moves 265) — EXCEPT MovesPerCharacter, which became 2 on
/// 2026-07-10 (docs/features/second-move.md; Unity had one move).
/// </summary>
public sealed record GenerationConfig
{
    public ParamSchema CharacterSchema { get; init; } = DefaultSchemas.Character;
    public ParamSchema MoveSchema { get; init; } = DefaultSchemas.Move;

    public ParamSchema ShieldSchema { get; init; } = DefaultSchemas.Shield;
    public ParamSchema DashSchema { get; init; } = DefaultSchemas.Dash;

    public int CharacterCount { get; init; } = 2;
    public int MovesPerCharacter { get; init; } = 2;

    /// <summary>Shield slots appended after the attack slots (2026-07-12; guaranteed
    /// composition for now — dynamic type assignment is a future flag; set 0 to
    /// disable shields in generation).</summary>
    public int ShieldSlotCount { get; init; } = 1;

    /// <summary>Dash slots appended LAST (2026-07-13), each pinned to the final action
    /// button (right shoulder / L) — designer clamp until dynamic composition. With
    /// 2 attacks + shield + dash on 4 buttons the mapping gene is a fixed bijection.</summary>
    public int DashSlotCount { get; init; } = 1;
    public int Stocks { get; init; } = 3;

    public int PlayerSpriteCount { get; init; } = 87;
    public int MoveSpriteCount { get; init; } = 265;

    public int JumpHeight { get; init; } = 2;
    public int JumpLength { get; init; } = 2;
    public int PlatformCount { get; init; } = 3;
    public int MaxPlatformSize { get; init; } = 6;

    /// <summary>Per-button composition (2026-07-14). Null (default) = the PINNED legacy
    /// layout above — that path must stay byte-identical (fingerprint golden). Non-null:
    /// exactly one move per button, buttonMoves = identity, slot i's type from spec i
    /// (Random = drawn at generation, re-rollable by mutation).</summary>
    public IReadOnlyList<SlotSpec>? ButtonComposition { get; init; }

    /// <summary>Chance, per RANDOM-spec slot of a game that rolled mutation, that the
    /// slot regenerates wholesale (uniform type draw + fresh params/sprite) instead of
    /// mutating its params. Fixed-spec slots never roll (RNG-gating).</summary>
    public float TypeRerollRate { get; init; } = 0.2f;

    public bool IsComposed => ButtonComposition is not null;

    /// <summary>RANDOM mode: every button free.</summary>
    public static IReadOnlyList<SlotSpec> RandomComposition { get; } =
        Enumerable.Repeat(SlotSpec.Random, Sim.InputFrame.ActionCount).ToArray();

    /// <summary>The active range overrides, recorded in run.json (empty = stock schemas).</summary>
    public IReadOnlyList<RangeOverride> RangeOverrides { get; init; } = Array.Empty<RangeOverride>();

    /// <summary>
    /// Rebuilds the four schemas with user generation ranges substituted (advanced
    /// evolve menu; designer-chosen UNRESTRICTED — ranges may exceed the tested valid
    /// domain, in which case ValidMin/Max widen to preserve the invariant
    /// "valid domain ⊇ generation range" so Validate() stays coherent). Clamping a
    /// param = min == max. Every genome in a run binds to the single rebuilt schema
    /// instances, satisfying GenomeOps.RequireSameSchema.
    /// </summary>
    public GenerationConfig WithRangeOverrides(IReadOnlyList<RangeOverride> overrides)
    {
        if (overrides.Count == 0)
        {
            return this with { RangeOverrides = Array.Empty<RangeOverride>() };
        }
        foreach (RangeOverride o in overrides)
        {
            if (o.Min > o.Max)
            {
                throw new ArgumentException($"Range for {o.Schema}.{o.Key} has min > max ({o.Min} > {o.Max}).");
            }
        }
        return this with
        {
            RangeOverrides = overrides.ToArray(),
            CharacterSchema = ApplyOverrides(CharacterSchema, overrides),
            MoveSchema = ApplyOverrides(MoveSchema, overrides),
            ShieldSchema = ApplyOverrides(ShieldSchema, overrides),
            DashSchema = ApplyOverrides(DashSchema, overrides),
        };
    }

    private static ParamSchema ApplyOverrides(ParamSchema schema, IReadOnlyList<RangeOverride> overrides)
    {
        List<ParamSpec>? specs = null;
        foreach (RangeOverride o in overrides)
        {
            if (!string.Equals(o.Schema, schema.Name, StringComparison.Ordinal) || !schema.ContainsKey(o.Key))
            {
                continue;
            }
            specs ??= schema.Specs.ToList();
            int i = schema.IndexOf(o.Key);
            ParamSpec stock = specs[i];
            specs[i] = new ParamSpec(o.Key, o.Min, o.Max)
            {
                ValidMin = Math.Min(stock.EffectiveValidMin, o.Min),
                ValidMax = Math.Max(stock.EffectiveValidMax, o.Max),
            };
        }
        return specs is null ? schema : new ParamSchema(schema.Name, specs);
    }

    public StageGenerator CreateStageGenerator() =>
        new(JumpHeight, JumpLength, PlatformCount, MaxPlatformSize);

    public static readonly GenerationConfig Default = new();
}
