using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>One evolvable move: raw params plus a cosmetic sprite gene.</summary>
public sealed class MoveGenome
{
    public ParamSet Params { get; }
    public int SpriteIndex { get; }

    public MoveGenome(ParamSet @params, int spriteIndex)
    {
        Params = @params;
        SpriteIndex = spriteIndex;
    }

    public static MoveGenome Generate(GenerationConfig config, Pcg32 rng)
    {
        ParamSet raw = GenomeOps.Generate(config.MoveSchema, rng);
        return new MoveGenome(MoveRules.ConstrainKnockback(raw), rng.NextInt(config.MoveSpriteCount));
    }
}

/// <summary>One evolvable character: raw params, its moves, and fixed/cosmetic genes.</summary>
public sealed class CharacterGenome
{
    public string Name { get; }
    public int Stocks { get; }
    public int SpriteIndex { get; }
    public ParamSet Params { get; }
    public IReadOnlyList<MoveGenome> Moves { get; }

    public CharacterGenome(string name, int stocks, int spriteIndex, ParamSet @params, IEnumerable<MoveGenome> moves)
    {
        Name = name;
        Stocks = stocks;
        SpriteIndex = spriteIndex;
        Params = @params;
        Moves = moves.ToArray();
    }

    public static CharacterGenome Generate(string name, GenerationConfig config, Pcg32 rng)
    {
        ParamSet @params = GenomeOps.Generate(config.CharacterSchema, rng);
        int spriteIndex = rng.NextInt(config.PlayerSpriteCount);
        var moves = new List<MoveGenome>(config.MovesPerCharacter);
        for (int i = 0; i < config.MovesPerCharacter; i++)
        {
            moves.Add(MoveGenome.Generate(config, rng));
        }
        return new CharacterGenome(name, config.Stocks, spriteIndex, @params, moves);
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
