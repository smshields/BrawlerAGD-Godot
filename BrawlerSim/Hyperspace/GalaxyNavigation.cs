namespace BrawlerSim.Hyperspace;

/// <summary>
/// The galaxy view's flight and navigation math (2026-09-16,
/// docs/features/galaxy-view.md §4/§6). Pure, frame-rate independent, engine-free —
/// the Godot layer supplies dt and input, this decides what happens.
///
/// SPEEDS ARE DOUBLED alongside <see cref="GalaxyLayout.S"/> (designer, 2026-09-16)
/// so that every traversal TIME matches the PoC even though the world is twice as
/// big: a galaxy crossing is 704 / 52 = 13.5 s at cruise and 2.9 s at boost, and an
/// inter-galactic hop is 3520 / 240 = 14.7 s. Traversal weight is a design goal, not
/// a rough edge — raising these without re-checking those times makes the galaxies
/// stop feeling far apart.
/// </summary>
public static class GalaxyNavigation
{
    public const float Cruise = 52f;
    public const float Boost = 240f;

    /// <summary>Acceleration = this x the current speed cap, along the input direction.</summary>
    public const float AccelerationFactor = 3f;

    /// <summary>Velocity retained per second (applied as pow(damping, dt)).</summary>
    public const float Damping = 0.14f;
    public const float BrakeDamping = 0.005f;

    public const float SteerDeadZone = 0.08f;
    public const float MaxTurnRate = 1.5f;   // rad/s
    public const float MaxPitch = 1.45f;     // rad

    /// <summary>Tracking gain while locked: min(1, this x dt) per frame.</summary>
    public const float TrackingGain = 4.5f;

    public const float WarpMaxDurationSeconds = 3.2f;
    public const float WarpBaseSeconds = 0.5f;

    /// <summary>Seconds of warp per world unit. HALVED from the PoC's 1.6 ms/unit
    /// alongside the doubled world, by the same rule as the speeds: an equivalent
    /// journey takes the time it always did.</summary>
    public const float WarpSecondsPerUnit = 0.0008f;

    /// <summary>
    /// How far short of a STAR a warp stops. The spec's flat 6 units was sized for
    /// the un-doubled world and for the PoC's smaller glow sprite; at our scale it
    /// parks the camera inside the star's corona with the system filling the screen.
    /// Standing off past the system envelope frames what you actually came to see —
    /// the star AND its planets.
    /// </summary>
    public static float StarStandoff => GalaxyLayout.MaxSystemRadius * 1.8f;

    /// <summary>A planet is a small body: close enough to fill a reasonable part of
    /// the view, far enough that its orbit ring still reads.</summary>
    public const float PlanetStandoff = 9f;

    /// <summary>A galaxy warp arrives at its edge, not its centre.</summary>
    public const float GalaxyArrivalRadius = GalaxyLayout.Half + 40f;

    /// <summary>Picking reach — no visual edge, targeting only.</summary>
    public const float TargetingRange = 16f * GalaxyLayout.S;

    /// <summary>
    /// Steering response for one normalized cursor axis: dead zone, then a quadratic
    /// ramp to the max turn rate. Quadratic so small corrections are gentle and the
    /// edges of the viewport still turn hard.
    /// </summary>
    public static float SteerRate(float normalized)
    {
        float magnitude = MathF.Abs(normalized);
        if (magnitude <= SteerDeadZone)
        {
            return 0f;
        }
        float q = Math.Clamp((magnitude - SteerDeadZone) / (1f - SteerDeadZone), 0f, 1f);
        return MathF.Sign(normalized) * q * q * MaxTurnRate;
    }

    /// <summary>Per-frame velocity damping factor for a dt — pow, so the decay is
    /// identical at any frame rate.</summary>
    public static float DampingFactor(float dt, bool braking) =>
        MathF.Pow(braking ? BrakeDamping : Damping, dt);

    /// <summary>Warp duration for a distance, in seconds.</summary>
    public static float WarpDuration(float distance) =>
        MathF.Min(WarpMaxDurationSeconds, WarpBaseSeconds + MathF.Max(0f, distance) * WarpSecondsPerUnit);

    /// <summary>Ease-in-out-quad on normalized warp progress.</summary>
    public static float WarpEase(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
    }

    /// <summary>
    /// The galaxy the ship is nearest — a DERIVED readout (position bins, cube map,
    /// hyperdrive segments), never a render switch. Pass the previous value to get
    /// the 10% hysteresis that stops the dashboard flickering between two galaxies
    /// when a player hovers at the midpoint (§8.1.6).
    /// </summary>
    public static int NearestGalaxy(float shipX, int previous = -1)
    {
        int candidate = Math.Clamp((int)MathF.Round(shipX / GalaxyLayout.Gap + 3.5f), 0, GalaxyLayout.Bins - 1);
        if (previous < 0 || previous == candidate || previous >= GalaxyLayout.Bins)
        {
            return candidate;
        }
        float toCandidate = MathF.Abs(shipX - GalaxyLayout.GalaxyCenter(candidate).X);
        float toPrevious = MathF.Abs(shipX - GalaxyLayout.GalaxyCenter(previous).X);
        return toCandidate < 0.9f * toPrevious ? candidate : previous;
    }

    /// <summary>Bin index of a coordinate LOCAL to a galaxy centre, or −1 for
    /// "outside" — the PositionPod's per-axis readout.</summary>
    public static int SectorOf(float local)
    {
        int bin = (int)MathF.Floor((local + GalaxyLayout.Half) / GalaxyLayout.S);
        return bin < 0 || bin >= GalaxyLayout.Bins ? -1 : bin;
    }

    /// <summary>
    /// Smoothstep fade band — THE range-visibility primitive (§8.1). Every
    /// range-based visibility decision in the view multiplies one of these into
    /// alpha; a bare `if (distance &lt; X)` is a bug.
    ///
    /// Parameters are named by ROLE, not by order, so the same call works in both
    /// directions: planets fade OUT with distance (fullAt 0.62 x range, zeroAt
    /// range), while a galaxy halo ramps IN as you leave it (fullAt 1.4 x NEAR_G,
    /// zeroAt 0.8 x NEAR_G) — and neither ever blinks at a boundary.
    /// </summary>
    public static float FadeBand(float distance, float fullAt, float zeroAt)
    {
        if (MathF.Abs(zeroAt - fullAt) < 1e-6f)
        {
            return distance <= fullAt ? 1f : 0f;
        }
        float t = Math.Clamp((distance - fullAt) / (zeroAt - fullAt), 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }
}
