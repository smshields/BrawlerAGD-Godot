using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Genome;

/// <summary>
/// Thin platforms, genome layer (2026-09-01, FEATURES.md §Thin Platforms;
/// docs/features/thin-platforms.md): the thin gene's generation (fraction-driven,
/// initial platform always solid), the structural at-least-one-solid rule across
/// generation and chained breeding, mirror symmetry of the flag, the gap solver's
/// thin exemption, and game.json v12 round-trip/legacy defaults.
/// </summary>
public class ThinPlatformGenomeTests
{
    private static GenerationConfig FractionPinned(float fraction) =>
        GenerationConfig.Default.WithRangeOverrides(new[]
        {
            new RangeOverride("stage", StageParams.ThinPlatformFraction, fraction, fraction),
        });

    [Fact]
    public void GeneratedStagesAlwaysKeepASolidPlatform()
    {
        // Even with the fraction clamped to 1 every stage keeps a solid platform:
        // the initial platform never rolls thin (and its mirror copy inherits).
        StageGenerator generator = FractionPinned(1f).CreateStageGenerator();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            StageGenome stage = generator.Generate(new Pcg32(seed));
            Assert.Contains(stage.Platforms, p => !p.Thin);
            // And the pin actually bites: multi-platform stages carry thin ones.
            if (stage.Platforms.Count > 2)
            {
                Assert.Contains(stage.Platforms, p => p.Thin);
            }
        }
    }

    [Fact]
    public void ThinFractionZeroGeneratesAllSolid()
    {
        StageGenerator generator = FractionPinned(0f).CreateStageGenerator();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            Assert.All(generator.Generate(new Pcg32(seed)).Platforms, p => Assert.False(p.Thin));
        }
    }

    [Fact]
    public void ThinFlagsAppearAndMirrorUnderTheDefaultRange()
    {
        // The stock 0-1 range must actually produce thin platforms across a sweep,
        // and mirrored stages stay flag-symmetric (PlatformGene equality includes
        // Thin, so IsSymmetric checks the flag too).
        StageGenerator generator = GenerationConfig.Default.CreateStageGenerator();
        int thinStages = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            StageGenome stage = generator.Generate(new Pcg32(seed));
            if (stage.Platforms.Any(p => p.Thin))
            {
                thinStages++;
            }
            Assert.Contains(stage.Platforms, p => !p.Thin);
            if (StageRules.IsMirrored(stage.Params))
            {
                Assert.True(StageRules.IsSymmetric(stage.Platforms));
            }
        }
        Assert.InRange(thinStages, 50, 200);
    }

    [Fact]
    public void RegenerateHonorsTheMutatedFractionGene()
    {
        // The mutation path reads the (already mutated) ParamSet's fraction.
        StageGenerator generator = GenerationConfig.Default.CreateStageGenerator();
        StageGenome stage = generator.Generate(new Pcg32(7));
        StageGenome allSolid = generator.Regenerate(
            stage.Params.With((StageParams.ThinPlatformFraction, 0f)), new Pcg32(8));
        Assert.All(allSolid.Platforms, p => Assert.False(p.Thin));
        StageGenome forcedThin = generator.Regenerate(
            stage.Params.With((StageParams.ThinPlatformFraction, 1f)), new Pcg32(8));
        Assert.Contains(forcedThin.Platforms, p => !p.Thin); // the initial platform
        if (forcedThin.Platforms.Count > 2)
        {
            Assert.Contains(forcedThin.Platforms, p => p.Thin);
        }
    }

    [Fact]
    public void EnsureSolidPlatformFlipsTheWidestAndItsMirrorTwin()
    {
        // All-thin: the widest flips solid (ties -> first), and on a symmetric
        // layout its mirror twin flips with it so IsSymmetric holds.
        var asymmetric = new List<PlatformGene>
        {
            new(-6, -3, 4, 1, Thin: true),
            new(0, -1, 6, 1, Thin: true),
            new(2, 2, 3, 1, Thin: true),
        };
        List<PlatformGene> repaired = StageRules.EnsureSolidPlatform(asymmetric);
        Assert.False(repaired[1].Thin); // widest (XSize 6)
        Assert.True(repaired[0].Thin);
        Assert.True(repaired[2].Thin);

        var mirrored = new List<PlatformGene>
        {
            new(-7, -3, 5, 1, Thin: true),
            new(-3, 1, 3, 1, Thin: true),
        };
        mirrored.Add(mirrored[0].MirrorX());
        mirrored.Add(mirrored[1].MirrorX());
        Assert.True(StageRules.IsSymmetric(mirrored));
        List<PlatformGene> repairedMirror = StageRules.EnsureSolidPlatform(mirrored);
        Assert.False(repairedMirror[0].Thin);
        Assert.False(repairedMirror[2].Thin); // the twin flips too
        Assert.True(StageRules.IsSymmetric(repairedMirror));

        // Identity when a solid platform already exists.
        var fine = new List<PlatformGene> { new(-2, -3, 4, 1), new(3, -1, 3, 1, Thin: true) };
        Assert.Equal(fine, StageRules.EnsureSolidPlatform(new List<PlatformGene>(fine)));
    }

    [Fact]
    public void BredStagesAlwaysKeepASolidPlatform()
    {
        // Chained pool sweep (the containment sweep's pattern) with the fraction
        // pinned to 1: crossover repair + mirror transform + regeneration must never
        // ship an all-thin stage.
        GenerationConfig config = FractionPinned(1f);
        var pool = Enumerable.Range(1, 16)
            .Select(i => GameGenome.Generate(config, new Pcg32((ulong)i)))
            .ToList();
        var rng = new Pcg32(20260901);
        for (int gen = 0; gen < 40; gen++)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                GameGenome child = GameGenomeOps.Breed(pool[i], pool[(i + 1) % pool.Count], 0.5f, rng, config);
                Assert.Contains(child.Stage.Platforms, p => !p.Thin);
                pool[i] = child;
            }
        }
    }

    [Fact]
    public void MirrorTransformRepairsAnAllThinSourceHalf()
    {
        // The chosen half holds only thin platforms; the transform must hand back a
        // symmetric layout with a solid pair.
        var platforms = new List<PlatformGene>
        {
            new(-6, -3, 4, 1, Thin: true),
            new(-5, 0, 2, 1, Thin: true),
            new(2, -2, 3, 1), // solid, but on the discarded right side
        };
        List<PlatformGene>? transformed = StageRules.MirrorTransform(platforms, rightSideIsSource: false);
        Assert.NotNull(transformed);
        Assert.Contains(transformed!, p => !p.Thin);
        Assert.True(StageRules.IsSymmetric(transformed!));
    }

    [Fact]
    public void ThinWallsAreNotAsymmetricCorridors()
    {
        // A corridor whose wall is thin is passable for every body (sides never
        // collide) — the fit solver must leave the layout untouched. The same
        // geometry with SOLID walls is the classic asymmetric gap and must move.
        const float baseW = 0.74289274f;
        CharacterGenome Make(string n, float widthScalar) => new(
            n, 3, 0, Support.TestGames.Character((CharacterParams.WidthScalar, widthScalar)),
            new[] { new MoveGenome(Support.TestGames.Move(), 0) });
        CharacterGenome small = Make("Small", 0.7f);
        CharacterGenome large = Make("Large", 1.5f);
        // Gap 1 between the platforms: small body (0.52) passes, large (1.11) walls.
        PlatformGene left = new(-5, -3, 4, 1);
        PlatformGene right = new(0, -3, 4, 1);

        var thinWall = new List<PlatformGene> { left, right with { Thin = true } };
        var thinStage = new StageGenome(thinWall, StageRules.LegacyParams(thinWall));
        Assert.Same(thinStage, StageRules.FitToCharacters(thinStage, small, large, 9.81f, baseW));

        var solidWall = new List<PlatformGene> { left, right };
        var solidStage = new StageGenome(solidWall, StageRules.LegacyParams(solidWall));
        StageGenome fitted = StageRules.FitToCharacters(solidStage, small, large, 9.81f, baseW);
        Assert.NotSame(solidStage, fitted);
    }

    [Fact]
    public void GameJsonV12RoundTripsThinAndOmitsItWhenSolid()
    {
        var platforms = new List<PlatformGene> { new(-8, -3, 16, 1), new(-2, 0, 4, 1, Thin: true) };
        var genome = new GameGenome(
            Support.TestGames.FlatArena().Characters,
            new StageGenome(platforms, StageRules.LegacyParams(platforms).With(
                (StageParams.ThinPlatformFraction, 0.4f))));
        string json = GameGenomeJson.Serialize(new GameRecord("thin game", null, genome));
        Assert.Contains("\"formatVersion\": 13", json); // v13: stage tile themes (2026-09-01)
        // Written only when true — solid platforms keep their pre-v12 bytes
        // (BuiltGameJson.ContentKey stability for legacy content).
        Assert.Single(
            json.Split('\n').Where(l => l.Contains("\"thin\"")));

        GameRecord reloaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(platforms, reloaded.Genome.Stage.Platforms);
        Assert.Equal(0.4f, reloaded.Genome.Stage.Params.Get(StageParams.ThinPlatformFraction));
    }

    [Fact]
    public void PreV12FilesLoadAllSolidWithNeutralDefaults()
    {
        // A v11-shaped file: no thin flags, no thinPlatformFraction, no
        // dropThroughDelay — loads solid with both new params 0.
        var legacy = new GameGenome(
            Support.TestGames.FlatArena().Characters,
            new StageGenome(new List<PlatformGene> { new(-8, -3, 16, 1) }));
        var doc = System.Text.Json.Nodes.JsonNode.Parse(
            GameGenomeJson.Serialize(new GameRecord("legacy", null, legacy)))!.AsObject();
        doc["formatVersion"] = 11;
        doc["stage"]!["params"]!.AsObject().Remove("thinPlatformFraction");
        foreach (System.Text.Json.Nodes.JsonNode? c in doc["characters"]!.AsArray())
        {
            c!["params"]!.AsObject().Remove("dropThroughDelay");
        }
        string json = doc.ToJsonString();
        Assert.DoesNotContain("thinPlatformFraction", json);
        Assert.DoesNotContain("dropThroughDelay", json);

        GameRecord reloaded = GameGenomeJson.Deserialize(json);
        Assert.All(reloaded.Genome.Stage.Platforms, p => Assert.False(p.Thin));
        Assert.Equal(0f, reloaded.Genome.Stage.Params.Get(StageParams.ThinPlatformFraction));
        Assert.All(reloaded.Genome.Characters, c =>
            Assert.Equal(0f, c.Params.Get(CharacterParams.DropThroughDelay)));
    }
}
