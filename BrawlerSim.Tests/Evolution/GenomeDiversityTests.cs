using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

/// <summary>GenomeDistance and the opt-in DiversityWeight (2026-07-09 noise study).</summary>
public class GenomeDiversityTests
{
    private static GameGenome Arena(params (string Key, float Value)[] characterOverrides) =>
        TestGames.FlatArena(characterOverrides);

    [Fact]
    public void IdenticalGenomesHaveZeroDistance()
    {
        Assert.Equal(0f, GenomeDistance.Normalized(Arena(), Arena(), GenerationConfig.Default));
    }

    [Fact]
    public void DistanceIsHandComputable()
    {
        // One character param moved by half its generation range: maxGroundSpeed 4→8
        // out of [2,10] → normalized 0.5 on that dimension for ONE character.
        // Dimensions: 2 chars × (12 char params + 12 move params) = 48.
        // Expected: 0.5 / 48.
        GameGenome a = Arena();
        var b = new GameGenome(new[]
        {
            new CharacterGenome("P1", 3, 0,
                TestGames.Character((CharacterParams.MaxGroundSpeed, 8f)),
                new[] { new MoveGenome(TestGames.Move(), 0) }),
            a.Characters[1],
        }, a.Stage);

        float distance = GenomeDistance.Normalized(a, b, GenerationConfig.Default);
        Assert.Equal(0.5 / 48.0, distance, 6);
    }

    [Fact]
    public void MeanPairwiseIsZeroForClonesAndPositiveForVariedPopulations()
    {
        var clones = new List<GameGenome> { Arena(), Arena(), Arena() };
        Assert.Equal(0f, GenomeDistance.MeanPairwise(clones, GenerationConfig.Default));

        var rng = new Pcg32(5);
        var varied = new List<GameGenome>
        {
            GameGenome.Generate(GenerationConfig.Default, rng),
            GameGenome.Generate(GenerationConfig.Default, rng),
            GameGenome.Generate(GenerationConfig.Default, rng),
        };
        Assert.True(GenomeDistance.MeanPairwise(varied, GenerationConfig.Default) > 0.05f);
    }

    [Fact]
    public void ZeroDiversityWeightIsExactlyLegacySelection()
    {
        var baseline = new EvolutionConfig { Seed = 77, PopulationSize = 12, RoundsPerIndividual = 1 };
        var explicitZero = baseline with { DiversityWeight = 0f };

        var a = new EvolutionEngine(baseline);
        var b = new EvolutionEngine(explicitZero);
        for (int gen = 0; gen < 3; gen++)
        {
            GenerationStats sa = a.Step();
            GenerationStats sb = b.Step();
            Assert.Equal(sa.TopFitness, sb.TopFitness);
            Assert.Equal(sa.AverageFitness, sb.AverageFitness);
            Assert.Equal(sa.BestIndex, sb.BestIndex);
        }
        Assert.Equal(a.RngSnapshot, b.RngSnapshot);
    }

    [Fact]
    public void DiversityWeightIsDeterministicAndKeepsRawStats()
    {
        var config = new EvolutionConfig
        {
            Seed = 78, PopulationSize = 12, RoundsPerIndividual = 1, DiversityWeight = 30f,
        };
        var a = new EvolutionEngine(config);
        var b = new EvolutionEngine(config);
        for (int gen = 0; gen < 3; gen++)
        {
            GenerationStats sa = a.Step();
            GenerationStats sb = b.Step();
            Assert.Equal(sa.TopFitness, sb.TopFitness);
            Assert.Equal(sa.BestIndex, sb.BestIndex);
            // TopFitness is RAW: it must equal the max of the recorded raw fitness.
            Assert.Equal(a.LastFitness.Max(), sa.TopFitness);
        }
    }

    [Fact]
    public void DiversityWeightPreservesMorePopulationSpread()
    {
        // The property the knob exists for: after some generations, the shared-fitness
        // population keeps a higher mean pairwise distance than the raw one (same seed).
        var raw = new EvolutionConfig { Seed = 79, PopulationSize = 16, RoundsPerIndividual = 1 };
        var shared = raw with { DiversityWeight = 60f };

        var a = new EvolutionEngine(raw);
        var b = new EvolutionEngine(shared);
        for (int gen = 0; gen < 12; gen++)
        {
            a.Step();
            b.Step();
        }
        float rawDiv = GenomeDistance.MeanPairwise(a.Population, GenerationConfig.Default);
        float sharedDiv = GenomeDistance.MeanPairwise(b.Population, GenerationConfig.Default);
        Assert.True(sharedDiv > rawDiv,
            $"diversity bonus did not preserve spread: raw {rawDiv:F4} vs shared {sharedDiv:F4}");
    }
}
