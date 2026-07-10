using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Evolution;

/// <summary>
/// Normalized behavioral-parameter distance between two genomes, used by the optional
/// diversity bonus (EvolutionConfig.DiversityWeight) and the popdiv research tool.
///
/// Definition (v1, 2026-07-09): mean over every character param and every move param
/// (positional pairing, matching crossover semantics) of |a−b| / generation-range
/// width. Cosmetic genes (sprites), button mappings, and the stage are EXCLUDED — this
/// measures the mechanics design space. Result is ~[0,1]: 0 = identical mechanics,
/// 1 = every parameter a full generation-range apart.
/// </summary>
public static class GenomeDistance
{
    public static float Normalized(GameGenome a, GameGenome b, GenerationConfig config)
    {
        if (a.Characters.Count != b.Characters.Count)
        {
            throw new ArgumentException("Genomes must have the same character count.");
        }
        float sum = 0f;
        int dims = 0;
        for (int c = 0; c < a.Characters.Count; c++)
        {
            CharacterGenome ca = a.Characters[c], cb = b.Characters[c];
            Accumulate(config.CharacterSchema, ca.Params, cb.Params, ref sum, ref dims);
            int moves = Math.Min(ca.Moves.Count, cb.Moves.Count);
            for (int m = 0; m < moves; m++)
            {
                Accumulate(config.MoveSchema, ca.Moves[m].Params, cb.Moves[m].Params, ref sum, ref dims);
            }
        }
        return dims == 0 ? 0f : sum / dims;
    }

    /// <summary>Mean pairwise Normalized() over a population (the popdiv metric).</summary>
    public static float MeanPairwise(IReadOnlyList<GameGenome> population, GenerationConfig config)
    {
        if (population.Count < 2)
        {
            return 0f;
        }
        double sum = 0;
        int pairs = 0;
        for (int i = 0; i < population.Count; i++)
        {
            for (int j = i + 1; j < population.Count; j++)
            {
                sum += Normalized(population[i], population[j], config);
                pairs++;
            }
        }
        return (float)(sum / pairs);
    }

    private static void Accumulate(ParamSchema schema, ParamSet a, ParamSet b, ref float sum, ref int dims)
    {
        foreach (ParamSpec spec in schema.Specs)
        {
            float width = spec.Max - spec.Min;
            if (width <= 0f)
            {
                continue;
            }
            sum += Math.Abs(a.Get(spec.Key) - b.Get(spec.Key)) / width;
            dims++;
        }
    }
}
