using BrawlerSim.Determinism;
using BrawlerSim.Params;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

public class SimPlayerFsmTests
{
    private static SimWorld GroundedWorld(out SimPlayer player)
    {
        var world = new SimWorld(TestGames.FlatArena());
        player = world.Players[0];
        player.Position = new Vec2(-4f, -1.4f);
        world.Players[1].Position = new Vec2(6f, -1.4f);
        // Settle until standing on the floor.
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
    public void GroundJumpEntersAirState()
    {
        SimWorld world = GroundedWorld(out SimPlayer player);
        Assert.Equal(PlayerState.Idle, player.State);
        TickWith(world, new InputFrame(0f, 0f, Jump: true, Actions: 0));
        Assert.Equal(PlayerState.Air, player.State);
        Assert.True(player.Velocity.Y > 0f);
    }

    [Fact]
    public void HeldJumpChainsGroundJumpIntoAirJump()
    {
        // Unity parity: the AI's level-based jump chains both jumps on consecutive ticks.
        SimWorld world = GroundedWorld(out SimPlayer player);
        var jump = new InputFrame(0f, 0f, Jump: true, Actions: 0);
        TickWith(world, jump);
        Assert.Equal(PlayerState.Air, player.State);
        TickWith(world, jump);
        Assert.Equal(PlayerState.AirJumpsExhausted, player.State);
        Assert.True(player.JumpsExhausted);
        TickWith(world, jump); // no third jump
        Assert.Equal(PlayerState.AirJumpsExhausted, player.State);
    }

    [Fact]
    public void LandingRestoresIdleAndJumps()
    {
        SimWorld world = GroundedWorld(out SimPlayer player);
        var jump = new InputFrame(0f, 0f, Jump: true, Actions: 0);
        TickWith(world, jump);
        TickWith(world, jump); // exhausted, rising
        for (int i = 0; i < 600 && player.State != PlayerState.Idle; i++)
        {
            TickWith(world, InputFrame.Neutral);
        }
        Assert.Equal(PlayerState.Idle, player.State);
        Assert.False(player.JumpsExhausted);
        Assert.True(player.IsGrounded);
    }

    [Fact]
    public void AttackRunsWarmUpExecuteCoolDownWithExactTickCounts()
    {
        SimWorld world = GroundedWorld(out SimPlayer player);
        TickWith(world, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)));
        Assert.Equal(PlayerState.WarmUp, player.State);

        int warmUpTicks = 1, activeTicks = 0, coolDownTicks = 0;
        for (int i = 0; i < 200 && player.State != PlayerState.Idle; i++)
        {
            TickWith(world, InputFrame.Neutral);
            if (player.State == PlayerState.WarmUp) warmUpTicks++;
            if (player.State == PlayerState.Attack) activeTicks++;
            if (player.State == PlayerState.CoolDown) coolDownTicks++;
        }
        // 0.2 s / 0.1 s / 0.2 s at 60 Hz.
        Assert.Equal(12, warmUpTicks);
        Assert.Equal(6, activeTicks);
        Assert.Equal(12, coolDownTicks);
        Assert.Equal(PlayerState.Idle, player.State);
    }

    [Fact]
    public void NoAttacksOnceAirJumpsAreExhausted()
    {
        SimWorld world = GroundedWorld(out SimPlayer player);
        var jump = new InputFrame(0f, 0f, Jump: true, Actions: 0);
        TickWith(world, jump);
        TickWith(world, jump);
        Assert.Equal(PlayerState.AirJumpsExhausted, player.State);
        TickWith(world, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)));
        Assert.Equal(PlayerState.AirJumpsExhausted, player.State); // Unity parity: ignored
    }

    [Fact]
    public void SelfMovementCannotReduceExternalVelocity()
    {
        // Defect #4 fix: knockback speed above the cap is preserved when holding toward it.
        SimWorld world = GroundedWorld(out SimPlayer player);
        player.Velocity = new Vec2(20f, 0f);
        TickWith(world, new InputFrame(1f, 0f, false, 0));
        Assert.True(player.Velocity.X > 10f, $"self-movement bled knockback speed to {player.Velocity.X}");
    }

    [Fact]
    public void ReversingDirectionAcceleratesInsteadOfSnapping()
    {
        // Defect #4 fix: tapping the opposite direction must not snap to full speed.
        SimWorld world = GroundedWorld(out SimPlayer player);
        player.Velocity = new Vec2(-4f, 0f);
        TickWith(world, new InputFrame(1f, 0f, false, 0));
        Assert.True(player.Velocity.X < 0f, $"velocity snapped to {player.Velocity.X}");
        Assert.True(player.Velocity.X > -4f);
    }

    [Fact]
    public void FacingFollowsLastHorizontalInput()
    {
        SimWorld world = GroundedWorld(out SimPlayer player);
        TickWith(world, new InputFrame(-1f, 0f, false, 0));
        Assert.Equal(-1, player.Facing);
        // Hitbox mirrors with facing: offset (1, 0) flips to the left side.
        Assert.True(player.Hitbox.Center.X < player.Position.X);
        TickWith(world, new InputFrame(1f, 0f, false, 0));
        Assert.Equal(1, player.Facing);
    }
}
