using BrawlerSim.Determinism;

namespace BrawlerSim.Params;

/// <summary>
/// Schema-generic genetic operations. These reproduce the Unity generator's semantics
/// exactly (documented per-method) so evolution dynamics carry over by design.
/// </summary>
public static class GenomeOps
{
    /// <summary>Uniform random value for every spec in the schema.</summary>
    public static ParamSet Generate(ParamSchema schema, Pcg32 rng)
    {
        float[] values = new float[schema.Count];
        for (int i = 0; i < schema.Count; i++)
        {
            values[i] = rng.NextFloat(schema[i].Min, schema[i].Max);
        }
        return new ParamSet(schema, values);
    }

    /// <summary>
    /// Single-point crossover, Unity parity: point is drawn in [0, count); indices
    /// below the point come from <paramref name="a"/>, the rest from <paramref name="b"/>.
    /// A point of 0 therefore yields a full copy of <paramref name="b"/>.
    /// </summary>
    public static ParamSet SinglePointCrossover(ParamSet a, ParamSet b, Pcg32 rng)
    {
        RequireSameSchema(a, b);
        int point = rng.NextInt(a.Schema.Count);
        float[] values = new float[a.Schema.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i < point ? a[i] : b[i];
        }
        return new ParamSet(a.Schema, values);
    }

    /// <summary>
    /// Re-rolls <paramref name="rerolls"/> randomly chosen params from their generation
    /// ranges. Unity parity: indices are drawn WITH replacement, so fewer than
    /// <paramref name="rerolls"/> distinct params may change.
    /// </summary>
    public static ParamSet Mutate(ParamSet source, Pcg32 rng, int rerolls = 5)
    {
        float[] values = source.ToArray();
        for (int i = 0; i < rerolls; i++)
        {
            int index = rng.NextInt(source.Schema.Count);
            values[index] = rng.NextFloat(source.Schema[index].Min, source.Schema[index].Max);
        }
        return new ParamSet(source.Schema, values);
    }

    private static void RequireSameSchema(ParamSet a, ParamSet b)
    {
        if (!ReferenceEquals(a.Schema, b.Schema))
        {
            throw new ArgumentException(
                $"Cannot cross param sets from different schemas ('{a.Schema.Name}' vs '{b.Schema.Name}').");
        }
    }
}
