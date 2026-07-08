using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>
/// Game-level genetic operations, Unity parity throughout:
/// - Crossover pairs characters positionally (a.P1 × b.P1, a.P2 × b.P2), crosses each
///   character's params, each corresponding move's params, and the stage platform list;
///   sprite genes come from a coin-flipped parent; names/stocks from parent A.
/// - Mutation is all-or-none per game: ONE roll against the mutation rate decides whether
///   every segment mutates (5 param re-rolls each, sprites re-randomized, stage fully
///   regenerated) or none does.
/// </summary>
public static class GameGenomeOps
{
    public static GameGenome Crossover(GameGenome a, GameGenome b, Pcg32 rng, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        if (a.Characters.Count != b.Characters.Count)
        {
            throw new ArgumentException(
                $"Cannot cross games with different character counts ({a.Characters.Count} vs {b.Characters.Count}).");
        }

        var children = new List<CharacterGenome>(a.Characters.Count);
        for (int c = 0; c < a.Characters.Count; c++)
        {
            children.Add(CrossoverCharacter(a.Characters[c], b.Characters[c], rng));
        }
        StageGenome stage = StageGenome.SinglePointCrossover(a.Stage, b.Stage, rng);
        return new GameGenome(children, stage);
    }

    public static GameGenome Mutate(GameGenome genome, Pcg32 rng, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        var mutated = new List<CharacterGenome>(genome.Characters.Count);
        foreach (CharacterGenome character in genome.Characters)
        {
            var moves = character.Moves
                .Select(m => new MoveGenome(GenomeOps.Mutate(m.Params, rng), rng.NextInt(config.MoveSpriteCount)))
                .ToList();
            mutated.Add(new CharacterGenome(
                character.Name,
                character.Stocks,
                rng.NextInt(config.PlayerSpriteCount),
                GenomeOps.Mutate(character.Params, rng),
                moves));
        }
        StageGenome stage = config.CreateStageGenerator().Generate(rng);
        return new GameGenome(mutated, stage);
    }

    /// <summary>Crossover followed by the single all-or-none mutation roll.</summary>
    public static GameGenome Breed(GameGenome a, GameGenome b, float mutationRate, Pcg32 rng, GenerationConfig? config = null)
    {
        GameGenome child = Crossover(a, b, rng, config);
        if (rng.NextFloat() < mutationRate)
        {
            child = Mutate(child, rng, config);
        }
        return child;
    }

    private static CharacterGenome CrossoverCharacter(CharacterGenome a, CharacterGenome b, Pcg32 rng)
    {
        if (a.Moves.Count != b.Moves.Count)
        {
            throw new ArgumentException(
                $"Cannot cross characters with different move counts ({a.Moves.Count} vs {b.Moves.Count}).");
        }
        ParamSet childParams = GenomeOps.SinglePointCrossover(a.Params, b.Params, rng);
        int spriteIndex = rng.NextInt(2) == 0 ? a.SpriteIndex : b.SpriteIndex;
        var moves = new List<MoveGenome>(a.Moves.Count);
        for (int m = 0; m < a.Moves.Count; m++)
        {
            ParamSet moveParams = GenomeOps.SinglePointCrossover(a.Moves[m].Params, b.Moves[m].Params, rng);
            int moveSprite = rng.NextInt(2) == 0 ? a.Moves[m].SpriteIndex : b.Moves[m].SpriteIndex;
            moves.Add(new MoveGenome(moveParams, moveSprite));
        }
        return new CharacterGenome(a.Name, a.Stocks, spriteIndex, childParams, moves);
    }
}
