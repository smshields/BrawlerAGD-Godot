using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// Chain defense (2026-09-10, designer-directed — CHANGE_LOG #37). The designer's bug
/// report: inward-knockback kits chained stunned victims because (a) the stunned DI
/// hold always pointed toward stage center — mid-stage that is usually INTO the
/// attacker — and (b) nothing treated stun exit as a moment to defend, so the victim
/// idled through the attacker's next swing. Two instrument changes: percent-aware DI
/// (low damage = break adjacency away from the attacker; kill percents keep the
/// survival hold toward center) and a stun-exit chain-escape trigger on the defense
/// channel (attacker in range, no counter-hit in hand).
/// </summary>
public class UtilityAgentChainDefenseTests
{
    /// <summary>r=0, interval=1: pure argmax every tick — assertions are exact.</summary>
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    /// <summary>Two identical melee-only brawlers on the wide FlatArena floor. The DI
    /// gene is live (TestGames.Character defaults it to 0, which gates the DI hold
    /// off entirely — these tests are ABOUT the hold).</summary>
    private static GameGenome BrawlerPair()
    {
        var melee = new MoveGenome(TestGames.Move(), 0);
        CharacterGenome brawler = new("Brawler", 3, 0,
            TestGames.Character((CharacterParams.DirectionalInfluence, 0.2f)),
            new[] { melee }, new[] { 0, 0, 0, 0, 0 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { brawler, brawler }, stage);
    }

    private static SimWorld Grounded(GameGenome genome, float p0X, float p1X)
    {
        var world = new SimWorld(genome);
        world.Players[0].Position = new Vec2(p0X, -1.4f);
        world.Players[1].Position = new Vec2(p1X, -1.4f);
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    [Fact]
    public void LowDamageStunDIHoldsAwayFromTheAttackerNotTowardCenter()
    {
        // P0 stands LEFT of center with the attacker center-ward (to its right): the
        // old always-toward-center hold and the attacker's direction coincide — the
        // exact geometry that fed chains. At low damage the hold must break away.
        SimWorld world = Grounded(BrawlerPair(), -2f, 0.5f);
        world.Players[0].ApplyHit(5f, Vec2.Zero, stunTicks: 30);

        InputFrame input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal); // away from the attacker (and center)
    }

    [Fact]
    public void HighDamageStunKeepsThePreChangeSurvivalBehavior()
    {
        // Same geometry at kill percent: the DI block's contribution reverts to the
        // pre-change survival hold (toward center, 1.8), and — exactly as before the
        // change — EvadeBehavior's high-damage retreat (2.5 at 100 damage) outranks
        // it in this grounded geometry, so the held direction stays AWAY (−1). The
        // point pinned here: at kill percents nothing about the hold changed.
        SimWorld world = Grounded(BrawlerPair(), -2f, 0.5f);
        world.Players[0].ApplyHit(100f, Vec2.Zero, stunTicks: 30);

        InputFrame input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal(-1f, input.Horizontal);
    }

    [Fact]
    public void StunExitInChainRangeTriggersTheDefenseChannel()
    {
        // Attacker inside ChainEscapeRange (3) but outside P0's melee reach (no
        // counter-hit in hand), no telegraph yet: the first decision after stun ends
        // must be a defense pick, not a re-approach. Under argmax with no shield,
        // the channel picks Jump (2.0 over None 1.0): hop away, no attack press.
        SimWorld world = Grounded(BrawlerPair(), -1f, 1.5f);
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        world.Players[0].ApplyHit(5f, Vec2.Zero, stunTicks: 6);

        for (int guard = 0; world.Players[0].State == PlayerState.Stun && guard < 60; guard++)
        {
            agent.GetInput(world, 0); // the agent observes the stun (arms _wasStunned)
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.NotEqual(PlayerState.Stun, world.Players[0].State);

        InputFrame exit = agent.GetInput(world, 0);
        Assert.True(exit.Jump);
        Assert.Equal(-1f, exit.Horizontal); // away from the attacker
        Assert.Equal(0, exit.Actions);      // an escape, not a swing
    }

    [Fact]
    public void StunExitWithACounterHitInHandStillSwings()
    {
        // Trade-commit is preserved: adjacent enough that P0's melee already reaches,
        // the counter-hit IS the chain break — the escape trigger must not override it.
        SimWorld world = Grounded(BrawlerPair(), -1f, 0.2f);
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        world.Players[0].ApplyHit(5f, Vec2.Zero, stunTicks: 6);

        for (int guard = 0; world.Players[0].State == PlayerState.Stun && guard < 60; guard++)
        {
            agent.GetInput(world, 0);
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }

        InputFrame exit = agent.GetInput(world, 0);
        Assert.NotEqual(0, exit.Actions); // the counter-attack fires
    }
}
