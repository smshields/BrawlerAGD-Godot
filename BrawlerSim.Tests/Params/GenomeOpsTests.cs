using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using Xunit;

namespace BrawlerSim.Tests.Params;

public class GenomeOpsTests
{
    private static readonly ParamSchema Schema = DefaultSchemas.Character;

    [Fact]
    public void GenerateStaysInRangeAndIsDeterministic()
    {
        var a = GenomeOps.Generate(Schema, new Pcg32(7, 1));
        var b = GenomeOps.Generate(Schema, new Pcg32(7, 1));
        for (int i = 0; i < Schema.Count; i++)
        {
            Assert.True(Schema[i].Contains(a[i]), $"{Schema[i].Key}={a[i]} out of range");
            Assert.Equal(a[i], b[i]); // exact: same seed, same arithmetic
        }
        Assert.Empty(a.Validate());
    }

    [Fact]
    public void CrossoverTakesPrefixFromAThenSuffixFromB()
    {
        // Parents with disjoint constant values make the crossover point observable.
        var a = new ParamSet(Schema, Enumerable.Range(0, Schema.Count).Select(i => Schema[i].Min).ToArray());
        var b = new ParamSet(Schema, Enumerable.Range(0, Schema.Count).Select(i => Schema[i].Max).ToArray());

        var child = GenomeOps.SinglePointCrossover(a, b, new Pcg32(3));

        bool switched = false;
        for (int i = 0; i < Schema.Count; i++)
        {
            bool fromB = child[i] == Schema[i].Max;
            if (fromB) switched = true;
            // Once values come from b, they must keep coming from b (single point).
            Assert.True(!switched || fromB, $"index {i} reverted to parent A after the crossover point");
        }
        Assert.True(switched, "point == count never happens; the tail always comes from B");
    }

    [Fact]
    public void CrossoverAcrossSchemasThrows()
    {
        var a = GenomeOps.Generate(DefaultSchemas.Character, new Pcg32(1));
        var b = GenomeOps.Generate(DefaultSchemas.Move, new Pcg32(2));
        Assert.Throws<ArgumentException>(() => GenomeOps.SinglePointCrossover(a, b, new Pcg32(3)));
    }

    [Fact]
    public void MutateChangesAtMostFiveParamsAndStaysValid()
    {
        var source = GenomeOps.Generate(Schema, new Pcg32(11));
        var mutated = GenomeOps.Mutate(source, new Pcg32(12));

        int changed = 0;
        for (int i = 0; i < Schema.Count; i++)
        {
            if (source[i] != mutated[i]) changed++;
        }
        // Unity parity: 5 draws WITH replacement — between 1 and 5 distinct params change
        // (0 would require re-rolling identical floats, which does not happen in practice).
        Assert.InRange(changed, 1, 5);
        Assert.Empty(mutated.Validate());
    }

    [Fact]
    public void MutateIsDeterministic()
    {
        var source = GenomeOps.Generate(Schema, new Pcg32(11));
        var m1 = GenomeOps.Mutate(source, new Pcg32(99));
        var m2 = GenomeOps.Mutate(source, new Pcg32(99));
        Assert.Equal(m1.ToArray(), m2.ToArray());
    }

    [Fact]
    public void ValidateFlagsOutOfRangeAndNaN()
    {
        float[] values = GenomeOps.Generate(Schema, new Pcg32(5)).ToArray();
        values[0] = Schema[0].Max + 1f;
        values[3] = float.NaN;
        var violations = new ParamSet(Schema, values).Validate();
        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void FromDictionaryRequiresAllKeysAndIgnoresExtras()
    {
        var dict = GenomeOps.Generate(Schema, new Pcg32(8)).ToDictionary();
        dict["legacyDerivedField"] = 123f; // extras (e.g. Unity's groundAcceleration) are ignored
        var set = ParamSet.FromDictionary(Schema, dict);
        Assert.Empty(set.Validate());

        dict.Remove(CharacterParams.Mass);
        Assert.Throws<KeyNotFoundException>(() => ParamSet.FromDictionary(Schema, dict));
    }
}
