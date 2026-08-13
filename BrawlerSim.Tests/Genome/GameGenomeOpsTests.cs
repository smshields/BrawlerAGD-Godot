using BrawlerSim.Determinism;
using BrawlerSim.Genome;
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
    /// never leave a platform outside the child's playable box.</summary>
    [Fact]
    public void BredStagesKeepPlatformsInsideTheirPlayableBox()
    {
        var rng = new Pcg32(77);
        var pool = Enumerable.Range(0, 8)
            .Select(i => GameGenome.Generate(GenerationConfig.Default, new Pcg32((ulong)(100 + i))))
            .ToList();
        for (int gen = 0; gen < 40; gen++)
        {
            var a = pool[rng.NextInt(pool.Count)];
            var b = pool[rng.NextInt(pool.Count)];
            var child = GameGenomeOps.Breed(a, b, 0.4f, rng);
            (var playMin, var playMax) = StageRules.PlayableBox(child.Stage.Params);
            foreach (PlatformGene p in child.Stage.Platforms)
            {
                Assert.True(StageRules.PlatformInPlayableBox(p, playMin, playMax),
                    $"bred gen {gen}: platform {p} outside the child's playable box");
            }
            pool[rng.NextInt(pool.Count)] = child;
        }
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
