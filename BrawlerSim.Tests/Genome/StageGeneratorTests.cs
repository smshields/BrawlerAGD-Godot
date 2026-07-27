using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using Xunit;

namespace BrawlerSim.Tests.Genome;

/// <summary>
/// Property tests for the Map Size generator (2026-07-21, docs/features/map-size.md).
/// The pre-feature tests pinned the Unity tree's fixed frame (InitialY, count ≤ 8);
/// the generator's guarantees are now the FEATURES.md §Map Size rules, checked across
/// a seed sweep.
/// </summary>
public class StageGeneratorTests
{
    private const int Seeds = 500;

    private static readonly StageGenerator Generator = GenerationConfig.Default.CreateStageGenerator();

    private static (IReadOnlyList<PlatformGene> Platforms, ParamSet Params) Gen(ulong seed)
    {
        StageGenome stage = Generator.Generate(new Pcg32(seed));
        return (stage.Platforms, stage.Params);
    }

    [Fact]
    public void SameSeedProducesIdenticalStages()
    {
        StageGenome a = Generator.Generate(new Pcg32(42));
        StageGenome b = Generator.Generate(new Pcg32(42));
        Assert.Equal(a.Platforms, b.Platforms);
        Assert.Equal(a.Params.ToArray(), b.Params.ToArray());
    }

    [Fact]
    public void MirroredGeneProducesSymmetricStages()
    {
        int mirrored = 0;
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, ParamSet stageParams) = Gen(seed);
            if (StageRules.IsMirrored(stageParams))
            {
                mirrored++;
                Assert.True(StageRules.IsSymmetric(platforms), $"seed {seed}: mirrored gene but asymmetric layout");
            }
        }
        // The mirrored gene is a coin — both kinds must actually occur in the sweep.
        Assert.InRange(mirrored, Seeds / 4, Seeds * 3 / 4);
    }

    [Fact]
    public void PlatformsNeverOverlap()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, _) = Gen(seed);
            for (int i = 0; i < platforms.Count; i++)
            {
                for (int j = i + 1; j < platforms.Count; j++)
                {
                    Assert.False(StageRules.Overlaps(platforms[i], platforms[j]),
                        $"seed {seed}: platforms {i} and {j} overlap");
                }
            }
        }
    }

    [Fact]
    public void PlatformGraphIsTraversable()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, _) = Gen(seed);
            Assert.True(
                StageRules.IsConnected(platforms,
                    GenerationConfig.Default.JumpHeight, GenerationConfig.Default.JumpLength),
                $"seed {seed}: platform graph is not jump-connected");
        }
    }

    [Fact]
    public void EveryPlatformIntersectsTheVisibleBox()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, ParamSet stageParams) = Gen(seed);
            float visW = stageParams.Get(StageParams.VisibleHalfWidth);
            float visH = stageParams.Get(StageParams.VisibleHalfHeight);
            foreach (PlatformGene p in platforms)
            {
                Assert.True(
                    p.X < visW && p.X + p.XSize > -visW && p.Y < visH && p.Y + p.YSize > -visH,
                    $"seed {seed}: platform {p} misses the visible box ({visW}×{visH})");
            }
        }
    }

    [Fact]
    public void PlatformCountAndSizeRespectTheirGenes()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, ParamSet stageParams) = Gen(seed);
            Assert.InRange(platforms.Count, 1, StageRules.PlatformCountOf(stageParams));
            int maxSize = StageRules.MaxPlatformSizeOf(stageParams);
            foreach (PlatformGene p in platforms)
            {
                Assert.InRange(p.YSize, StageGenerator.MinThickness, StageGenerator.MaxThickness);
                Assert.InRange(p.XSize, 1, maxSize);
            }
        }
    }

    [Fact]
    public void SpawnsAreOverPlatformsAndInsideTheVisibleBox()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, ParamSet stageParams) = Gen(seed);
            float visW = stageParams.Get(StageParams.VisibleHalfWidth);
            float visH = stageParams.Get(StageParams.VisibleHalfHeight);
            foreach ((string xKey, string yKey) in new[]
                     {
                         (StageParams.Spawn1X, StageParams.Spawn1Y),
                         (StageParams.Spawn2X, StageParams.Spawn2Y),
                     })
            {
                float x = stageParams.Get(xKey);
                float y = stageParams.Get(yKey);
                Assert.True(MathF.Abs(x) <= visW && MathF.Abs(y) <= visH,
                    $"seed {seed}: spawn ({x}, {y}) outside the visible box");
                bool over = false;
                foreach (PlatformGene p in platforms)
                {
                    if (x >= p.X && x <= p.X + p.XSize && y >= p.Y + p.YSize)
                    {
                        over = true;
                        break;
                    }
                    // The 2026-07-21 eject-bug class: a spawn whose BODY box clips a
                    // platform gets shoved to its far side by the axis-clamp physics
                    // (potentially past the KO line). Generated spawns must clear
                    // every platform by the conservative body extents.
                    bool bodyClips =
                        x + StageRules.SpawnBodyHalfWidth > p.X
                        && x - StageRules.SpawnBodyHalfWidth < p.X + p.XSize
                        && y + StageRules.SpawnBodyHalfHeight > p.Y
                        && y - StageRules.SpawnBodyHalfHeight < p.Y + p.YSize;
                    Assert.False(bodyClips,
                        $"seed {seed}: spawn ({x}, {y}) body-embeds in platform {p}");
                }
                Assert.True(over, $"seed {seed}: spawn ({x}, {y}) is not over any platform");
            }
        }
    }

    [Fact]
    public void MirroredStagesGetMirroredSpawns()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (IReadOnlyList<PlatformGene> platforms, ParamSet stageParams) = Gen(seed);
            if (!StageRules.IsMirrored(stageParams))
            {
                continue;
            }
            Assert.Equal(-stageParams.Get(StageParams.Spawn1X), stageParams.Get(StageParams.Spawn2X));
            Assert.Equal(stageParams.Get(StageParams.Spawn1Y), stageParams.Get(StageParams.Spawn2Y));
        }
    }

    [Fact]
    public void GeneratedStageParamsValidate()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            (_, ParamSet stageParams) = Gen(seed);
            Assert.Empty(stageParams.Validate());
        }
    }
}
