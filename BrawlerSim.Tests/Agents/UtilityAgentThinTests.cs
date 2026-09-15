using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// Thin platforms, agent layer (2026-09-01, CHANGE_LOG #34): drop-through as an
/// escape/traversal option — the platform graph gains downward drop edges from thin
/// platforms, and the utility channels use them ONLY with a safe landing below
/// (FEATURES.md: "only if there is a reachable platform below that they can safely
/// land on"). Thin-free stages leave the instrument untouched — the standing utility
/// golden is that proof.
/// </summary>
public class UtilityAgentThinTests
{
    private static readonly PlatformGene Floor = new(-8, -3, 16, 1);

    private static GameGenome Arena(PlatformGene[] platforms,
        (string Key, float Value)[]? characterOverrides = null)
    {
        var character = TestGames.Character(characterOverrides
            ?? new[] { (CharacterParams.DropThroughDelay, 0.1f) });
        CharacterGenome Make(string name) =>
            new(name, 3, 0, character, new[] { new MoveGenome(TestGames.Move(), 0) });
        return new GameGenome(new[] { Make("P1"), Make("P2") },
            new StageGenome(platforms));
    }

    private static SimWorld Settled(GameGenome genome, Vec2 p0, Vec2 p1)
    {
        var world = new SimWorld(genome);
        world.Players[0].Position = p0;
        world.Players[1].Position = p1;
        world.Players[0].Velocity = Vec2.Zero;
        world.Players[1].Velocity = Vec2.Zero;
        for (int i = 0; i < 180 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.True(world.Players[0].IsGrounded && world.Players[1].IsGrounded, "players failed to settle");
        return world;
    }

    private static void RunAgents(SimWorld world, int ticks, ulong seed)
    {
        // Deterministic argmax agents — the tests pin intent, not sampling noise.
        var config = AgentConfig.Default with { Randomness = 0f };
        IInputSource a = new UtilityAgent(new Pcg32(seed, 0), config);
        IInputSource b = new UtilityAgent(new Pcg32(seed, 1), config);
        for (int i = 0; i < ticks && !world.IsOver; i++)
        {
            world.Tick(stackalloc[] { a.GetInput(world, 0), b.GetInput(world, 1) });
        }
    }

    [Fact]
    public void DropLandingSensorFindsTheHighestPlatformUnderTheColumn()
    {
        // The safety sensor: TryDropLanding returns the HIGHEST platform under the
        // column, nothing for the bottom platform, nothing off the span; IsThin
        // reads the world's flags. (The route table itself is pre-feature: downward
        // hops over overlapping spans were always edge-feasible — drop-through only
        // changes route EXECUTION.)
        var ledge = new PlatformGene(-2, 4, 4, 1, Thin: true);
        var shelf = new PlatformGene(-1, 1, 2, 1); // between the ledge and the floor
        var world = new SimWorld(Arena(new[] { Floor, ledge, shelf }));
        var graph = new PlatformGraph(world.Platforms, world.Players[0], world.Config.Gravity,
            world.PlatformThin);
        Assert.True(graph.IsThin(1));
        Assert.False(graph.IsThin(0));
        Assert.True(graph.TryDropLanding(1, 0f, out int landing));
        Assert.Equal(2, landing); // the shelf catches the drop, not the floor
        Assert.True(graph.TryDropLanding(1, -1.8f, out landing));
        Assert.Equal(0, landing); // off the shelf's span: straight to the floor
        Assert.False(graph.TryDropLanding(0, 0f, out _)); // the floor has nothing below
    }

    [Fact]
    public void AgentDropsThroughToReachAnOpponentBelow()
    {
        // P0 on a thin ledge, P1 on the floor beneath it: the traversal/pursuit
        // channels must take the drop route (crouch, wait out the delay, fall).
        var ledge = new PlatformGene(-3, 1, 6, 1, Thin: true);
        SimWorld world = Settled(Arena(new[] { Floor, ledge }),
            new Vec2(0f, 2.7f), new Vec2(0.8f, -1.4f));
        RunAgents(world, 600, seed: 7);
        Assert.True(world.Players[0].DropThroughs >= 1,
            "the agent never dropped through to the opponent below");
    }

    [Fact]
    public void AgentNeverDropsWithoutALandingBelow()
    {
        // Both fighters on a thin BOTTOM platform (nothing beneath it): every
        // deliberate down-hold is gated on CanDropSafely, so no one may ever drop —
        // a drop here is death by design error.
        var thinFloor = new PlatformGene(-8, -3, 16, 1, Thin: true);
        var sideLedge = new PlatformGene(-7, 0, 3, 1); // a solid platform, off to the side
        SimWorld world = Settled(Arena(new[] { thinFloor, sideLedge }),
            new Vec2(1f, -1.4f), new Vec2(4f, -1.4f));
        RunAgents(world, 900, seed: 11);
        Assert.Equal(0, world.Players[0].DropThroughs);
        Assert.Equal(0, world.Players[1].DropThroughs);
        Assert.Equal(0, world.Players[0].SelfDestructs + world.Players[1].SelfDestructs);
    }
}
