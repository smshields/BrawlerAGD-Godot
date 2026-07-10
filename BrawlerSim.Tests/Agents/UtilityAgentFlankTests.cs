using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// Regression: designer-reported 2026-07-10 — characters separated vertically by a
/// platform paced back and forth without ever converging (the approach target's X
/// flips sign around the opponent while the vertical route is blocked). The flank
/// behavior must route them around the blocking platform's SAFE edge.
/// </summary>
public class UtilityAgentFlankTests
{
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    /// <summary>Floor spanning x ∈ [-8, 8] (top −2) plus a blocker platform (top +1)
    /// whose span is configurable. P0 stands ON the blocker, P1 on the floor below.</summary>
    private static SimWorld StackedWorld(int blockerX, int blockerWidth, float p0X, float p1X)
    {
        var stage = new StageGenome(new[]
        {
            new PlatformGene(-8, -3, 16, 1),
            new PlatformGene(blockerX, 0, blockerWidth, 1),
        });
        ParamSet character = TestGames.Character();
        ParamSet move = TestGames.Move();
        CharacterGenome Make(string name) =>
            new(name, 3, 0, character, new[] { new MoveGenome(move, 0) });
        var world = new SimWorld(new GameGenome(new[] { Make("P1"), Make("P2") }, stage));

        world.Players[0].Position = new Vec2(p0X, 1.6f);  // on the blocker
        world.Players[1].Position = new Vec2(p1X, -1.4f); // on the floor beneath
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    [Fact]
    public void StackedCharactersDoNotStallTheyFlankTowardTheNearestEdge()
    {
        // Blocker spans x ∈ [-3, 3]; both characters at x = 1, separated only by it.
        // The character on top must head for the NEAR (right) edge to drop around;
        // the one underneath must do the same to get out from under the ceiling.
        SimWorld world = StackedWorld(blockerX: -3, blockerWidth: 6, p0X: 1f, p1X: 1f);

        var above = new UtilityAgent(new Pcg32(1), Greedy);
        var below = new UtilityAgent(new Pcg32(2), Greedy);
        Assert.Equal(1f, above.GetInput(world, 0).Horizontal);
        InputFrame belowInput = below.GetInput(world, 1);
        Assert.Equal(1f, belowInput.Horizontal);
        Assert.False(belowInput.Jump); // jumping into the platform's underside is wasted
    }

    [Fact]
    public void FlankPrefersTheEdgeWithGroundBeyondIt()
    {
        // Blocker spans x ∈ [1, 7]; its RIGHT edge pokes past the floor (floor ends at
        // x = 8, probe beyond edge ≈ 7.75 is still floor... use a blocker overhanging
        // the right void: x ∈ [4, 10]. P0 stands at x = 9 — nearest edge is the RIGHT
        // one (x = 10, over the void), but only the LEFT edge (x = 4) has floor below.
        // Self-preservation (designer constraint) must win: flank LEFT.
        SimWorld world = StackedWorld(blockerX: 4, blockerWidth: 6, p0X: 9f, p1X: 5f);

        var above = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(-1f, above.GetInput(world, 0).Horizontal);
    }

    [Fact]
    public void SameLevelApproachIsUnaffected()
    {
        // No platform between them → the flank behavior stays silent and the ordinary
        // approach drives toward the opponent.
        SimWorld world = StackedWorld(blockerX: -3, blockerWidth: 6, p0X: -6f, p1X: 6f);
        world.Players[0].Position = new Vec2(-6f, -1.4f); // move P0 down to the floor
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(1f, agent.GetInput(world, 0).Horizontal);
    }
}
