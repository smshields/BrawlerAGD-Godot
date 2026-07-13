using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Integration;

/// <summary>
/// End-to-end exercise of everything Phase 1 built: seeded generation → repeated
/// crossover/mutation → serialization, with determinism verified by fingerprint.
/// </summary>
public class Phase1PipelineTests
{
    private const int PopulationSize = 20;
    private const int Generations = 10;
    private const float MutationRate = 0.4f;

    private static ulong RunPipeline(ulong seed)
    {
        var rng = new Pcg32(seed);
        var population = new List<GameGenome>(PopulationSize);
        for (int i = 0; i < PopulationSize; i++)
        {
            population.Add(GameGenome.Generate(GenerationConfig.Default, rng));
        }

        for (int gen = 0; gen < Generations; gen++)
        {
            var next = new List<GameGenome>(PopulationSize);
            for (int i = 0; i < PopulationSize; i++)
            {
                GameGenome a = population[rng.NextInt(PopulationSize)];
                GameGenome b = population[rng.NextInt(PopulationSize)];
                GameGenome child = GameGenomeOps.Breed(a, b, MutationRate, rng);
                Assert.Empty(child.Validate());
                next.Add(child);
            }
            population = next;
        }

        ulong hash = Fnv1a.OffsetBasis;
        foreach (GameGenome genome in population)
        {
            string json = GameGenomeJson.Serialize(new GameRecord("g", null, genome));
            hash = Fnv1a.Hash(Encoding.UTF8.GetBytes(json), hash);
        }
        return hash;
    }

    [Fact]
    public void TenGenerationsOfBreedingStayValidAndDeterministic()
    {
        Assert.Equal(RunPipeline(20260707), RunPipeline(20260707));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentPopulations()
    {
        Assert.NotEqual(RunPipeline(1), RunPipeline(2));
    }

    /// <summary>
    /// Cross-platform / cross-runtime canary. This value was produced on macOS ARM64,
    /// .NET 8; CI runs Linux x64. If the two ever disagree, some operation in the genome
    /// pipeline is not bit-deterministic across platforms (prime suspect: transcendental
    /// functions behind DetMath) and must be hardened per the determinism contract —
    /// treat a failure here as a release blocker, not a flaky test.
    /// </summary>
    [Fact]
    public void PopulationFingerprintMatchesGoldenValue()
    {
        // Re-pinned 2026-07-13 (2nd): seven character-schema appends (fast fall /
        // crouch / DI) — new generation draws, a REAL design-space change. Prior
        // pins: 16079587979934170348 (dash slot), 10607725140721060960 (shield),
        // 5432710911100783110 (two moves), 13551893661434631362, 9300943650238635838.
        Assert.Equal(5768454974650524447UL, RunPipeline(20260707));
    }
}
