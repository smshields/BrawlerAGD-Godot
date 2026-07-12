using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// The multi-move control scheme (docs/features/multi-move-controls.md): four assignable
/// action buttons, the genome's button→move mapping gene, and its genetic-op bounds.
/// </summary>
public class MultiMoveControlsTests
{
    /// <summary>FlatArena variant with TWO moves per character and a chosen mapping.
    /// Move 0 warms up 12 ticks (0.2 s), move 1 warms up 24 ticks (0.4 s).</summary>
    private static GameGenome TwoMoveArena(int[] buttonMoves)
    {
        var moves = new[]
        {
            new MoveGenome(TestGames.Move(), 0),
            new MoveGenome(TestGames.Move((MoveParams.WarmUpDuration, 0.4f)), 0),
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, buttonMoves);
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("Player 1"), Make("Player 2") }, stage);
    }

    private static SimWorld GroundedWorld(GameGenome genome, out SimPlayer player)
    {
        var world = new SimWorld(genome);
        player = world.Players[0];
        player.Position = new Vec2(-4f, -1.4f);
        world.Players[1].Position = new Vec2(6f, -1.4f);
        for (int i = 0; i < 120 && !player.IsGrounded; i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    private static void TickWith(SimWorld world, InputFrame p1)
    {
        world.Tick(stackalloc[] { p1, InputFrame.Neutral });
    }

    [Fact]
    public void ButtonsDispatchTheirMappedMoves()
    {
        SimWorld world = GroundedWorld(TwoMoveArena(new[] { 0, 1, 0, 1 }), out SimPlayer player);

        TickWith(world, new InputFrame(0f, 0f, false, InputFrame.ActionBit(1)));
        Assert.Equal(PlayerState.WarmUp, player.State);
        Assert.Equal(1, player.CurrentMoveIndex);
        Assert.Equal(24, player.Move.WarmUpTicks); // move 1's 0.4 s, not move 0's 0.2 s
    }

    [Fact]
    public void SimultaneousButtonsResolveToTheLowestIndex()
    {
        SimWorld world = GroundedWorld(TwoMoveArena(new[] { 0, 1, 0, 1 }), out SimPlayer player);

        var both = new InputFrame(0f, 0f, false,
            (byte)(InputFrame.ActionBit(2) | InputFrame.ActionBit(3)));
        Assert.Equal(2, both.FirstAction);
        TickWith(world, both);
        Assert.Equal(0, player.CurrentMoveIndex); // button 2 → move 0 wins over button 3 → move 1
    }

    [Fact]
    public void ButtonForMoveReturnsTheLowestMappedButton()
    {
        GroundedWorld(TwoMoveArena(new[] { 1, 0, 1, 0 }), out SimPlayer player);
        Assert.Equal(1, player.ButtonForMove(0)); // buttons 1 and 3 map to move 0
        Assert.Equal(0, player.ButtonForMove(1)); // buttons 0 and 2 map to move 1
        Assert.Equal(-1, player.ButtonForMove(9));
    }

    [Fact]
    public void ButtonMoveGeneIsValidatedAtConstruction()
    {
        var moves = new[] { new MoveGenome(TestGames.Move(), 0) };
        // Wrong length.
        Assert.Throws<ArgumentException>(() =>
            new CharacterGenome("X", 3, 0, TestGames.Character(), moves, new[] { 0, 0 }));
        // Index outside the move list (single move → only 0 is legal).
        Assert.Throws<ArgumentException>(() =>
            new CharacterGenome("X", 3, 0, TestGames.Character(), moves, new[] { 0, 0, 0, 1 }));
        Assert.Throws<ArgumentException>(() =>
            new CharacterGenome("X", 3, 0, TestGames.Character(), moves, new[] { -1, 0, 0, 0 }));
    }

    [Fact]
    public void SingleMoveGenomesGetTheAllZerosMappingByDefault()
    {
        GameGenome genome = TestGames.FlatArena();
        Assert.All(genome.Characters, c =>
        {
            Assert.Equal(InputFrame.ActionCount, c.ButtonMoves.Count);
            Assert.All(c.ButtonMoves, m => Assert.Equal(0, m));
        });
    }

    [Fact]
    public void GenerationAndBreedingKeepButtonGenesInRangeAndActuallySearchThem()
    {
        // Bounds under adversarial evolutionary pressure (design Q2): with 2 moves per
        // character, generation + forced mutation + crossover must keep every gene a
        // valid move index — and must actually explore both values. (Shield slot off:
        // this test pins the two-ATTACK mapping semantics.)
        var config = GenerationConfig.Default with { MovesPerCharacter = 2, ShieldSlotCount = 0 };
        var rng = new Pcg32(99);
        var population = new List<GameGenome>();
        for (int i = 0; i < 10; i++)
        {
            population.Add(GameGenome.Generate(config, rng));
        }
        for (int i = 0; i < 50; i++)
        {
            GameGenome a = population[rng.NextInt(population.Count)];
            GameGenome b = population[rng.NextInt(population.Count)];
            population.Add(GameGenomeOps.Breed(a, b, mutationRate: 1f, rng, config));
        }

        var seen = new HashSet<int>();
        foreach (GameGenome g in population)
        {
            foreach (CharacterGenome c in g.Characters)
            {
                Assert.Equal(InputFrame.ActionCount, c.ButtonMoves.Count);
                foreach (int move in c.ButtonMoves)
                {
                    Assert.InRange(move, 0, 1);
                    seen.Add(move);
                }
            }
        }
        Assert.Equal(new HashSet<int> { 0, 1 }, seen); // the gene is genuinely searched
    }

    [Fact]
    public void TwoMoveAiMatchesTerminateDeterministically()
    {
        // Integration probe (ADDING_FEATURES step 5): AI-vs-AI on generated two-move
        // genomes still terminates with sane stats and stays bit-deterministic.
        var config = GenerationConfig.Default with { MovesPerCharacter = 2 };
        var rng = new Pcg32(7);
        for (int i = 0; i < 5; i++)
        {
            GameGenome genome = GameGenome.Generate(config, rng);
            MatchResult a = RunAiMatch(genome, seed: (ulong)(100 + i));
            MatchResult b = RunAiMatch(genome, seed: (ulong)(100 + i));
            Assert.True(a.Ticks <= MatchConfig.Default.MaxTicks);
            Assert.InRange(a.LoserIndex, -1, 1);
            Assert.Equal(a.FinalHash, b.FinalHash);
        }
    }

    [Fact]
    public void AgentPressesTheLowestButtonMappedToMoveZero()
    {
        // The ported decision tree only ever wants move 0; with the default mapping that
        // must surface as action button 0 in the trace — and never any other button.
        MatchResult result = RunAiMatch(TestGames.FlatArena(), seed: 3, recordTrace: true);
        bool sawAttack = false;
        for (int t = 0; t < result.Trace!.TickCount; t++)
        {
            for (int p = 0; p < 2; p++)
            {
                InputFrame frame = result.Trace.Get(t, p);
                if (frame.Actions != 0)
                {
                    sawAttack = true;
                    Assert.Equal(InputFrame.ActionBit(0), frame.Actions);
                }
                Assert.Equal(0f, frame.Vertical); // agent has no vertical intent yet
            }
        }
        Assert.True(sawAttack, "agents never attacked on a flat arena");
    }

    [Fact]
    public void EveryMoveIsAlwaysReachableFromSomeButton()
    {
        // Coverage guarantee (second-move feature): across generation and heavy
        // breeding, no genome may carry a move that no button triggers.
        var config = GenerationConfig.Default; // 2 moves since 2026-07-10
        var rng = new Pcg32(123);
        var population = new List<GameGenome>();
        for (int i = 0; i < 20; i++)
        {
            population.Add(GameGenome.Generate(config, rng));
        }
        for (int i = 0; i < 60; i++)
        {
            population.Add(GameGenomeOps.Breed(
                population[rng.NextInt(population.Count)],
                population[rng.NextInt(population.Count)],
                mutationRate: 1f, rng, config));
        }
        foreach (GameGenome g in population)
        {
            foreach (CharacterGenome c in g.Characters)
            {
                for (int m = 0; m < c.Moves.Count; m++)
                {
                    Assert.Contains(m, c.ButtonMoves);
                }
            }
        }
    }

    [Fact]
    public void EnsureButtonCoverageRepairsWithoutUnmappingAnything()
    {
        // Pigeonhole repair: fixing one unmapped move must never orphan another.
        Assert.Equal(new[] { 1, 0, 0, 0 }, CharacterGenome.EnsureButtonCoverage(new[] { 0, 0, 0, 0 }, 2));
        Assert.Equal(new[] { 0, 1, 1, 1 }, CharacterGenome.EnsureButtonCoverage(new[] { 1, 1, 1, 1 }, 2));
        // 3 moves, move 0 only at a button that move 2's repair must NOT steal.
        Assert.Equal(new[] { 2, 1, 0, 1 }, CharacterGenome.EnsureButtonCoverage(new[] { 1, 1, 0, 1 }, 3));
        // Already covering → untouched.
        Assert.Equal(new[] { 1, 0, 1, 0 }, CharacterGenome.EnsureButtonCoverage(new[] { 1, 0, 1, 0 }, 2));
    }

    private static MatchResult RunAiMatch(GameGenome genome, ulong seed, bool recordTrace = false)
    {
        var sources = new IInputSource[]
        {
            new DecisionTreeAgent(new Pcg32(seed, 0)),
            new DecisionTreeAgent(new Pcg32(seed, 1)),
        };
        return MatchRunner.Run(genome, sources, recordTrace: recordTrace);
    }
}
