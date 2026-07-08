using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using Xunit;

namespace BrawlerSim.Tests.Genome;

public class StageGeneratorTests
{
    private static readonly StageGenerator Generator = GenerationConfig.Default.CreateStageGenerator();

    [Fact]
    public void SameSeedProducesIdenticalStages()
    {
        var a = Generator.Generate(new Pcg32(42)).Platforms;
        var b = Generator.Generate(new Pcg32(42)).Platforms;
        Assert.Equal(a, b);
    }

    [Fact]
    public void StagesAreMirroredAroundXZero()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            IReadOnlyList<PlatformGene> platforms = Generator.Generate(new Pcg32(seed)).Platforms;
            Assert.True(platforms.Count % 2 == 0, $"seed {seed}: odd platform count");
            int half = platforms.Count / 2;
            for (int i = 0; i < half; i++)
            {
                Assert.Equal(platforms[i].MirrorX(), platforms[half + i]);
            }
        }
    }

    [Fact]
    public void PlatformCountsAndSizesStayInBounds()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            IReadOnlyList<PlatformGene> platforms = Generator.Generate(new Pcg32(seed)).Platforms;
            // Growth loop can overshoot the target by one before mirroring doubles it.
            Assert.InRange(platforms.Count, 2, 2 * (GenerationConfig.Default.PlatformCount + 1));
            foreach (PlatformGene p in platforms)
            {
                Assert.True(p.XSize >= 1, $"seed {seed}: platform width {p.XSize}");
                Assert.True(p.YSize >= 1, $"seed {seed}: platform height {p.YSize}");
            }
        }
    }

    [Fact]
    public void FirstPlatformIsTheInitialOne()
    {
        var platforms = Generator.Generate(new Pcg32(7)).Platforms;
        Assert.Equal(StageGenerator.InitialY, platforms[0].Y);
        Assert.True(platforms[0].X < 0);
    }

    [Fact]
    public void NoPlatformExceedsTheDesignSpaceMaximumWidth()
    {
        // Regression guard for the absolute-vs-relative coordinate fix. With the Unity
        // bug, Above-children of negative-x parents could grow wider than any generation
        // rule allows (parent width capped at MaxPlatformSize); with the fix, every
        // platform's width is bounded by MaxPlatformSize.
        for (ulong seed = 0; seed < 500; seed++)
        {
            IReadOnlyList<PlatformGene> platforms = Generator.Generate(new Pcg32(seed)).Platforms;
            foreach (PlatformGene p in platforms)
            {
                Assert.True(p.XSize <= GenerationConfig.Default.MaxPlatformSize,
                    $"seed {seed}: platform width {p.XSize} exceeds MaxPlatformSize");
            }
        }
    }
}
