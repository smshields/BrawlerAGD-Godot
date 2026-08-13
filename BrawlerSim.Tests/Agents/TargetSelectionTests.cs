using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Agents;

/// <summary>
/// UtilityAgent target selection for 3-4 players (2026-08-12, DEVIATIONS #32):
/// nearest non-eliminated enemy, present preferred over blacked-out, vulnerable over
/// spawn-immune, squared-distance nearest within a tier, lower index on ties, zero
/// RNG. The N=2 reduction (target ≡ the single opponent, whatever its condition) is
/// proven by the UNMOVED utility golden; these tests cover the multi-enemy choices.
/// </summary>
public class TargetSelectionTests
{
    private static SimWorld Arena4()
    {
        var world = new SimWorld(TestGames.FlatArenaN(4));
        world.Players[0].Position = new Vec2(-6f, -1.4f);
        world.Players[1].Position = new Vec2(-3f, -1.4f);
        world.Players[2].Position = new Vec2(2f, -1.4f);
        world.Players[3].Position = new Vec2(6f, -1.4f);
        return world;
    }

    [Fact]
    public void NearestLiveEnemyIsTheTarget()
    {
        SimWorld world = Arena4();
        Assert.Same(world.Players[1], UtilityAgent.SelectTarget(world, 0)); // 3 < 8 < 12
        Assert.Same(world.Players[3], UtilityAgent.SelectTarget(world, 2)); // 4 < 5 < 8
    }

    [Fact]
    public void NearestUsesEuclideanDistanceNotJustX()
    {
        SimWorld world = Arena4();
        // p1 close in x but far in y; p2 slightly farther in x but level.
        world.Players[1].Position = new Vec2(-5f, 6f);  // distSq = 1 + 54.76... (7.4²)
        world.Players[2].Position = new Vec2(-2f, -1.4f); // distSq = 16
        Assert.Same(world.Players[2], UtilityAgent.SelectTarget(world, 0));
    }

    [Fact]
    public void EquidistantEnemiesTieBreakToTheLowerIndex()
    {
        SimWorld world = Arena4();
        world.Players[1].Position = new Vec2(-3f, -1.4f); // 3 to the right of p0
        world.Players[2].Position = new Vec2(-9f, -1.4f); // 3 to the left of p0
        Assert.Same(world.Players[1], UtilityAgent.SelectTarget(world, 0));
    }

    [Fact]
    public void RespawningEnemiesAreChasedOnlyAsALastResort()
    {
        SimWorld world = Arena4();
        world.Players[1].RespawnBlackoutLeft = 60; // nearest, but absent
        Assert.Same(world.Players[2], UtilityAgent.SelectTarget(world, 0));

        world.Players[2].RespawnBlackoutLeft = 60;
        world.Players[3].RespawnBlackoutLeft = 60; // everyone absent → nearest absent
        Assert.Same(world.Players[1], UtilityAgent.SelectTarget(world, 0));
    }

    [Fact]
    public void SpawnImmuneEnemiesRankBelowVulnerableOnes()
    {
        SimWorld world = Arena4();
        world.Players[1].SpawnInvulnTicksLeft = 60; // nearest, but unhittable (#29)
        Assert.Same(world.Players[2], UtilityAgent.SelectTarget(world, 0));
        // ...but an immune PRESENT enemy still beats a blacked-out one.
        world.Players[2].RespawnBlackoutLeft = 60;
        world.Players[3].RespawnBlackoutLeft = 60;
        Assert.Same(world.Players[1], UtilityAgent.SelectTarget(world, 0));
    }

    [Fact]
    public void EliminatedEnemiesAreNeverTargeted()
    {
        SimWorld world = Arena4();
        world.Players[1].Eliminated = true;
        Assert.Same(world.Players[2], UtilityAgent.SelectTarget(world, 0));
        world.Players[2].Eliminated = true;
        Assert.Same(world.Players[3], UtilityAgent.SelectTarget(world, 0));
    }

    [Fact]
    public void EliminatedAgentsEmitNeutralInputWithoutSpendingRng()
    {
        SimWorld world = Arena4();
        world.Players[0].Eliminated = true;
        var agentRng = new Pcg32(7, 0);
        var reference = new Pcg32(7, 0);
        var agent = new UtilityAgent(agentRng);
        InputFrame frame = agent.GetInput(world, 0);
        Assert.Equal(InputFrame.Neutral, frame);
        Assert.Equal(reference.NextUInt(), agentRng.NextUInt()); // stream untouched
    }

    [Fact]
    public void FourUtilityAgentsFightAFullMatchToCompletion()
    {
        // Integration probe: a real 4P AI-vs-AI STOCK match on a generated genome
        // (shields/dashes/projectiles in the pool) terminates, eliminates in a valid
        // order, and produces a total placement.
        var config = GenerationConfig.Default with { CharacterCount = 4 };
        GameGenome genome = GameGenome.Generate(config, new Pcg32(11));
        var sources = new IInputSource[4];
        for (int i = 0; i < 4; i++)
        {
            sources[i] = new UtilityAgent(new Pcg32(20260812, (ulong)i));
        }
        MatchResult result = MatchRunner.Run(genome, sources);
        Assert.True(result.Ticks <= MatchConfig.Default.MaxTicks);
        Assert.NotNull(result.Placements);
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Placements!.OrderBy(p => p).ToArray());
        // Someone lost stocks — the four actually fought.
        Assert.Contains(result.Players, p => p.RemainingStocks < 3 || p.TotalDamageTaken > 0f);
    }

    /// <summary>
    /// Golden 4P hash (pinned 2026-08-12, feature-final for phases 1-3): a generated
    /// four-character genome under four utility agents. Any drift is a determinism-
    /// contract release blocker, exactly like the 2P goldens.
    /// </summary>
    [Fact]
    public void GoldenFourPlayerMatchHashMatches()
    {
        var config = GenerationConfig.Default with { CharacterCount = 4 };
        GameGenome genome = GameGenome.Generate(config, new Pcg32(11));
        var sources = new IInputSource[4];
        for (int i = 0; i < 4; i++)
        {
            sources[i] = new UtilityAgent(new Pcg32(20260812, (ulong)i));
        }
        MatchResult result = MatchRunner.Run(genome, sources);
        // Pinned 2026-08-12 (first pin — the 4P mode is new with this feature):
        // covers N-player spawning, all-pairs contact/hits, elimination, the gated
        // hash suffix, and nearest-enemy targeting end to end.
        Assert.Equal(8893643871391191293UL, result.FinalHash);
    }
}
