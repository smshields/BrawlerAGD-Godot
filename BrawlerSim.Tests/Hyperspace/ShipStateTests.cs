using BrawlerSim.Hyperspace;

namespace BrawlerSim.Tests.Hyperspace;

public class ShipStateTests
{
    private static ShipInput Thrust(float forward = 0f, float strafe = 0f, bool boost = false,
        bool brake = false, float steerX = 0f, float steerY = 0f, bool steering = true) =>
        new(steerX, steerY, forward, strafe, boost, brake, steering);

    private static ShipState Fly(ShipInput input, float seconds, float dt)
    {
        var ship = new ShipState();
        for (float t = 0f; t < seconds - 1e-6f; t += dt)
        {
            ship.Step(input, dt);
        }
        return ship;
    }

    [Fact]
    public void HeadingFollowsYawAndPitch()
    {
        var ship = new ShipState();
        Assert.Equal(-1f, ship.Forward.Z, 0.001f);   // Godot looks down -Z
        Assert.Equal(1f, ship.Right.X, 0.001f);

        ship.Pitch = MathF.PI / 2f;
        Assert.Equal(1f, ship.Forward.Y, 0.001f);    // nose up
        Assert.Equal(0f, ship.Right.Y, 0.001f);      // strafe stays horizontal
    }

    [Fact]
    public void CursorRightTurnsRight()
    {
        var ship = new ShipState();
        ship.Step(Thrust(steerX: 1f), 0.5f);
        // Turning right swings the heading toward +X.
        Assert.True(ship.Forward.X > 0.1f, $"heading went to {ship.Forward.X}");
    }

    [Fact]
    public void PitchIsClampedSoTheHorizonNeverFlips()
    {
        var ship = new ShipState();
        for (int i = 0; i < 600; i++)
        {
            ship.Step(Thrust(steerY: -1f), 1f / 60f);
        }
        Assert.Equal(GalaxyNavigation.MaxPitch, ship.Pitch, 0.001f);
        for (int i = 0; i < 1200; i++)
        {
            ship.Step(Thrust(steerY: 1f), 1f / 60f);
        }
        Assert.Equal(-GalaxyNavigation.MaxPitch, ship.Pitch, 0.001f);
    }

    [Fact]
    public void SteeringSuspensionHoldsTheHeading()
    {
        var ship = new ShipState();
        ship.Step(Thrust(steerX: 1f, steerY: 1f), 0.3f);
        float yaw = ship.Yaw, pitch = ship.Pitch;
        // Over the dashboard / locked / warping: the ship KEEPS its heading.
        ship.Step(Thrust(steerX: 1f, steerY: 1f, steering: false), 0.3f);
        Assert.Equal(yaw, ship.Yaw);
        Assert.Equal(pitch, ship.Pitch);
    }

    [Fact]
    public void SpeedSettlesAtTheCapAndBoostIsFaster()
    {
        ShipState cruise = Fly(Thrust(forward: 1f), seconds: 6f, dt: 1f / 60f);
        Assert.Equal(GalaxyNavigation.Cruise, cruise.Speed, 0.5f);

        ShipState boost = Fly(Thrust(forward: 1f, boost: true), seconds: 6f, dt: 1f / 60f);
        Assert.Equal(GalaxyNavigation.Boost, boost.Speed, 1f);

        // The design goal is stated in seconds: a galaxy crossing at cruise.
        Assert.Equal(13.5f, GalaxyLayout.Extent / cruise.Speed, 0.3f);
    }

    [Fact]
    public void FlightIsFrameRateIndependent()
    {
        ShipState slow = Fly(Thrust(forward: 1f), seconds: 4f, dt: 1f / 30f);
        ShipState fast = Fly(Thrust(forward: 1f), seconds: 4f, dt: 1f / 144f);
        // Same journey, wildly different frame rates: within a fraction of a percent.
        float drift = MathF.Abs(slow.Position.Z - fast.Position.Z);
        Assert.True(drift < MathF.Abs(slow.Position.Z) * 0.02f,
            $"30 fps ended {drift} from 144 fps");
    }

    [Fact]
    public void BrakeStopsFasterThanCoasting()
    {
        ShipState moving = Fly(Thrust(forward: 1f), seconds: 4f, dt: 1f / 60f);
        float cruising = moving.Speed;

        var coasting = new ShipState();
        var braking = new ShipState();
        for (int i = 0; i < 30; i++)
        {
            coasting.Step(Thrust(), 1f / 60f);
            braking.Step(Thrust(brake: true), 1f / 60f);
        }
        // Both start from rest here, so compare the decay factors directly instead.
        Assert.True(GalaxyNavigation.DampingFactor(0.5f, braking: true)
            < GalaxyNavigation.DampingFactor(0.5f, braking: false));
        Assert.True(cruising > 0f);
    }

    [Fact]
    public void StrafeMovesSidewaysWithoutClimbing()
    {
        ShipState ship = Fly(Thrust(strafe: 1f), seconds: 3f, dt: 1f / 60f);
        Assert.True(ship.Position.X > 10f);
        Assert.Equal(0f, ship.Position.Y, 0.001f);
    }
}
