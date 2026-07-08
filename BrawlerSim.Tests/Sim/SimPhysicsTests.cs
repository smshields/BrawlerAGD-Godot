using BrawlerSim.Determinism;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

public class SimPhysicsTests
{
    private static SimWorld FloatingWorld(out SimPlayer player, float x = 0f, float y = 5f)
    {
        var world = new SimWorld(TestGames.FlatArena());
        player = world.Players[0];
        player.Position = new Vec2(x, y);
        player.Velocity = Vec2.Zero;
        // Park the other player on the floor, clear of the test subject.
        world.Players[1].Position = new Vec2(6f, -1.4f);
        return world;
    }

    [Fact]
    public void GravityAndDragIntegrateExactlyLikeBox2D()
    {
        SimWorld world = FloatingWorld(out SimPlayer player);
        float y0 = player.Position.Y;
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        // v = (0 − g·scale·dt) · 1/(1 + dt·drag); y += v·dt — same ops, same order.
        float dt = world.Config.Dt;
        float expectedVy = -9.81f * 1f * dt * (1f / (1f + dt * 1f));
        Assert.Equal(expectedVy, player.Velocity.Y);
        Assert.Equal(y0 + expectedVy * dt, player.Position.Y);
    }

    [Fact]
    public void FallingPlayerLandsOnPlatformAndBecomesGrounded()
    {
        SimWorld world = FloatingWorld(out SimPlayer player, y: 2f);
        Span<InputFrame> neutral = stackalloc[] { InputFrame.Neutral, InputFrame.Neutral };
        for (int i = 0; i < 300 && !player.IsGrounded; i++)
        {
            world.Tick(neutral);
        }
        Assert.True(player.IsGrounded);
        Assert.Equal(PlayerState.Idle, player.State);
        Assert.Equal(0f, player.Velocity.Y);
        // Floor top is y = −2; feet rest on it (within resolution skin).
        Assert.Equal(-2f + player.BodyHalf.Y, player.Position.Y, 0.01f);
    }

    [Fact]
    public void ExtremeKnockbackVelocityDoesNotTunnelThroughThePlatform()
    {
        SimWorld world = FloatingWorld(out SimPlayer player, y: 3f);
        player.Velocity = new Vec2(0f, -400f); // ~6.7 units per tick — tunnels without substeps
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        Assert.True(player.Position.Y > -2.6f, $"player fell through the floor to y={player.Position.Y}");
    }

    [Fact]
    public void HorizontalMotionIsBlockedByPlatformSides()
    {
        // Wall platform to the right of the player, above the floor.
        var genome = TestGames.FlatArena();
        var stage = new BrawlerSim.Genome.StageGenome(new[]
        {
            new BrawlerSim.Genome.PlatformGene(-8, -3, 16, 1),
            new BrawlerSim.Genome.PlatformGene(2, -2, 2, 4),
        });
        var world = new SimWorld(new BrawlerSim.Genome.GameGenome(genome.Characters, stage));
        SimPlayer player = world.Players[0];
        player.Position = new Vec2(0f, -1.4f);
        world.Players[1].Position = new Vec2(6f, -1.4f);

        Span<InputFrame> right = stackalloc[] { new InputFrame(1f, false, false), InputFrame.Neutral };
        for (int i = 0; i < 120; i++)
        {
            world.Tick(right);
        }
        Assert.True(player.Body.Right <= 2.01f, $"player pushed into the wall: right edge {player.Body.Right}");
        Assert.Equal(0f, player.Velocity.X);
    }

    [Fact]
    public void OverlappingPlayersPushApartByMass()
    {
        var world = new SimWorld(TestGames.FlatArena());
        SimPlayer a = world.Players[0];
        SimPlayer b = world.Players[1];
        a.Position = new Vec2(-0.1f, 5f);
        b.Position = new Vec2(0.1f, 5f);
        a.Velocity = Vec2.Zero;
        b.Velocity = Vec2.Zero;

        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });

        Assert.True(a.Position.X < b.Position.X);
        Assert.False(a.Body.Overlaps(b.Body));
    }
}
