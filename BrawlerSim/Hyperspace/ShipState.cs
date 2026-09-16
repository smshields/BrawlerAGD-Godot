namespace BrawlerSim.Hyperspace;

/// <summary>One frame of pilot intent. Steer values are the cursor's offset from the
/// viewport centre normalized to its half extents; Forward/Strafe are −1..1.</summary>
public readonly record struct ShipInput(
    float SteerX, float SteerY, float Forward, float Strafe,
    bool Boost, bool Brake, bool SteeringEnabled);

/// <summary>
/// The ship's flight model (galaxy-view.md §4) — engine-free so the feel is pinned by
/// tests rather than by eye. Mouse steers, keys translate, and everything is dt-exact:
/// the same flight at 30 fps and 144 fps ends in the same place.
///
/// Speeds live in <see cref="GalaxyNavigation"/> and are doubled with the world so
/// traversal TIMES match the PoC. Steering is suspended (not zeroed) whenever the
/// cursor is over the dashboard, a target is locked, or a warp is running — the ship
/// keeps its heading rather than snapping.
/// </summary>
public sealed class ShipState
{
    public GalaxyPoint Position { get; set; }
    public GalaxyPoint Velocity { get; private set; }

    /// <summary>Yaw on the rig, pitch on its child, so pitch never rolls the horizon.</summary>
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>Godot looks down −Z, so yaw 0 / pitch 0 faces (0,0,−1).</summary>
    public GalaxyPoint Forward
    {
        get
        {
            float cp = MathF.Cos(Pitch);
            return new GalaxyPoint(-MathF.Sin(Yaw) * cp, MathF.Sin(Pitch), -MathF.Cos(Yaw) * cp);
        }
    }

    /// <summary>Strafe axis: horizontal, so sliding sideways never climbs.</summary>
    public GalaxyPoint Right => new(MathF.Cos(Yaw), 0f, -MathF.Sin(Yaw));

    public float Speed => Velocity.Length;

    public float SpeedCap(bool boost) => boost ? GalaxyNavigation.Boost : GalaxyNavigation.Cruise;

    public void Step(in ShipInput input, float dt)
    {
        if (dt <= 0f)
        {
            return;
        }
        if (input.SteeringEnabled)
        {
            // Cursor right turns right: +yaw swings toward −X in Godot's basis, so
            // the steer rate is SUBTRACTED (the PoC's +=, in its own handedness).
            Yaw -= GalaxyNavigation.SteerRate(input.SteerX) * dt;
            Pitch -= GalaxyNavigation.SteerRate(input.SteerY) * dt;
            Pitch = Math.Clamp(Pitch, -GalaxyNavigation.MaxPitch, GalaxyNavigation.MaxPitch);
        }

        float cap = SpeedCap(input.Boost);
        GalaxyPoint thrust = Forward * input.Forward + Right * input.Strafe;
        if (thrust.Length > 1e-4f)
        {
            Velocity += thrust.Normalized() * (GalaxyNavigation.AccelerationFactor * cap * dt);
        }

        Velocity *= GalaxyNavigation.DampingFactor(dt, input.Brake);
        if (Velocity.Length > cap)
        {
            Velocity = Velocity.Normalized() * cap;
        }
        Position += Velocity * dt;
    }

    /// <summary>Warp and other scripted motion own the position outright.</summary>
    public void Halt() => Velocity = new GalaxyPoint(0f, 0f, 0f);
}
