using BrawlerSim.Hyperspace;

namespace BrawlerSim.Tests.Hyperspace;

/// <summary>
/// The spherical/radial grid experiment (galaxy-view.md §Radial experiment). The
/// containment test is the load-bearing one: a wedge's walls are spheres, half-planes
/// and cones, and the placement insets must clear all six for the worst-case system.
/// </summary>
public class RadialGalaxyLayoutTests
{
    private static (float R, float Azimuth, float Elevation) Spherical(GalaxyPoint local)
    {
        float r = local.Length;
        float elevation = MathF.Asin(Math.Clamp(local.Y / MathF.Max(r, 1e-6f), -1f, 1f));
        float azimuth = MathF.Atan2(local.Z, local.X);
        if (azimuth < 0f)
        {
            azimuth += MathF.Tau;
        }
        return (r, azimuth, elevation);
    }

    /// <summary>
    /// THE containment invariant, radial edition: at every cell, the star's distance
    /// to all six wedge walls clears the worst-case system envelope — |r−wall| for
    /// the sphere walls, rho*sin(delta) for the azimuth half-planes, r*sin(delta)
    /// for the elevation cones (all exact distances). The envelope itself is already
    /// pinned against planet count 0-32 by GalaxyLayoutTests; here the wedge is what
    /// is under test.
    /// </summary>
    [Fact]
    public void SystemsNeverLeaveTheirWedge()
    {
        float envelope = GalaxyLayout.MaxSystemRadius; // what must fit; inset adds margin
        for (int i = 0; i < RadialGalaxyLayout.Bins; i++)
        {
            for (int j = 0; j < RadialGalaxyLayout.Bins; j++)
            {
                for (int k = 0; k < RadialGalaxyLayout.Bins; k++)
                {
                    GalaxyPoint local = RadialGalaxyLayout.StarPosition(i, j, k, 2)
                        + RadialGalaxyLayout.GalaxyCenter(2) * -1f;
                    (float r, float azimuth, float elevation) = Spherical(local);
                    float rho = r * MathF.Cos(elevation);

                    float rLo = RadialGalaxyLayout.CoreRadius + i * RadialGalaxyLayout.ShellThickness;
                    float azLo = j * RadialGalaxyLayout.AzimuthCell;
                    float elLo = -RadialGalaxyLayout.ElevationSpan / 2f + k * RadialGalaxyLayout.ElevationCell;

                    string cell = $"cell {i}-{j}-{k}";
                    Assert.True(r - rLo >= envelope, $"{cell}: inner sphere {r - rLo}");
                    Assert.True(rLo + RadialGalaxyLayout.ShellThickness - r >= envelope,
                        $"{cell}: outer sphere");
                    Assert.True(rho * MathF.Sin(azimuth - azLo) >= envelope,
                        $"{cell}: low azimuth wall {rho * MathF.Sin(azimuth - azLo)}");
                    Assert.True(rho * MathF.Sin(azLo + RadialGalaxyLayout.AzimuthCell - azimuth) >= envelope,
                        $"{cell}: high azimuth wall");
                    Assert.True(r * MathF.Sin(elevation - elLo) >= envelope,
                        $"{cell}: low elevation cone");
                    Assert.True(r * MathF.Sin(elLo + RadialGalaxyLayout.ElevationCell - elevation) >= envelope,
                        $"{cell}: high elevation cone");
                }
            }
        }
    }

    [Fact]
    public void SectorOfInvertsStarPlacement()
    {
        for (int i = 0; i < RadialGalaxyLayout.Bins; i++)
        {
            for (int j = 0; j < RadialGalaxyLayout.Bins; j++)
            {
                for (int k = 0; k < RadialGalaxyLayout.Bins; k++)
                {
                    GalaxyPoint local = RadialGalaxyLayout.StarPosition(i, j, k, 5)
                        + RadialGalaxyLayout.GalaxyCenter(5) * -1f;
                    Assert.Equal((i, j, k), RadialGalaxyLayout.SectorOf(local));
                }
            }
        }
        // The hollow core and the seam read as OUTSIDE, never as a bucket.
        Assert.Null(RadialGalaxyLayout.SectorOf(new GalaxyPoint(50f, 0f, 0f)));
        float seamMid = RadialGalaxyLayout.AzimuthSpan
            + (MathF.Tau - RadialGalaxyLayout.AzimuthSpan) / 2f;
        Assert.Null(RadialGalaxyLayout.SectorOf(new GalaxyPoint(
            300f * MathF.Cos(seamMid), 0f, 300f * MathF.Sin(seamMid))));
    }

    /// <summary>The azimuth descriptor is NOT periodic: bins 0 and 7 must not touch.
    /// The seam keeps them further apart than one whole cell.</summary>
    [Fact]
    public void AzimuthSeamKeepsTheEndsApart()
    {
        float seam = MathF.Tau - RadialGalaxyLayout.AzimuthSpan;
        Assert.True(seam > RadialGalaxyLayout.AzimuthCell,
            $"seam {seam} rad is narrower than a cell {RadialGalaxyLayout.AzimuthCell}");
    }

    [Fact]
    public void GalaxiesScatterInThreeDimensionsWellSeparated()
    {
        float minSeparation = float.MaxValue;
        bool offLane = false;
        for (int a = 0; a < RadialGalaxyLayout.Bins; a++)
        {
            GalaxyPoint pa = RadialGalaxyLayout.GalaxyCenter(a);
            if (MathF.Abs(pa.Y) > 100f || MathF.Abs(pa.Z) > 100f)
            {
                offLane = true; // genuinely 3D, not a disguised lane
            }
            for (int b = a + 1; b < RadialGalaxyLayout.Bins; b++)
            {
                minSeparation = MathF.Min(minSeparation,
                    (pa + RadialGalaxyLayout.GalaxyCenter(b) * -1f).Length);
            }
        }
        // Two balls plus real void between them.
        float needed = 2f * (RadialGalaxyLayout.OuterRadius + GalaxyLayout.MaxSystemRadius) + 300f;
        Assert.True(minSeparation >= needed,
            $"nearest galaxies are {minSeparation} apart, below {needed}");
        Assert.True(offLane);
    }

    [Fact]
    public void PlacementIsDeterministicAndUsesTheWedge()
    {
        GalaxyPoint a = RadialGalaxyLayout.StarPosition(3, 4, 5, 1);
        Assert.Equal(a, RadialGalaxyLayout.StarPosition(3, 4, 5, 1));
        Assert.NotEqual(a, RadialGalaxyLayout.StarPosition(3, 4, 5, 2));

        // Radii spread across shells rather than pinning to shell centres.
        float minFraction = 1f, maxFraction = 0f;
        for (int j = 0; j < RadialGalaxyLayout.Bins; j++)
        {
            for (int k = 0; k < RadialGalaxyLayout.Bins; k++)
            {
                GalaxyPoint local = RadialGalaxyLayout.StarPosition(4, j, k, 0)
                    + RadialGalaxyLayout.GalaxyCenter(0) * -1f;
                float r = local.Length;
                float fraction = (r - RadialGalaxyLayout.CoreRadius
                    - 4 * RadialGalaxyLayout.ShellThickness) / RadialGalaxyLayout.ShellThickness;
                minFraction = MathF.Min(minFraction, fraction);
                maxFraction = MathF.Max(maxFraction, fraction);
            }
        }
        Assert.True(maxFraction - minFraction > 0.2f, "radial placement collapsed to a point");
    }

    /// <summary>The cube adapter must never drift from the GalaxyLayout statics —
    /// the seam exists so the experiment cannot disturb the shipping grid.</summary>
    [Fact]
    public void CubeGeometryMatchesTheStatics()
    {
        var cube = new CubeGalaxyGeometry();
        Assert.Equal(GalaxyLayout.Half, cube.GalaxyRadius);
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            Assert.Equal(GalaxyLayout.GalaxyCenter(g), cube.GalaxyCenter(g));
        }
        for (int i = 0; i < GalaxyLayout.Bins; i += 3)
        {
            for (int j = 0; j < GalaxyLayout.Bins; j += 3)
            {
                for (int k = 0; k < GalaxyLayout.Bins; k += 3)
                {
                    Assert.Equal(GalaxyLayout.StarPosition(i, j, k, 3),
                        cube.StarPosition(i, j, k, 3));
                    GalaxyPoint local = GalaxyLayout.StarPosition(i, j, k, 3)
                        + GalaxyLayout.GalaxyCenter(3) * -1f;
                    Assert.Equal((i, j, k), cube.SectorOf(local));
                }
            }
        }
        Assert.Equal(12, cube.CellWireframe(0, 0, 0).Count);
    }
}
