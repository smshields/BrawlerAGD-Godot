using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Genome;

public class GameGenomeOpsTests
{
    private static GameGenome NewGame(ulong seed) =>
        GameGenome.Generate(GenerationConfig.Default, new Pcg32(seed));

    private static string Fingerprint(GameGenome genome) =>
        GameGenomeJson.Serialize(new GameRecord("t", null, genome));

    [Fact]
    public void GenerateIsDeterministicAndValid()
    {
        var a = NewGame(42);
        var b = NewGame(42);
        Assert.Equal(Fingerprint(a), Fingerprint(b));
        Assert.Empty(a.Validate());
        Assert.Equal(2, a.Characters.Count);
        Assert.Equal("Player 1", a.Characters[0].Name);
        Assert.Equal(3, a.Characters[0].Stocks);
        // 2 attacks (2026-07-10) + shield (2026-07-12) + dash last (2026-07-13).
        Assert.Equal(4, a.Characters[0].Moves.Count);
        Assert.Equal(MoveType.Shield, a.Characters[0].Moves[2].Type);
        Assert.Equal(MoveType.Dash, a.Characters[0].Moves[3].Type);
    }

    [Fact]
    public void CrossoverProducesValidChildWithParentSprites()
    {
        var a = NewGame(1);
        var b = NewGame(2);
        var child = GameGenomeOps.Crossover(a, b, new Pcg32(3));

        Assert.Empty(child.Validate());
        for (int c = 0; c < child.Characters.Count; c++)
        {
            int sprite = child.Characters[c].SpriteIndex;
            Assert.True(sprite == a.Characters[c].SpriteIndex || sprite == b.Characters[c].SpriteIndex,
                "child sprite must come from one of the parents");
            Assert.Equal(a.Characters[c].Name, child.Characters[c].Name);
        }
    }

    [Fact]
    public void CrossoverIsDeterministic()
    {
        var a = NewGame(1);
        var b = NewGame(2);
        Assert.Equal(
            Fingerprint(GameGenomeOps.Crossover(a, b, new Pcg32(9))),
            Fingerprint(GameGenomeOps.Crossover(a, b, new Pcg32(9))));
    }

    [Fact]
    public void ChildStagePlatformsComeFromParentsUpToContainment()
    {
        // Unity-parity platform mixing, amended twice: the per-character platform FIT
        // (2026-07-22) may MOVE a platform, and the playable-box repair (2026-08-13)
        // may CLAMP one into the child's kill box. Both preserve sizes; neither
        // invents platforms — so every child platform is a parent platform, possibly
        // repositioned, and always inside the child's playable box.
        var a = NewGame(1);
        var b = NewGame(2);
        var child = GameGenomeOps.Crossover(a, b, new Pcg32(3));

        var parentPlatforms = a.Stage.Platforms.Concat(b.Stage.Platforms).ToHashSet();
        var parentSizes = parentPlatforms.Select(p => (p.XSize, p.YSize)).ToHashSet();
        (var playMin, var playMax) = StageRules.PlayableBox(child.Stage.Params);
        foreach (PlatformGene p in child.Stage.Platforms)
        {
            Assert.True(parentPlatforms.Contains(p) || parentSizes.Contains((p.XSize, p.YSize)),
                $"child platform {p} matches no parent platform or size");
            Assert.True(StageRules.PlatformInPlayableBox(p, playMin, playMax),
                $"child platform {p} is outside the child's playable box");
        }
    }

    /// <summary>Breeding sweep for the 2026-08-13 containment rule: chains of
    /// crossover + mutation (regeneration, mirror transform, platform fit, repair)
    /// never leave a platform outside the child's playable box. Strengthened
    /// 2026-08-17 (designer report of an escaped stage): a CHAINED pool — every
    /// child re-enters and breeds again — across pinned AND random compositions,
    /// the pattern that exposed the integer-feasible-span repair bug the original
    /// fresh-pair sweep missed.</summary>
    [Fact]
    public void BredStagesKeepPlatformsInsideTheirPlayableBox()
    {
        foreach (GenerationConfig config in new[]
                 {
                     GenerationConfig.Default,
                     GenerationConfig.Default with { ButtonComposition = GenerationConfig.RandomComposition },
                 })
        {
            var pool = Enumerable.Range(1, 20)
                .Select(i => GameGenome.Generate(config, new Pcg32((ulong)i)))
                .ToList();
            var rng = new Pcg32(9999);
            for (int gen = 0; gen < 40; gen++)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    var child = GameGenomeOps.Breed(pool[i], pool[(i + 1) % pool.Count], 0.5f, rng, config);
                    (var playMin, var playMax) = StageRules.PlayableBox(child.Stage.Params);
                    foreach (PlatformGene p in child.Stage.Platforms)
                    {
                        Assert.True(StageRules.PlatformInPlayableBox(p, playMin, playMax),
                            $"bred gen {gen} idx {i}: platform {p} outside the child's playable box");
                    }
                    pool[i] = child;
                }
            }
        }
    }

    /// <summary>Regression (2026-08-17, designer report): RepairPlatforms size-clamped
    /// against the RAW playable-box width but position-clamped against integer-aligned
    /// bounds. An 11-wide platform in an 11.96-wide box passed the size clamp, yet no
    /// integer X contains it — the position clamp parked it at ceil(min.X), sticking
    /// out the far side (the only containment leak in 1,600 audited breedings, all
    /// pure-crossover children). The repaired size must be the integer-feasible span.</summary>
    [Fact]
    public void RepairPlatformsShrinksAPlatformNoIntegerPositionCanContain()
    {
        var platforms = new List<PlatformGene> { new(-5, 0, 11, 2), new(-2, -3, 4, 1) };
        // Blast half width = 5.2 × (1 + 0.15) = 5.98: the raw box width 11.96 admits
        // size 11, but the integer-feasible span is floor(5.98) − ceil(−5.98) = 10.
        ParamSet stage = StageRules.LegacyParams(platforms).With(
            (StageParams.VisibleHalfWidth, 5.2f),
            (StageParams.KoMarginFraction, 0.15f));
        var repaired = StageRules.RepairPlatforms(platforms, stage);
        (var min, var max) = StageRules.PlayableBox(stage);
        foreach (PlatformGene p in repaired)
        {
            Assert.True(StageRules.PlatformInPlayableBox(p, min, max),
                $"repaired platform {p} is still outside the playable box");
        }
        Assert.Equal(10, repaired[0].XSize);
    }

    [Fact]
    public void BreedWithZeroMutationRateEqualsPureCrossover()
    {
        var a = NewGame(1);
        var b = NewGame(2);
        // Same rng seed: Breed draws the crossover sequence, then one extra roll that
        // must not alter the child when the rate is 0.
        Assert.Equal(
            Fingerprint(GameGenomeOps.Crossover(a, b, new Pcg32(5))),
            Fingerprint(GameGenomeOps.Breed(a, b, 0f, new Pcg32(5))));
    }

    [Fact]
    public void BreedWithCertainMutationChangesTheChildAndStaysValid()
    {
        var a = NewGame(1);
        var b = NewGame(2);
        var pure = GameGenomeOps.Crossover(a, b, new Pcg32(5));
        var mutated = GameGenomeOps.Breed(a, b, 1f, new Pcg32(5));

        Assert.NotEqual(Fingerprint(pure), Fingerprint(mutated));
        Assert.Empty(mutated.Validate());
    }

    [Fact]
    public void MutationRegeneratesTheStage()
    {
        var game = NewGame(1);
        var mutated = GameGenomeOps.Mutate(game, new Pcg32(77));
        // Unity parity: stage mutation is full regeneration, not perturbation.
        Assert.NotEqual(game.Stage.Platforms, mutated.Stage.Platforms);
        Assert.Empty(mutated.Validate());
    }
}
