using BrawlerSim.Hyperspace;

namespace BrawlerSim.Tests.Hyperspace;

/// <summary>Flight, targeting-range and fade-band math (galaxy-view.md §4/§6/§8.1).</summary>
public class GalaxyNavigationTests
{
    [Fact]
    public void SteeringHasADeadZoneAndAQuadraticRamp()
    {
        Assert.Equal(0f, GalaxyNavigation.SteerRate(0f));
        Assert.Equal(0f, GalaxyNavigation.SteerRate(GalaxyNavigation.SteerDeadZone));
        Assert.Equal(0f, GalaxyNavigation.SteerRate(-GalaxyNavigation.SteerDeadZone));

        // Full deflection is the max rate, and the sign follows the cursor.
        Assert.Equal(GalaxyNavigation.MaxTurnRate, GalaxyNavigation.SteerRate(1f), 0.001f);
        Assert.Equal(-GalaxyNavigation.MaxTurnRate, GalaxyNavigation.SteerRate(-1f), 0.001f);

        // Quadratic: half deflection past the dead zone turns a QUARTER as fast.
        float half = GalaxyNavigation.SteerDeadZone + (1f - GalaxyNavigation.SteerDeadZone) * 0.5f;
        Assert.Equal(GalaxyNavigation.MaxTurnRate * 0.25f, GalaxyNavigation.SteerRate(half), 0.001f);
    }

    [Fact]
    public void DampingIsFrameRateIndependent()
    {
        // Two 1/120 s steps must decay exactly like one 1/60 s step.
        float coarse = GalaxyNavigation.DampingFactor(1f / 60f, braking: false);
        float fine = GalaxyNavigation.DampingFactor(1f / 120f, braking: false);
        Assert.Equal(coarse, fine * fine, 0.0001f);
        Assert.True(GalaxyNavigation.DampingFactor(0.1f, braking: true)
            < GalaxyNavigation.DampingFactor(0.1f, braking: false));
    }

    [Fact]
    public void WarpDurationEasesAndCaps()
    {
        Assert.Equal(GalaxyNavigation.WarpBaseSeconds, GalaxyNavigation.WarpDuration(0f), 0.001f);
        Assert.True(GalaxyNavigation.WarpDuration(1000f) > GalaxyNavigation.WarpDuration(100f));
        // A galaxy-to-galaxy hop is far past the cap.
        Assert.Equal(GalaxyNavigation.WarpMaxDurationSeconds,
            GalaxyNavigation.WarpDuration(GalaxyLayout.Gap), 0.001f);

        Assert.Equal(0f, GalaxyNavigation.WarpEase(0f), 0.001f);
        Assert.Equal(0.5f, GalaxyNavigation.WarpEase(0.5f), 0.001f);
        Assert.Equal(1f, GalaxyNavigation.WarpEase(1f), 0.001f);
        // Ease-in: the first tenth covers less ground than the linear share.
        Assert.True(GalaxyNavigation.WarpEase(0.1f) < 0.1f);
        Assert.True(GalaxyNavigation.WarpEase(0.9f) > 0.9f);
    }

    [Fact]
    public void NearestGalaxyIsDerivedFromPositionWithHysteresis()
    {
        Assert.Equal(0, GalaxyNavigation.NearestGalaxy(GalaxyLayout.GalaxyCenter(0).X));
        Assert.Equal(7, GalaxyNavigation.NearestGalaxy(GalaxyLayout.GalaxyCenter(7).X));
        Assert.Equal(4, GalaxyNavigation.NearestGalaxy(GalaxyLayout.GalaxyCenter(4).X));

        // Past the ends of the lane it clamps rather than running off the archive.
        Assert.Equal(0, GalaxyNavigation.NearestGalaxy(-99f * GalaxyLayout.Gap));
        Assert.Equal(7, GalaxyNavigation.NearestGalaxy(99f * GalaxyLayout.Gap));

        // Hovering at the midpoint must NOT flicker the dashboard (§8.1.6): sitting
        // just past halfway keeps the previous galaxy until you are 10% closer.
        float midpoint = (GalaxyLayout.GalaxyCenter(3).X + GalaxyLayout.GalaxyCenter(4).X) / 2f;
        float justPast = midpoint + 1f;
        Assert.Equal(4, GalaxyNavigation.NearestGalaxy(justPast));          // no history
        Assert.Equal(3, GalaxyNavigation.NearestGalaxy(justPast, previous: 3)); // held
        // Committed well into galaxy 4, it switches.
        Assert.Equal(4, GalaxyNavigation.NearestGalaxy(
            GalaxyLayout.GalaxyCenter(4).X - GalaxyLayout.Half, previous: 3));
    }

    [Fact]
    public void SectorBinningCoversTheGalaxyAndReportsOutside()
    {
        Assert.Equal(0, GalaxyNavigation.SectorOf(-GalaxyLayout.Half + 1f));
        Assert.Equal(7, GalaxyNavigation.SectorOf(GalaxyLayout.Half - 1f));
        Assert.Equal(4, GalaxyNavigation.SectorOf(1f));
        Assert.Equal(-1, GalaxyNavigation.SectorOf(-GalaxyLayout.Half - 1f)); // outside
        Assert.Equal(-1, GalaxyNavigation.SectorOf(GalaxyLayout.Half + 1f));
    }

    [Fact]
    public void FadeBandsRampInsteadOfSwitching()
    {
        // Fading OUT with distance (planets, orbit rings).
        float near = 0.62f * GalaxyLayout.PlanetRange, far = GalaxyLayout.PlanetRange;
        Assert.Equal(1f, GalaxyNavigation.FadeBand(near - 10f, near, far), 0.001f);
        Assert.Equal(0f, GalaxyNavigation.FadeBand(far + 10f, near, far), 0.001f);
        float mid = GalaxyNavigation.FadeBand((near + far) / 2f, near, far);
        Assert.InRange(mid, 0.4f, 0.6f);

        // Fading IN with distance (the galaxy halo ramping in as you leave): the
        // same call with the roles swapped, so the halo never blinks on at the
        // boundary (§8.1).
        float fullAt = 1.4f * GalaxyLayout.NearGalaxy, zeroAt = 0.8f * GalaxyLayout.NearGalaxy;
        Assert.Equal(0f, GalaxyNavigation.FadeBand(zeroAt, fullAt, zeroAt), 0.001f);
        Assert.Equal(1f, GalaxyNavigation.FadeBand(fullAt, fullAt, zeroAt), 0.001f);
        Assert.Equal(1f, GalaxyNavigation.FadeBand(fullAt * 3f, fullAt, zeroAt), 0.001f);
        Assert.InRange(GalaxyNavigation.FadeBand((fullAt + zeroAt) / 2f, fullAt, zeroAt), 0.4f, 0.6f);

        // Monotone across the whole band — no step anywhere, which is the rule.
        float previous = 1f;
        for (int step = 0; step <= 100; step++)
        {
            float d = near + (far - near) * (step / 100f);
            float alpha = GalaxyNavigation.FadeBand(d, near, far);
            Assert.True(alpha <= previous + 1e-4f, $"fade band rose at {d}");
            previous = alpha;
        }
    }

    [Fact]
    public void WarpStandsOffPastTheSystemItArrivesAt()
    {
        // Arriving inside the envelope puts the camera in the star's corona with the
        // system filling the screen — you came to look at the system, not its surface.
        Assert.True(GalaxyNavigation.StarStandoff > GalaxyLayout.MaxSystemRadius);
        Assert.True(GalaxyNavigation.PlanetStandoff > GalaxyLayout.MaxPlanetRadius * 4f);
        // ...but still well inside the cell, so the neighbours are not what you frame.
        Assert.True(GalaxyNavigation.StarStandoff < GalaxyLayout.S / 2f);
    }

    [Fact]
    public void TargetingRangeExceedsTheVisibleGalaxy()
    {
        // Picking must reach across a galaxy; it has no visual edge of its own.
        Assert.True(GalaxyNavigation.TargetingRange > GalaxyLayout.Extent);
    }
}
