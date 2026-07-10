using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// Regression: designer-reported 2026-07-10 — agents never traversed platforms toward
/// a distant opponent (approach abdicated over pits, recover pulled back to the
/// nearest platform → pacing at gap edges). The platform-graph next-hop table must
/// carry a chase across the whole level.
/// </summary>
public class UtilityAgentTraversalTests
{
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    /// <summary>Three-platform staircase: A x∈[-9,-5] top −2 · B x∈[-2,2] top 0 ·
    /// C x∈[5,9] top 2. Gaps of 3 and rises of 2 — well within the test character's
    /// hop envelope (jump force 8, air speed 4, gravity 9.81 → range ≈ 9).</summary>
    private static GameGenome Staircase()
    {
        ParamSet character = TestGames.Character();
        ParamSet move = TestGames.Move();
        CharacterGenome Make(string name) =>
            new(name, 3, 0, character, new[] { new MoveGenome(move, 0) });
        var stage = new StageGenome(new[]
        {
            new PlatformGene(-9, -3, 4, 1),
            new PlatformGene(-2, -1, 4, 1),
            new PlatformGene(5, 1, 4, 1),
        });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
    }

    [Fact]
    public void AChaseCrossesTheWholeLevel()
    {
        // P0 starts on the LEFT platform, opponent parked on the RIGHT one. Within
        // 15 sim-seconds the chaser must have climbed the staircase and be standing
        // in the right platform's airspace — not pacing at the first gap edge.
        var world = new SimWorld(Staircase());
        SimPlayer chaser = world.Players[0];
        chaser.Position = new Vec2(-7f, -1.4f);
        world.Players[1].Position = new Vec2(7f, 2.6f);

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Span<InputFrame> inputs = stackalloc InputFrame[2];
        float maxX = float.MinValue;
        for (int t = 0; t < 900 && !world.IsOver; t++)
        {
            inputs[0] = agent.GetInput(world, 0);
            inputs[1] = InputFrame.Neutral;
            world.Tick(inputs);
            maxX = MathF.Max(maxX, chaser.Position.X);
        }
        Assert.True(maxX > 4f,
            $"chaser never traversed the level: max x reached {maxX:F2} (needs > 4 to reach the far platform)");
        Assert.True(chaser.Jumps >= 2,
            $"a staircase chase needs hops; only {chaser.Jumps} jump(s) recorded");
    }

    [Fact]
    public void MidGapRecoveryContinuesForwardInsteadOfTurningBack()
    {
        // The chaser is airborne over the FIRST gap, moving right, with the opponent
        // far right: directional recovery must target the platform AHEAD (B), not pull
        // back to A. Greedy input at that instant must be rightward.
        var world = new SimWorld(Staircase());
        SimPlayer chaser = world.Players[0];
        chaser.Position = new Vec2(-3.8f, -0.6f); // over the A→B gap, above B's top
        chaser.Velocity = new Vec2(3f, 1f);
        world.Players[1].Position = new Vec2(7f, 2.6f);
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(1f, agent.GetInput(world, 0).Horizontal);
    }
}
