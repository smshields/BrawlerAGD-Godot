using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Hand-computed tests for Spawning Behaviors (2026-07-22,
/// docs/features/spawn-and-polish.md): the invulnerable/intangible distinction, the
/// two independent timers, attack- and leave-driven intangibility loss, the respawn
/// blackout, and the pre-feature-off parity path.
/// </summary>
public class SpawnBehaviorTests
{
    private const int Fps = SimInfo.TicksPerSecond;

    /// <summary>Flat floor, spawns apart, with the two spawn durations set (seconds).
    /// durations 0 ⇒ feature off.</summary>
    private static GameGenome Game(float platformSeconds, float invulnSeconds)
    {
        var platforms = new[] { new PlatformGene(-8, -3, 16, 1) }; // top −2, span [−8, 8]
        ParamSet stage = StageRules.LegacyParams(platforms).With(
            (StageParams.PlatformSpawnDuration, platformSeconds),
            (StageParams.SpawnInvulnDuration, invulnSeconds),
            (StageParams.Spawn1X, -4f), (StageParams.Spawn1Y, 0f),
            (StageParams.Spawn2X, 4f), (StageParams.Spawn2Y, 0f));
        CharacterGenome Make(string n) =>
            new(n, 3, 0, TestGames.Character(), new[] { new MoveGenome(TestGames.Move(), 0) });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, new StageGenome(platforms, stage));
    }

    private static InputFrame Attack => new(0f, 0f, false, InputFrame.ActionBit(0));

    [Fact]
    public void FeatureOffLeavesSpawnStateInert()
    {
        var world = new SimWorld(Game(0f, 0f));
        foreach (SimPlayer p in world.Players)
        {
            Assert.False(p.SpawnPadActive);
            Assert.False(p.SpawnIntangible);
            Assert.Equal(0, p.SpawnInvulnTicksLeft);
            Assert.False(p.IsRespawning);
            Assert.Equal(PlayerState.Idle, p.State);
        }
    }

    [Fact]
    public void MatchStartMaterializesOnPadIntangibleAndInvulnerableNoBlackout()
    {
        var world = new SimWorld(Game(2f, 3f));
        foreach (SimPlayer p in world.Players)
        {
            Assert.True(p.SpawnPadActive);
            Assert.True(p.SpawnIntangible);
            Assert.True(p.SpawnDamageImmune);
            Assert.Equal(3 * Fps, p.SpawnInvulnTicksLeft);
            Assert.False(p.IsRespawning); // match start skips the blackout (designer)
        }
    }

    [Fact]
    public void InvulnerabilityEndsStrictlyOnItsTimer()
    {
        var world = new SimWorld(Game(5f, 1f)); // invuln 1 s = 60 ticks, platform 5 s
        for (int t = 0; t < 1 * Fps; t++)
        {
            Assert.True(world.Players[0].SpawnDamageImmune, $"tick {t}: should still be immune");
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        // Exactly at the timer, invulnerability is gone. (Still intangible-on-pad here,
        // so SpawnDamageImmune stays true via intangibility — assert the timer field.)
        Assert.Equal(0, world.Players[0].SpawnInvulnTicksLeft);
    }

    [Fact]
    public void AttackEndsIntangibilityButNotInvulnerability()
    {
        var world = new SimWorld(Game(5f, 3f));
        world.Tick(stackalloc[] { Attack, InputFrame.Neutral }); // P0 swings from the pad
        SimPlayer p0 = world.Players[0];
        Assert.False(p0.SpawnIntangible);          // attacking dropped intangibility
        Assert.True(p0.SpawnInvulnTicksLeft > 0);  // invulnerability rides its own timer
        Assert.True(p0.SpawnDamageImmune);         // still damage-proof (via invuln)
        Assert.Contains(p0.State, new[] { PlayerState.WarmUp, PlayerState.Attack });
    }

    [Fact]
    public void LeavingThePadDespawnsItAndEndsIntangibility()
    {
        var world = new SimWorld(Game(5f, 3f));
        world.Players[0].Position = new Vec2(0f, world.Players[0].Position.Y); // off the pad span
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        Assert.False(world.Players[0].SpawnPadActive);
        Assert.False(world.Players[0].SpawnIntangible);
    }

    [Fact]
    public void PlatformTimerExpiryEndsIntangibilityWhileInvulnPersists()
    {
        var world = new SimWorld(Game(1f, 3f)); // platform 1 s, invuln 3 s
        for (int t = 0; t < 1 * Fps; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        SimPlayer p0 = world.Players[0];
        Assert.False(p0.SpawnPadActive);      // platform lifetime elapsed
        Assert.False(p0.SpawnIntangible);
        Assert.True(p0.SpawnInvulnTicksLeft > 0); // longer invuln timer still running
        Assert.True(p0.SpawnDamageImmune);
    }

    [Fact]
    public void RespawnBlackoutThenMaterializeOnPad()
    {
        var world = new SimWorld(Game(2f, 3f));
        SimPlayer p0 = world.Players[0];
        int stocksBefore = p0.Stocks;
        // Fling P0 out of the blast zone; the KO tick starts the blackout.
        p0.Position = new Vec2(world.BlastZone.Right + 5f, 0f);
        p0.SpawnIntangible = false;
        p0.SpawnInvulnTicksLeft = 0; // clear match-start immunity so the KO registers
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        Assert.True(p0.IsRespawning);
        Assert.Equal(stocksBefore - 1, p0.Stocks);
        Assert.Equal(MatchConfig.Default.RespawnBlackoutTicks, p0.RespawnBlackoutLeft);

        // Blacked out for the full blackout, then reappears on the pad.
        for (int t = 0; t < MatchConfig.Default.RespawnBlackoutTicks; t++)
        {
            Assert.True(p0.IsRespawning, $"tick {t}: still in blackout");
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.False(p0.IsRespawning);
        Assert.True(p0.SpawnPadActive);
        Assert.True(p0.SpawnDamageImmune);
    }

    [Fact]
    public void IntangiblePlayerPhasesThroughTheOpponent()
    {
        var world = new SimWorld(Game(5f, 3f));
        SimPlayer a = world.Players[0];
        SimPlayer b = world.Players[1];
        // Overlap them; a is intangible (match start), b is not (cleared here).
        b.SpawnIntangible = false;
        a.Position = new Vec2(0f, -1.4f);
        b.Position = new Vec2(0f, -1.4f);
        Vec2 aBefore = a.Position;
        Vec2 bBefore = b.Position;
        SimPhysics.ResolvePlayerContact(a, b, world.Config);
        Assert.Equal(aBefore.X, a.Position.X); // no separation — a phases through b
        Assert.Equal(bBefore.X, b.Position.X);
    }

    [Fact]
    public void InvulnerableVictimTakesNoDamageButStillCollides()
    {
        var world = new SimWorld(Game(5f, 3f));
        SimPlayer a = world.Players[0];
        SimPlayer b = world.Players[1];
        // b invulnerable-but-tangible (left the pad, timer still running).
        b.SpawnIntangible = false;
        b.SpawnInvulnTicksLeft = 30;
        Assert.True(b.SpawnDamageImmune);
        // Collision still resolves for an invulnerable (non-intangible) body: overlap
        // them and confirm ResolvePlayerContact separates (a is NOT intangible either).
        a.SpawnIntangible = false;
        a.Position = new Vec2(0f, -1.4f);
        b.Position = new Vec2(0.1f, -1.4f);
        SimPhysics.ResolvePlayerContact(a, b, world.Config);
        Assert.True(MathF.Abs(a.Position.X - b.Position.X) > 0.1f); // separated — collision still resolves
    }
}
