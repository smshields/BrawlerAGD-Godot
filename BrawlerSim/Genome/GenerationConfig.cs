using BrawlerSim.Params;

namespace BrawlerSim.Genome;

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

    public StageGenerator CreateStageGenerator() =>
        new(JumpHeight, JumpLength, PlatformCount, MaxPlatformSize);

    public static readonly GenerationConfig Default = new();
}
