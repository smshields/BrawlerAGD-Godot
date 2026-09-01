using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Thin platforms, sim layer (2026-09-01, FEATURES.md §Thin Platforms;
/// docs/features/thin-platforms.md): one-way collision (land from above; pass from
/// below and from the side), the crouch drop-through with its per-character delay
/// gene (hand-computed tick counts), the timer reset on crouch release, top-only
/// projectile destruction, and determinism on thin stages. The gated StateHash means
/// all-solid matches hash exactly as before — the standing goldens are that proof.
/// </summary>
public class ThinPlatformSimTests
{
    // Floor top y = -2; thin slice [1.8, 2.0] spanning x in [-3, 3] (0.2 thickness
    // at the top of the gene cell [1, 2]).
    private static readonly PlatformGene Floor = new(-8, -3, 16, 1);
    private static readonly PlatformGene ThinLedge = new(-3, 1, 6, 1, Thin: true);

    private static GameGenome ThinArena(
        (string Key, float Value)[]? characterOverrides = null,
        PlatformGene[]? platforms = null)
    {
        var character = TestGames.Character(characterOverrides ?? Array.Empty<(string, float)>());
        CharacterGenome Make(string name) =>
            new(name, 3, 0, character, new[] { new MoveGenome(TestGames.Move(), 0) });
        var stage = new StageGenome(platforms ?? new[] { Floor, ThinLedge });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
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

    private static void TickHold(SimWorld world, InputFrame p0, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            world.Tick(stackalloc[] { p0, InputFrame.Neutral });
        }
    }

    private static readonly InputFrame HoldDown = new(0f, -1f, false, 0);

    [Fact]
    public void FallingPlayerLandsOnAThinPlatformFromAbove()
    {
        SimWorld world = Settled(ThinArena(), new Vec2(0f, 4f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];
        Assert.True(player.IsGrounded);
        Assert.Equal(2.001f, player.Body.Bottom, 0.005f); // the slice TOP, skin-resting
        Assert.Equal(PlayerState.Idle, player.State);
    }

    [Fact]
    public void ExtremeDownwardVelocityStillLandsOnTheThinSlice()
    {
        // The slice (0.2) is thinner than a substep (0.25): the surface-CROSSING test,
        // not overlap, must catch a knockback-speed fall (no tunneling).
        SimWorld world = Settled(ThinArena(), new Vec2(0f, 4f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];
        player.Position = new Vec2(0f, 6f);
        player.Velocity = new Vec2(0f, -60f);
        TickHold(world, InputFrame.Neutral, 10);
        Assert.True(player.IsGrounded);
        Assert.Equal(2.001f, player.Body.Bottom, 0.005f);
    }

    [Fact]
    public void JumpFromBelowPassesUpThroughAndLandsOnTop()
    {
        // Under a SOLID ledge the jump would bonk on the underside; a thin ledge lets
        // the body rise through, then catches it on the way down.
        SimWorld world = Settled(
            ThinArena(new[] { (CharacterParams.GroundJumpForce, 15f) }),
            new Vec2(0f, -1.4f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral });
        bool roseAbove = false;
        for (int i = 0; i < 240 && !(player.IsGrounded && roseAbove); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            roseAbove |= player.Body.Bottom > 2.1f;
        }
        Assert.True(roseAbove, "never rose above the thin surface");
        Assert.True(player.IsGrounded);
        Assert.Equal(2.001f, player.Body.Bottom, 0.005f); // landed ON it, from above
    }

    [Fact]
    public void WalkingPassesThroughAThinPlatformSide()
    {
        // A ground-level thin block: a walker passes through where a solid one clamps.
        var thinBlock = new PlatformGene(2, -2, 3, 1, Thin: true);
        SimWorld thin = Settled(
            ThinArena(platforms: new[] { Floor, thinBlock }),
            new Vec2(0f, -1.4f), new Vec2(-6.5f, -1.4f));
        TickHold(thin, new InputFrame(1f, 0f, false, 0), 150);
        Assert.True(thin.Players[0].Position.X > 5.5f,
            $"blocked at x = {thin.Players[0].Position.X}");

        SimWorld solid = Settled(
            ThinArena(platforms: new[] { Floor, thinBlock with { Thin = false } }),
            new Vec2(0f, -1.4f), new Vec2(-6.5f, -1.4f));
        TickHold(solid, new InputFrame(1f, 0f, false, 0), 150);
        Assert.True(solid.Players[0].Position.X < 2f,
            $"solid wall failed to block (x = {solid.Players[0].Position.X})");
    }

    [Fact]
    public void CrouchDropFiresAfterSinkPlusTheDelayGene()
    {
        // Hand-computed (crouchSpeed 0.1 -> 6 sink ticks; dropThroughDelay 0.1 -> 6):
        // tick 1 enters Crouch/Sink, ticks 2-7 sink, ticks 8-13 count the delay down,
        // tick 14 fires the drop = CrouchStageTicks + DropThroughDelayTicks + 2.
        SimWorld world = Settled(
            ThinArena(new[] { (CharacterParams.DropThroughDelay, 0.1f) }),
            new Vec2(0f, 2.7f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];
        Assert.Equal(6, player.CrouchStageTicks);
        Assert.Equal(6, player.DropThroughDelayTicks);

        int expected = player.CrouchStageTicks + player.DropThroughDelayTicks + 2;
        for (int tick = 1; tick <= expected; tick++)
        {
            Assert.Equal(0, player.DropThroughs);
            world.Tick(stackalloc[] { HoldDown, InputFrame.Neutral });
        }
        Assert.Equal(1, player.DropThroughs);
        Assert.Equal(PlayerState.Air, player.State);
        Assert.False(player.JumpsExhausted); // the drop spends no air budget

        // Still holding down: fast-falls through, clears the ignore, and lands on the
        // floor below — where the held down re-enters crouch (no thin support there).
        TickHold(world, HoldDown, 90);
        Assert.Equal(-1, player.DropThroughPlatform);
        Assert.Equal(-1.999f, player.Body.Bottom, 0.005f);
        Assert.Equal(1, player.DropThroughs); // the solid floor never drops
    }

    [Fact]
    public void LegacyZeroDelayDropsOnTheFirstHeldTick()
    {
        // Pre-v12 characters load dropThroughDelay 0: the drop fires on the first
        // Held tick = CrouchStageTicks + 2.
        SimWorld world = Settled(ThinArena(), new Vec2(0f, 2.7f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];
        Assert.Equal(0, player.DropThroughDelayTicks);
        int expected = player.CrouchStageTicks + 2;
        for (int tick = 1; tick <= expected; tick++)
        {
            Assert.Equal(0, player.DropThroughs);
            world.Tick(stackalloc[] { HoldDown, InputFrame.Neutral });
        }
        Assert.Equal(1, player.DropThroughs);
    }

    [Fact]
    public void ReleasingCrouchResetsTheDropTimer()
    {
        // Designer rule: the timer re-arms on every crouch. Hold three delay ticks,
        // release through the full rise, re-crouch — the second attempt needs the
        // FULL sink + delay again (nothing carried over).
        SimWorld world = Settled(
            ThinArena(new[] { (CharacterParams.DropThroughDelay, 0.1f) }),
            new Vec2(0f, 2.7f), new Vec2(6.5f, -1.4f));
        SimPlayer player = world.Players[0];

        TickHold(world, HoldDown, 1 + player.CrouchStageTicks + 3); // entry + sink + 3 delay ticks
        Assert.Equal(0, player.DropThroughs);
        TickHold(world, InputFrame.Neutral, player.CrouchStageTicks + 2); // release: rise out fully
        Assert.Equal(PlayerState.Idle, player.State);

        int full = player.CrouchStageTicks + player.DropThroughDelayTicks + 2;
        for (int tick = 1; tick <= full; tick++)
        {
            Assert.Equal(0, player.DropThroughs);
            world.Tick(stackalloc[] { HoldDown, InputFrame.Neutral });
        }
        Assert.Equal(1, player.DropThroughs);
    }

    [Fact]
    public void ProjectileIsDestroyedCrossingAThinTopDownward()
    {
        // A gravity bolt fired from a raised perch arcs down through the thin slice
        // below — the downward surface crossing consumes it well before TTL, the
        // floor, or the blast line could.
        var perch = new PlatformGene(-8, 0, 3, 1);
        var ledge = new PlatformGene(-2, -1, 6, 1, Thin: true); // slice [-0.2, 0], x in [-2, 4]
        CharacterGenome Shooter(string name) => new(name, 3, 0, TestGames.Character(),
            new[]
            {
                new MoveGenome(TestGames.Projectile(
                    (BrawlerSim.Genome.ProjectileParams.AffectedByGravity, 1f)), 0, MoveType.Projectile),
            },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(
            new[] { Shooter("P1"), Shooter("P2") },
            new StageGenome(new[] { Floor, perch, ledge }));
        SimWorld world = Settled(genome, new Vec2(-6.5f, 1.6f), new Vec2(7f, -1.4f));

        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        int waited = 0;
        while (world.Projectiles.Count == 0 && waited++ < 60)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Single(world.Projectiles);

        float lastY = world.Projectiles[0].Position.Y;
        float lastX = world.Projectiles[0].Position.X;
        int alive = 0;
        while (world.Projectiles.Count > 0 && alive++ < 90)
        {
            lastY = world.Projectiles[0].Position.Y;
            lastX = world.Projectiles[0].Position.X;
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Empty(world.Projectiles);
        // Died at the slice: last alive position just above the thin top (y = 0),
        // inside its span — not at the floor (-2), not at the blast line.
        Assert.InRange(lastY, -0.35f, 0.6f);
        Assert.InRange(lastX, -2f, 4f);
    }

    [Fact]
    public void ProjectileFromBelowPassesUpThroughAThinPlatform()
    {
        // A sine bolt rising through a thin slice from BELOW survives the upward
        // crossing (designer: thin platforms do not destroy from the bottom) and is
        // consumed only when the wave comes back down through the top.
        var ledge = new PlatformGene(-8, -2, 16, 1, Thin: true); // slice [-1.2, -1.0]
        CharacterGenome Shooter(string name) => new(name, 3, 0, TestGames.Character(),
            new[]
            {
                new MoveGenome(TestGames.Projectile(
                    (BrawlerSim.Genome.ProjectileParams.PathShape, 1f),      // sine, amplitude 0.8
                    (BrawlerSim.Genome.ProjectileParams.PathScalar, 0.5f)),  // 0.5 Hz
                    0, MoveType.Projectile),
            },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(
            new[] { Shooter("P1"), Shooter("P2") },
            new StageGenome(new[] { Floor, ledge }));
        // Bolt origin y = -1.5 (below the slice); the sine peaks at -0.7 (above it).
        SimWorld world = Settled(genome, new Vec2(-6.2f, -1.4f), new Vec2(7f, -1.4f));

        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        int waited = 0;
        while (world.Projectiles.Count == 0 && waited++ < 60)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Single(world.Projectiles);

        bool roseAbove = false;
        int ticks = 0;
        while (world.Projectiles.Count > 0 && ticks++ < 110)
        {
            roseAbove |= world.Projectiles[0].Position.Y > -0.98f;
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.True(roseAbove, "the bolt never rose above the slice — bad fixture");
        // Consumed on the DOWNWARD crossing (about 0.79 s after launch) — long
        // before its 2 s TTL, which is the only other way this bolt could die.
        Assert.Empty(world.Projectiles);
        Assert.True(ticks < 75, $"bolt lived {ticks} ticks — died by TTL, not the slice");
    }

    [Fact]
    public void ThinStageMatchesAreDeterministic()
    {
        // Same genome + agent seeds twice: identical final hash (the thin suffix and
        // every thin code path are inside the determinism contract).
        var genome = ThinArena(new[] { (CharacterParams.DropThroughDelay, 0.2f) });
        Assert.NotEqual(0UL, Run().FinalHash);
        Assert.Equal(Run().FinalHash, Run().FinalHash);

        MatchResult Run() => MatchRunner.Run(genome, new IInputSource[]
        {
            new UtilityAgent(new Pcg32(2026, 0)),
            new UtilityAgent(new Pcg32(2026, 1)),
        });
    }
}
