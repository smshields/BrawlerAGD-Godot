using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// Nearest-platform recovery (2026-09-10, designer-directed — DEVIATIONS #38).
/// The designer's bug report: agents chased enemies off stage and self-destructed
/// because recovery targeted the platform nearest the OPPONENT (the far, risky
/// option) instead of the reliable ledge back. Recovery now favors the nearest
/// reachable platform, with a momentum split — nearest AHEAD of current horizontal
/// motion first, nearest behind only when nothing ahead is reachable — because a
/// pure nearest pick reintroduced the 2026-07-10 traversal oscillation stall
/// (every hop hijacked back to its origin platform; UtilityAgentTraversalTests
/// guards that side).
/// </summary>
public class UtilityAgentRecoveryTests
{
    /// <summary>r=0, interval=1: pure argmax every tick — assertions are exact.</summary>
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    /// <summary>A small side platform left of a wide main platform, with a pit
    /// between them. The opponent stands mid-main.</summary>
    private static GameGenome SideAndMain()
    {
        var brawler = new CharacterGenome("B", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Move(), 0) }, new[] { 0, 0, 0, 0, 0 });
        var stage = new StageGenome(new[]
        {
            new PlatformGene(-8, -3, 2, 1),  // side: x [-8,-6], top -2
            new PlatformGene(-2, -3, 8, 1),  // main: x [-2, 6], top -2
        });
        return new GameGenome(new[] { brawler, brawler }, stage);
    }

    [Fact]
    public void RecoveryTargetsTheNearestReachablePlatformNotTheOpponents()
    {
        // Falling into the pit with no horizontal momentum, side platform 0.7 units
        // away, opponent's main platform 3.5 away and also reachable: the old rule
        // aimed at the main (nearest the opponent, +1); the nearest rule goes -1.
        var world = new SimWorld(SideAndMain());
        world.Players[0].Position = new Vec2(-5.5f, -2.5f);
        world.Players[1].Position = new Vec2(3f, -1.4f);
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        InputFrame input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal);
    }

    [Fact]
    public void AnOffStageChaserTurnsBackAndRecoversInsteadOfSelfDestructing()
    {
        // Mid-chase past the main platform's left edge, still moving OUTWARD: nothing
        // is reachable ahead, so the momentum split falls back to the nearest platform
        // behind — the agent turns around (+1) and actually lands, keeping its stock.
        var world = new SimWorld(SideAndMain());
        SimPlayer chaser = world.Players[0];
        chaser.Position = new Vec2(-11f, -2.5f); // past the side platform too
        chaser.Velocity = new Vec2(-3f, 0.5f);   // still flying away from the stage
        world.Players[1].Position = new Vec2(3f, -1.4f);

        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        Assert.Equal(1f, agent.GetInput(world, 0).Horizontal);

        Span<InputFrame> inputs = stackalloc InputFrame[2];
        bool landed = false;
        for (int t = 0; t < 600 && !landed; t++)
        {
            inputs[0] = agent.GetInput(world, 0);
            inputs[1] = InputFrame.Neutral;
            world.Tick(inputs);
            landed = chaser.IsGrounded;
        }
        Assert.True(landed, $"chaser never landed (pos {chaser.Position.X:F2},{chaser.Position.Y:F2})");
        Assert.Equal(3, chaser.Stocks); // recovered — no self-destruct
    }
}
