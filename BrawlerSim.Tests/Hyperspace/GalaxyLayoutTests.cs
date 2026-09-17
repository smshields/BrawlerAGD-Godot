using BrawlerSim.Hyperspace;

namespace BrawlerSim.Tests.Hyperspace;

/// <summary>
/// Golden values and invariants for the galaxy view's layout math
/// (docs/features/galaxy-view.md §3). The separation invariant test is the important
/// one: it is what makes a future change to S, JITTER, the envelope cap, or the
/// radius formulas fail loudly instead of quietly letting neighbouring systems
/// interleave their orbits.
/// </summary>
public class GalaxyLayoutTests
{
    [Fact]
    public void ConstantsAreTheDoubledWorld()
    {
        // Designer 2026-09-16: the PoC's S=44 world doubled, speeds doubled with it.
        Assert.Equal(88f, GalaxyLayout.S);
        Assert.Equal(704f, GalaxyLayout.Extent);
        Assert.Equal(352f, GalaxyLayout.Half);
        Assert.Equal(3520f, GalaxyLayout.Gap);
        Assert.Equal(704f, GalaxyLayout.NearGalaxy);
        Assert.Equal(616f, GalaxyLayout.PlanetRange);
        Assert.Equal(968f, GalaxyLayout.FadeNumerator);
    }

    [Fact]
    public void TraversalTimesMatchThePocDespiteTheDoubledWorld()
    {
        // The whole point of doubling the speeds with the world: these three numbers
        // are the design goal (§4), and they are stated in SECONDS.
        Assert.Equal(13.5f, GalaxyLayout.Extent / GalaxyNavigation.Cruise, 0.1f);
        Assert.Equal(2.9f, GalaxyLayout.Extent / GalaxyNavigation.Boost, 0.1f);
        Assert.Equal(14.7f, GalaxyLayout.Gap / GalaxyNavigation.Boost, 0.1f);
    }

    /// <summary>
    /// THE containment invariant (designer, 2026-09-17): a system — star, orbits,
    /// planet bodies — stays entirely inside its OWN cell, at every placement and
    /// every planet count. Containment implies neighbours can never intersect, which
    /// is what freed placement to roam the whole bucket. The worst case is asserted,
    /// not the typical one: max-fitness star, max-fitness planets.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SystemsNeverLeaveTheirOwnCell(int planets)
    {
        float starRadius = GalaxyLayout.StarRadius(1f);          // worst case: max fitness
        var fitness = new float[planets];
        System.Array.Fill(fitness, 1f);                          // worst case: max planets

        for (int i = 0; i < GalaxyLayout.Bins; i++)
        {
            for (int j = 0; j < GalaxyLayout.Bins; j++)
            {
                for (int k = 0; k < GalaxyLayout.Bins; k++)
                {
                    ulong hash = GalaxyLayout.CellHash(i, j, k, 3);
                    float radius = GalaxyLayout.SystemRadius(hash, planets, starRadius, fitness);
                    Assert.True(radius <= GalaxyLayout.MaxSystemRadius,
                        $"system envelope {radius} exceeded the cap {GalaxyLayout.MaxSystemRadius}");

                    GalaxyPoint star = GalaxyLayout.StarPosition(i, j, k, 3);
                    GalaxyPoint center = GalaxyLayout.GalaxyCenter(3);
                    float[] offsets =
                    {
                        star.X - center.X - (i - 3.5f) * GalaxyLayout.S,
                        star.Y - center.Y - (j - 3.5f) * GalaxyLayout.S,
                        star.Z - center.Z - (k - 3.5f) * GalaxyLayout.S,
                    };
                    foreach (float offset in offsets)
                    {
                        Assert.True(
                            System.MathF.Abs(offset) + radius <= GalaxyLayout.S / 2f + 1e-3f,
                            $"{planets} planets: a system at offset {offset} with envelope "
                            + $"{radius} crosses its cell wall (half-cell {GalaxyLayout.S / 2f})");
                    }
                }
            }
        }
    }

    /// <summary>Placement must actually USE the bucket (designer: "randomly placed
    /// inside"), not hug the centre the way the old triangular jitter did.</summary>
    [Fact]
    public void PlacementSpreadsAcrossTheWholeCell()
    {
        float range = GalaxyLayout.S / 2f - GalaxyLayout.PlacementInset;
        float maxOffset = 0f;
        int farHalf = 0, total = 0;
        for (int i = 0; i < GalaxyLayout.Bins; i++)
        {
            for (int j = 0; j < GalaxyLayout.Bins; j++)
            {
                for (int k = 0; k < GalaxyLayout.Bins; k++)
                {
                    GalaxyPoint star = GalaxyLayout.StarPosition(i, j, k, 5);
                    GalaxyPoint center = GalaxyLayout.GalaxyCenter(5);
                    float offset = System.MathF.Abs(
                        star.X - center.X - (i - 3.5f) * GalaxyLayout.S);
                    maxOffset = System.MathF.Max(maxOffset, offset);
                    if (offset > range / 2f)
                    {
                        farHalf++;
                    }
                    total++;
                }
            }
        }
        Assert.True(maxOffset > 0.9f * range,
            $"nothing placed near the wall region: max offset {maxOffset} of {range}");
        // Uniform placement puts about half the stars in the outer half of the range.
        Assert.True(farHalf > total / 3, $"only {farHalf}/{total} stars left the centre");
    }

    [Fact]
    public void StarPlacementIsDeterministicAndCellDistinct()
    {
        GalaxyPoint a = GalaxyLayout.StarPosition(3, 4, 5, 1);
        Assert.Equal(a, GalaxyLayout.StarPosition(3, 4, 5, 1));       // stable across calls
        Assert.NotEqual(a, GalaxyLayout.StarPosition(3, 4, 5, 2));    // and per galaxy
        Assert.NotEqual(a, GalaxyLayout.StarPosition(3, 4, 6, 1));    // and per cell

        // Galaxy 0 sits 3.5 gaps below the origin; galaxy 7, 3.5 above.
        Assert.Equal(-3.5f * GalaxyLayout.Gap, GalaxyLayout.GalaxyCenter(0).X);
        Assert.Equal(3.5f * GalaxyLayout.Gap, GalaxyLayout.GalaxyCenter(7).X);
    }

    [Fact]
    public void PlanetsAreAlwaysSmallerThanTheirStar()
    {
        // The elite is its cell's maximum by construction, so a member's fitness
        // never exceeds the star's — a planet must never out-size its own star.
        for (int f = 0; f <= 10; f++)
        {
            float fitness = f / 10f;
            Assert.True(GalaxyLayout.PlanetRadius(fitness) < GalaxyLayout.StarRadius(fitness));
        }
        // Size carries fitness (designer 2026-09-16), so it must actually vary.
        Assert.True(GalaxyLayout.PlanetRadius(1f) > 3f * GalaxyLayout.PlanetRadius(0f));
    }

    [Fact]
    public void OrbitsVaryInOrientationAndEccentricity()
    {
        ulong hash = GalaxyLayout.CellHash(2, 3, 4, 5);
        const int count = 8;
        var normals = new List<GalaxyPoint>();
        float minEccentricity = 1f, maxEccentricity = 0f;
        for (int n = 0; n < count; n++)
        {
            OrbitElements orbit = GalaxyLayout.Orbit(hash, n, count, GalaxyLayout.StarRadius(0.8f), 0.5f);
            normals.Add(GalaxyPoint.Cross(orbit.PlaneU, orbit.PlaneV).Normalized());
            minEccentricity = MathF.Min(minEccentricity, orbit.Eccentricity);
            maxEccentricity = MathF.Max(maxEccentricity, orbit.Eccentricity);
            Assert.InRange(orbit.Eccentricity, 0f, GalaxyLayout.MaxEccentricity);

            // Plane basis is orthonormal — otherwise the ellipse shears.
            Assert.Equal(1f, orbit.PlaneU.Length, 0.001f);
            Assert.Equal(1f, orbit.PlaneV.Length, 0.001f);
            Assert.Equal(0f,
                orbit.PlaneU.X * orbit.PlaneV.X + orbit.PlaneU.Y * orbit.PlaneV.Y
                + orbit.PlaneU.Z * orbit.PlaneV.Z, 0.001f);
        }

        // Orientation is what separates neighbouring orbits now that radial spacing
        // is packed, so the planes must genuinely differ (§Geometry).
        float closest = 1f;
        for (int a = 0; a < normals.Count; a++)
        {
            for (int b = a + 1; b < normals.Count; b++)
            {
                float dot = MathF.Abs(normals[a].X * normals[b].X + normals[a].Y * normals[b].Y
                    + normals[a].Z * normals[b].Z);
                closest = MathF.Min(closest, 1f - dot);
            }
        }
        Assert.True(closest > 0.01f, $"two orbital planes were nearly coincident ({closest})");
        Assert.True(maxEccentricity - minEccentricity > 0.1f, "orbits were all the same shape");
    }

    [Fact]
    public void PlanetMotionIsKeplerianAndStaysOnItsEllipse()
    {
        ulong hash = GalaxyLayout.CellHash(1, 1, 1, 0);
        var star = new GalaxyPoint(100f, -20f, 40f);
        OrbitElements orbit = GalaxyLayout.Orbit(hash, 0, 3, GalaxyLayout.StarRadius(1f), 1f);

        float minRadius = float.MaxValue, maxRadius = 0f;
        GalaxyPoint previous = GalaxyLayout.PlanetPosition(star, orbit, 0f);
        for (int step = 1; step <= 400; step++)
        {
            float t = step * 0.1f;
            GalaxyPoint p = GalaxyLayout.PlanetPosition(star, orbit, t);
            float radius = (p + star * -1f).Length;
            minRadius = MathF.Min(minRadius, radius);
            maxRadius = MathF.Max(maxRadius, radius);
            previous = p;
        }

        // Focus at the star: the orbit sweeps between periapsis and apoapsis.
        Assert.Equal(orbit.Periapsis, minRadius, 0.1f);
        Assert.Equal(orbit.Apoapsis, maxRadius, 0.1f);
        Assert.True(maxRadius - minRadius > 0.01f, "orbit degenerated to a circle");

        // Deterministic: same (orbit, t) is the same point, always.
        Assert.Equal(previous, GalaxyLayout.PlanetPosition(star, orbit, 40f));
    }

    [Fact]
    public void StarColourEncodesBinsAndFitness()
    {
        // R = move variety, G = character asymmetry, B = platform ratio.
        GalaxyColor low = GalaxyLayout.StarColor(0, 0, 0, 0f);
        GalaxyColor high = GalaxyLayout.StarColor(7, 7, 7, 1f);
        Assert.True(Luminance(high) > Luminance(low), "fitness must lift luminosity");

        GalaxyColor red = GalaxyLayout.StarColor(7, 0, 0, 1f);
        GalaxyColor green = GalaxyLayout.StarColor(0, 7, 0, 1f);
        GalaxyColor blue = GalaxyLayout.StarColor(0, 0, 7, 1f);
        Assert.True(red.R > red.G && red.R > red.B);
        Assert.True(green.G > green.R && green.G > green.B);
        Assert.True(blue.B > blue.R && blue.B > blue.G);

    }

    /// <summary>Planet bodies are a habitability ramp (designer, 2026-09-17): red at
    /// the hostile end, blue-green at the habitable one, by normalized fitness.</summary>
    [Fact]
    public void PlanetColourRunsRedToBlueGreen()
    {
        GalaxyColor hostile = GalaxyLayout.PlanetColor(0f);
        Assert.True(hostile.R > hostile.G && hostile.R > hostile.B,
            $"low fitness should be red, was ({hostile.R:F2},{hostile.G:F2},{hostile.B:F2})");

        GalaxyColor habitable = GalaxyLayout.PlanetColor(1f);
        Assert.True(habitable.G > habitable.R && habitable.B > habitable.R,
            $"high fitness should be blue-green, was ({habitable.R:F2},{habitable.G:F2},{habitable.B:F2})");

        // The ramp moves monotonically away from red, so mid values stay readable.
        float previous = float.MaxValue;
        for (int step = 0; step <= 10; step++)
        {
            GalaxyColor c = GalaxyLayout.PlanetColor(step / 10f);
            float redness = c.R - (c.G + c.B) / 2f;
            Assert.True(redness <= previous + 1e-3f, $"redness rose again at {step / 10f}");
            previous = redness;
        }
        // Habitable worlds glow a little brighter — size-limited dots keep the read.
        Assert.True(Luminance(habitable) > Luminance(hostile));
    }

    [Fact]
    public void StarFadeNeverReachesZero()
    {
        // §8.1: stars have no far cull — their fade floors above zero, so a star at
        // the far end of the lane is dim, never absent.
        Assert.True(GalaxyLayout.StarFade(7f * GalaxyLayout.Gap, 1f) > 0f);
        Assert.True(GalaxyLayout.StarFade(float.MaxValue, 0f) > 0f);
        Assert.True(GalaxyLayout.StarFade(10f, 1f) > GalaxyLayout.StarFade(10_000f, 1f));
    }

    private static float Luminance(GalaxyColor c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
}
