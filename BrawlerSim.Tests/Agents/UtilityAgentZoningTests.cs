using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// The agent zoning upgrade (2026-09-04, designer-directed — CHANGE_LOG #35):
/// projectile/melee score parity, the release-moment lead (aim at where the target
/// will be when the shot actually comes out — which doubles as the commit gate
/// against point-blank releases), and the zoner stance (a projectile carrier plays
/// range: retreats inside the gate, plants in the firing pocket, still approaches
/// runaways). The standing utility golden is the no-regression proof for every
/// projectile-free matchup — nothing here draws or scores for melee-only kits.
/// </summary>
public class UtilityAgentZoningTests
{
    /// <summary>r=0, interval=1: pure argmax every tick — assertions are exact.</summary>
    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    /// <summary>P1 = melee + projectile (buttons 0/1), P2 = melee only. The wide
    /// FlatArena floor (x in [-8, 8], top y = -2).</summary>
    private static GameGenome ZonerArena(params (string Key, float Value)[] projectileOverrides)
    {
        var melee = new MoveGenome(TestGames.Move(), 0);
        CharacterGenome zoner = new("Zoner", 3, 0, TestGames.Character(),
            new[] { melee, new MoveGenome(TestGames.Projectile(projectileOverrides), 0, MoveType.Projectile) },
            new[] { 0, 1, 0, 0, 0 });
        CharacterGenome brawler = new("Brawler", 3, 0, TestGames.Character(),
            new[] { melee }, new[] { 0, 0, 0, 0, 0 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { zoner, brawler }, stage);
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
    public void ZonerRetreatsInsideTheGateAndPlantsInThePocket()
    {
        GameGenome genome = ZonerArena();
        var agent = new UtilityAgent(new Pcg32(1), Greedy);

        // Inside the retreat band (dx 3 < 4): back AWAY from the opponent.
        SimWorld close = Grounded(genome, -1f, 2f);
        InputFrame input = agent.GetInput(close, 0);
        Assert.Equal(-1f, input.Horizontal);

        // In the firing pocket (dx 6): plant (neutral) — the shot does the work.
        SimWorld pocket = Grounded(genome, -3f, 3f);
        input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(pocket, 0);
        Assert.Equal(0f, input.Horizontal);

        // Beyond the pocket (dx 12 > 10): the normal approach stack takes over.
        SimWorld far = Grounded(genome, -6f, 6f);
        input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(far, 0);
        Assert.Equal(1f, input.Horizontal);

        // A melee-only character at the same spacing still approaches (the stance
        // belongs to projectile carriers alone — P2 here).
        input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(pocket, 1);
        Assert.Equal(-1f, input.Horizontal);
    }

    [Fact]
    public void ZonerNeverBacksOffALedge()
    {
        // Zoner at the LEFT edge of the floor, opponent closing from the right:
        // retreating left walks off at x < -8 — the shared retreat helper flips
        // toward stage center instead of dropping into the pit.
        GameGenome genome = ZonerArena();
        SimWorld world = Grounded(genome, -7.6f, -4.8f);
        InputFrame input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.True(input.Horizontal >= 0f, "zoner backed off the ledge");
    }

    [Fact]
    public void CommitGateRefusesShotsAClosingTargetWouldEatPointBlank()
    {
        // The lead aims at the RELEASE moment: a target sprinting in at the gate's
        // edge is inside 2.5 units by release, so the shot is refused; the same
        // target standing still is a legal shot. Warm-up 0.5 s x closing ~8 u/s
        // erases the whole gap from dx 4.
        GameGenome genome = ZonerArena((ProjectileParams.WarmUpDuration, 0.5f));
        SimWorld world = Grounded(genome, -2f, 2f); // dx 4: inside retreat band, gate-legal
        // Give the opponent a hard closing velocity (as a real approach would).
        world.Players[1].Velocity = new Vec2(-8f, 0f);
        var agent = new UtilityAgent(new Pcg32(1), Greedy);
        InputFrame closing = agent.GetInput(world, 0);
        Assert.Equal<byte>(0, closing.Actions); // no commit into an interrupt

        world.Players[1].Velocity = Vec2.Zero;
        InputFrame still = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.NotEqual<byte>(0, still.Actions); // the standing target is fair game
        Assert.Equal<byte>(1 << 1, still.Actions); // and it is the projectile button
    }

    [Fact]
    public void LeadRefusesTargetsThatOutrunTheShotByRelease()
    {
        // A target at the range ceiling RETREATING at speed is out of range by
        // release; the same target standing still is shootable. Velocity 8 x TTL 2 s
        // = 16 max range; dx 15 + retreat 6 u/s x 0.5 s warm-up = 18 > 16 + slack.
        GameGenome genome = ZonerArena((ProjectileParams.WarmUpDuration, 0.5f));
        SimWorld world = Grounded(genome, -7.5f, 7.5f); // dx 15
        world.Players[1].Velocity = new Vec2(6f, 0f);
        InputFrame fleeing = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal<byte>(0, fleeing.Actions);

        world.Players[1].Velocity = Vec2.Zero;
        InputFrame still = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal<byte>(1 << 1, still.Actions);
    }

    [Fact]
    public void ProjectileAndMeleeCandidatesScoreAtParity()
    {
        // Same damage, both in range → the attack channel's argmax tie-break (lower
        // index) decides, proving neither carries a type preference anymore. Melee
        // sits at slot 0, so the tie goes to melee at point blank (where the gate
        // silences the projectile anyway) and to the projectile alone at range —
        // asserted through behavior: at dx 6 ONLY the projectile can hit, and it
        // fires despite the old 2.6-vs-4.0 handicap having made this exact spot a
        // frequent no-op under sampling.
        GameGenome genome = ZonerArena();
        SimWorld world = Grounded(genome, -3f, 3f);
        InputFrame input = new UtilityAgent(new Pcg32(1), Greedy).GetInput(world, 0);
        Assert.Equal<byte>(1 << 1, input.Actions);
    }
}
