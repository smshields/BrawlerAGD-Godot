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
    public void ChildStagePlatformsComeFromParents()
    {
        var a = NewGame(1);
        var b = NewGame(2);
        var child = GameGenomeOps.Crossover(a, b, new Pcg32(3));

        var parentPlatforms = a.Stage.Platforms.Concat(b.Stage.Platforms).ToHashSet();
        foreach (PlatformGene p in child.Stage.Platforms)
        {
            Assert.Contains(p, parentPlatforms);
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
