using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>What a move slot IS (2026-07-12, FEATURES.md §Shield). A structural gene:
/// slots of the same type cross params positionally; mismatched slots coin-flip the
/// whole move from one parent (see GameGenomeOps).</summary>
public enum MoveType
{
    Attack,
    Shield,
    Dash, // 2026-07-13, FEATURES.md §Dash
}

/// <summary>One evolvable move: type, raw params (schema per type), and a cosmetic
/// sprite gene (unused visually by shields — the circle is procedural — but kept so
/// gene structure is uniform).</summary>
public sealed class MoveGenome
{
    public MoveType Type { get; }
    public ParamSet Params { get; }
    public int SpriteIndex { get; }

    public MoveGenome(ParamSet @params, int spriteIndex, MoveType type = MoveType.Attack)
    {
        Type = type;
        Params = @params;
        SpriteIndex = spriteIndex;
    }

    public static MoveGenome Generate(GenerationConfig config, Pcg32 rng)
    {
        ParamSet raw = GenomeOps.Generate(config.MoveSchema, rng);
        return new MoveGenome(MoveRules.ConstrainKnockback(raw), rng.NextInt(config.MoveSpriteCount));
    }

    /// <summary>All nine shield parameters are generated (and hence evolved) freely;
    /// only the slot TYPE is pinned by the current composition.</summary>
    public static MoveGenome GenerateShield(GenerationConfig config, Pcg32 rng)
    {
        ParamSet raw = GenomeOps.Generate(config.ShieldSchema, rng);
        return new MoveGenome(raw, rng.NextInt(config.MoveSpriteCount), MoveType.Shield);
    }

    public static MoveGenome GenerateDash(GenerationConfig config, Pcg32 rng)
    {
        ParamSet raw = GenomeOps.Generate(config.DashSchema, rng);
        return new MoveGenome(raw, rng.NextInt(config.MoveSpriteCount), MoveType.Dash);
    }

    public static MoveGenome GenerateOfType(MoveType type, GenerationConfig config, Pcg32 rng) => type switch
    {
        MoveType.Shield => GenerateShield(config, rng),
        MoveType.Dash => GenerateDash(config, rng),
        _ => Generate(config, rng),
    };

    /// <summary>Resolve a composed slot spec (2026-07-14): fixed specs map directly;
    /// Random draws uniformly over the three move types (one RNG draw).</summary>
    public static MoveType ResolveSlot(SlotSpec spec, Pcg32 rng) => spec switch
    {
        SlotSpec.Attack => MoveType.Attack,
        SlotSpec.Shield => MoveType.Shield,
        SlotSpec.Dash => MoveType.Dash,
        _ => (MoveType)rng.NextInt(3),
    };
}

/// <summary>One evolvable character: raw params, its moves, and fixed/cosmetic genes.</summary>
public sealed class CharacterGenome
{
    public string Name { get; }
    public int Stocks { get; }
    public int SpriteIndex { get; }
    public ParamSet Params { get; }
    public IReadOnlyList<MoveGenome> Moves { get; }

    /// <summary>
    /// Button→move mapping gene (2026-07-08, docs/features/multi-move-controls.md):
    /// ButtonMoves[b] is the index into Moves triggered by action button b. Length is
    /// always InputFrame.ActionCount. A structural int gene (like SpriteIndex), not a
    /// ParamSpec: its valid range [0, Moves.Count-1] depends on the move count, which a
    /// static schema range cannot express.
    /// </summary>
    public IReadOnlyList<int> ButtonMoves { get; }

    public CharacterGenome(string name, int stocks, int spriteIndex, ParamSet @params, IEnumerable<MoveGenome> moves,
        IEnumerable<int>? buttonMoves = null)
    {
        Name = name;
        Stocks = stocks;
        SpriteIndex = spriteIndex;
        Params = @params;
        Moves = moves.ToArray();
        ButtonMoves = buttonMoves?.ToArray() ?? new int[Sim.InputFrame.ActionCount];
        if (ButtonMoves.Count != Sim.InputFrame.ActionCount)
        {
            throw new ArgumentException(
                $"buttonMoves must have {Sim.InputFrame.ActionCount} entries, got {ButtonMoves.Count}.");
        }
        int maxMove = Math.Max(0, Moves.Count - 1);
        foreach (int move in ButtonMoves)
        {
            if (move < 0 || move > maxMove)
            {
                throw new ArgumentException(
                    $"buttonMoves entry {move} is outside the move list (0..{maxMove}).");
            }
        }
    }

    public static CharacterGenome Generate(string name, GenerationConfig config, Pcg32 rng)
    {
        ParamSet @params = GenomeOps.Generate(config.CharacterSchema, rng);
        int spriteIndex = rng.NextInt(config.PlayerSpriteCount);
        if (config.ButtonComposition is { } composition)
        {
            // Composed mode (2026-07-14): one move per button, identity mapping. Per
            // slot: type resolution (a draw only for Random specs), then that type's
            // generation draws. The mapping gene is structural identity — "which type
            // sits on which button" evolves via the type gene, not the mapping.
            var slotMoves = new List<MoveGenome>(composition.Count);
            int[] identity = new int[composition.Count];
            for (int i = 0; i < composition.Count; i++)
            {
                slotMoves.Add(MoveGenome.GenerateOfType(MoveGenome.ResolveSlot(composition[i], rng), config, rng));
                identity[i] = i;
            }
            return new CharacterGenome(name, config.Stocks, spriteIndex, @params, slotMoves, identity);
        }
        var moves = new List<MoveGenome>(config.MovesPerCharacter + config.ShieldSlotCount + config.DashSlotCount);
        for (int i = 0; i < config.MovesPerCharacter; i++)
        {
            moves.Add(MoveGenome.Generate(config, rng));
        }
        for (int i = 0; i < config.ShieldSlotCount; i++)
        {
            moves.Add(MoveGenome.GenerateShield(config, rng));
        }
        for (int i = 0; i < config.DashSlotCount; i++)
        {
            moves.Add(MoveGenome.GenerateDash(config, rng)); // dash is always the LAST slot
        }
        // Button genes consume RNG only when there is a real choice (>1 move), so
        // single-move populations reproduce pre-feature RNG streams bit-exactly.
        // Dash pin (2026-07-13, designer): the dash owns the LAST button (right
        // shoulder / L); other moves are mapped over the remaining buttons.
        int mappableButtons = config.DashSlotCount > 0
            ? Sim.InputFrame.ActionCount - 1 : Sim.InputFrame.ActionCount;
        int nonDashMoves = moves.Count - config.DashSlotCount;
        int[] buttonMoves = new int[Sim.InputFrame.ActionCount];
        if (nonDashMoves > 1)
        {
            for (int b = 0; b < mappableButtons; b++)
            {
                buttonMoves[b] = rng.NextInt(nonDashMoves);
            }
        }
        EnsureButtonCoverage(buttonMoves, nonDashMoves, mappableButtons);
        if (config.DashSlotCount > 0)
        {
            buttonMoves[Sim.InputFrame.ActionCount - 1] = moves.Count - 1;
        }
        return new CharacterGenome(name, config.Stocks, spriteIndex, @params, moves, buttonMoves);
    }

    /// <summary>
    /// Coverage guarantee (2026-07-10, docs/features/second-move.md): every move must
    /// be reachable from at least one button. Each unmapped move (ascending) takes the
    /// FIRST button whose move is mapped elsewhere too (a duplicate) — by pigeonhole
    /// such a button exists whenever moves ≤ buttons, and overwriting it can never
    /// unmap anything. Deterministic and RNG-free; a no-op for covering mappings.
    /// </summary>
    public static int[] EnsureButtonCoverage(int[] buttonMoves, int moveCount)
        => EnsureButtonCoverage(buttonMoves, moveCount, buttonMoves.Length);

    /// <summary>Coverage over the FIRST buttonLimit buttons only — buttons past the
    /// limit are reserved (the dash pin) and neither counted nor repaired.</summary>
    public static int[] EnsureButtonCoverage(int[] buttonMoves, int moveCount, int buttonLimit)
    {
        if (moveCount > buttonLimit)
        {
            throw new ArgumentException(
                $"{moveCount} moves cannot all be mapped onto {buttonLimit} buttons.");
        }
        if (moveCount == 0)
        {
            return buttonMoves;
        }
        Span<int> uses = stackalloc int[moveCount];
        for (int b = 0; b < buttonLimit; b++)
        {
            uses[buttonMoves[b]]++;
        }
        for (int m = 0; m < moveCount; m++)
        {
            if (uses[m] > 0)
            {
                continue;
            }
            for (int b = 0; b < buttonLimit; b++)
            {
                if (uses[buttonMoves[b]] > 1)
                {
                    uses[buttonMoves[b]]--;
                    buttonMoves[b] = m;
                    uses[m] = 1;
                    break;
                }
            }
        }
        return buttonMoves;
    }
}

/// <summary>
/// A complete candidate game — the unit the evolutionary algorithm breeds and the
/// fitness function scores. Characters are positional (index 0 spawns left).
/// </summary>
public sealed class GameGenome
{
    public IReadOnlyList<CharacterGenome> Characters { get; }
    public StageGenome Stage { get; }

    public GameGenome(IEnumerable<CharacterGenome> characters, StageGenome stage)
    {
        Characters = characters.ToArray();
        if (Characters.Count < 2)
        {
            throw new ArgumentException("A game needs at least two characters.");
        }
        Stage = stage;
    }

    public static GameGenome Generate(GenerationConfig config, Pcg32 rng)
    {
        StageGenome stage = config.CreateStageGenerator().Generate(rng);
        var characters = new List<CharacterGenome>(config.CharacterCount);
        for (int i = 0; i < config.CharacterCount; i++)
        {
            characters.Add(CharacterGenome.Generate($"Player {i + 1}", config, rng));
        }
        return new GameGenome(characters, stage);
    }

    /// <summary>All range violations across every segment; empty when valid.</summary>
    public List<string> Validate()
    {
        var violations = new List<string>();
        foreach (CharacterGenome character in Characters)
        {
            violations.AddRange(character.Params.Validate());
            foreach (MoveGenome move in character.Moves)
            {
                violations.AddRange(move.Params.Validate());
            }
        }
        return violations;
    }
}
