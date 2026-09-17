using BrawlerSim.Determinism;

namespace BrawlerSim.Hyperspace;

/// <summary>
/// The SPHERICAL/RADIAL grid experiment (2026-09-17, designer: "plot on a spherical
/// grid instead of a cube... galaxies in 3 dimensional space instead of a linear
/// path"). Each galaxy is a BALL of spherical-shell wedge cells; the eight galaxies
/// scatter through 3D space on golden-spiral directions rather than a lane.
///
/// Axis mapping keeps the cube order: i (move variety) = RADIUS shell, j (character
/// asymmetry) = AZIMUTH sector, k (platform ratio) = ELEVATION band, g (timing) =
/// which galaxy. Two honest distortions of this projection, both deliberate:
///
/// - AZIMUTH IS NOT A CIRCLE. The descriptor is not periodic, so bins 0 and 7 must
///   not touch — the sectors span 300 degrees and a 60 degree SEAM keeps the ends
///   apart (wider than one cell, pinned by test).
/// - ELEVATION stops at +/-70 degrees: at the poles, azimuth cells pinch to nothing
///   and could not contain a system.
///
/// THE CONTAINMENT INVARIANT carries over exactly: a system (star, orbits, planet
/// bodies) stays inside its own wedge. A wedge's walls are two SPHERES (radius), two
/// HALF-PLANES through the polar axis (azimuth) and two CONES (elevation); the
/// placement insets are computed against each — |r-wall| for spheres, rho*sin(delta)
/// for planes (rho = distance from the polar axis), r*sin(delta) for cones, all
/// exact — so the worst-case envelope cannot reach any of the six. Pinned by
/// RadialGalaxyLayoutTests across the whole grid and 0-32 planets.
/// </summary>
public static class RadialGalaxyLayout
{
    public const int Bins = GalaxyLayout.Bins;

    /// <summary>Inner radius of shell 0. Below this the ball is empty — the wedge
    /// arcs would pinch too tight to hold a system (a "galactic core" the view may
    /// decorate but never populates).</summary>
    public const float CoreRadius = 200f;

    /// <summary>Radial thickness of one shell.</summary>
    public const float ShellThickness = 52f;

    /// <summary>Outer radius of the ball: shell 7's far wall.</summary>
    public const float OuterRadius = CoreRadius + Bins * ShellThickness; // 616

    /// <summary>Azimuth span in radians (300 degrees; the other 60 are the seam).</summary>
    public const float AzimuthSpan = 300f / 180f * MathF.PI;

    public static float AzimuthCell => AzimuthSpan / Bins; // 37.5 degrees

    /// <summary>Elevation span, centred on the equator (+/-70 degrees).</summary>
    public const float ElevationSpan = 140f / 180f * MathF.PI;

    public static float ElevationCell => ElevationSpan / Bins; // 17.5 degrees

    /// <summary>What must clear every wall: the worst-case system envelope plus the
    /// same wall margin the cube grid keeps.</summary>
    public static float Envelope => GalaxyLayout.MaxSystemRadius + GalaxyLayout.WallMargin;

    // ── Galaxy scatter ─────────────────────────────────────────────────────────────

    /// <summary>Distance band the galaxy centres scatter across.</summary>
    public const float ScatterMin = 4600f;
    public const float ScatterMax = 6400f;

    /// <summary>
    /// Galaxy centres on golden-spiral directions (well-spread on the sphere by
    /// construction) at hashed radii — 3D space, no lane. Pure in g; separation is
    /// pinned by test, never trusted.
    /// </summary>
    public static GalaxyPoint GalaxyCenter(int g)
    {
        float y = 1f - 2f * (g + 0.5f) / Bins;              // spread in elevation
        float ring = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
        float azimuth = g * 2.399963f;                       // golden angle
        ulong hash = Fnv1a.Add(Fnv1a.OffsetBasis, 0x5AD1A1 + g);
        float radius = ScatterMin + (ScatterMax - ScatterMin) * GalaxyLayout.HashUnit(hash, 3);
        return new GalaxyPoint(
            ring * MathF.Cos(azimuth) * radius,
            y * radius,
            ring * MathF.Sin(azimuth) * radius);
    }

    // ── Star placement ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hashed uniform placement inside the wedge, inset per wall kind so the
    /// worst-case system fits. Radius first, then elevation (its inset needs r),
    /// then azimuth (its inset needs the axis distance rho = r*cos(elevation)).
    /// Same six hash bytes the cube grid uses.
    /// </summary>
    public static GalaxyPoint StarPosition(int i, int j, int k, int g)
    {
        ulong hash = GalaxyLayout.CellHash(i, j, k, g);

        float r = Place(
            CoreRadius + i * ShellThickness, ShellThickness, Envelope, Unit(hash, 0, 1));

        float elevationInset = MathF.Asin(MathF.Min(1f, Envelope / r));
        float elevation = Place(
            -ElevationSpan / 2f + k * ElevationCell, ElevationCell, elevationInset, Unit(hash, 4, 5));

        float rho = r * MathF.Cos(elevation);
        float azimuthInset = MathF.Asin(MathF.Min(1f, Envelope / rho));
        float azimuth = Place(
            j * AzimuthCell, AzimuthCell, azimuthInset, Unit(hash, 2, 3));

        return GalaxyCenter(g) + new GalaxyPoint(
            rho * MathF.Cos(azimuth), r * MathF.Sin(elevation), rho * MathF.Sin(azimuth));
    }

    /// <summary>Uniform in [low + inset, low + width − inset]; the cell centre when
    /// the insets meet (they never do at the shipped constants — pinned by test).</summary>
    private static float Place(float low, float width, float inset, float u)
    {
        float usable = width - 2f * inset;
        return usable <= 0f ? low + width / 2f : low + inset + u * usable;
    }

    private static float Unit(ulong hash, int byteA, int byteB) =>
        (GalaxyLayout.HashUnit(hash, byteA) * 255f + GalaxyLayout.HashUnit(hash, byteB)) / 256f;

    // ── Inverse: position → bins ───────────────────────────────────────────────────

    public static (int I, int J, int K)? SectorOf(GalaxyPoint local)
    {
        float r = local.Length;
        if (r < CoreRadius || r >= OuterRadius || r <= 1e-3f)
        {
            return null;
        }
        float elevation = MathF.Asin(Math.Clamp(local.Y / r, -1f, 1f));
        if (elevation < -ElevationSpan / 2f || elevation >= ElevationSpan / 2f)
        {
            return null;
        }
        float azimuth = MathF.Atan2(local.Z, local.X);
        if (azimuth < 0f)
        {
            azimuth += MathF.Tau;
        }
        if (azimuth >= AzimuthSpan)
        {
            return null; // the seam
        }
        int i = (int)((r - CoreRadius) / ShellThickness);
        int j = (int)(azimuth / AzimuthCell);
        int k = (int)((elevation + ElevationSpan / 2f) / ElevationCell);
        return (Math.Clamp(i, 0, Bins - 1), Math.Clamp(j, 0, Bins - 1), Math.Clamp(k, 0, Bins - 1));
    }

    // ── Wireframes ─────────────────────────────────────────────────────────────────

    /// <summary>The wedge outline with CHORD edges — at 37.5 degree arcs the
    /// straight-line simplification reads fine and costs a rebuild of twelve
    /// segments instead of tessellated arcs.</summary>
    public static IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> CellWireframe(int i, int j, int k)
    {
        var corners = new GalaxyPoint[8];
        for (int c = 0; c < 8; c++)
        {
            float r = CoreRadius + (i + ((c & 1) == 0 ? 0 : 1)) * ShellThickness;
            float azimuth = (j + ((c & 2) == 0 ? 0 : 1)) * AzimuthCell;
            float elevation = -ElevationSpan / 2f + (k + ((c & 4) == 0 ? 0 : 1)) * ElevationCell;
            float rho = r * MathF.Cos(elevation);
            corners[c] = new GalaxyPoint(
                rho * MathF.Cos(azimuth), r * MathF.Sin(elevation), rho * MathF.Sin(azimuth));
        }
        return CubeGalaxyGeometry.BoxEdges(corners);
    }

    /// <summary>Three orthogonal great-circle rings at the outer radius — the ball's
    /// answer to the cube's edge box.</summary>
    public static IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> BoundsWireframe()
    {
        const int segments = 48;
        var list = new List<(GalaxyPoint, GalaxyPoint)>(3 * segments);
        for (int ring = 0; ring < 3; ring++)
        {
            GalaxyPoint Point(float angle)
            {
                float c = MathF.Cos(angle) * OuterRadius, s = MathF.Sin(angle) * OuterRadius;
                return ring switch
                {
                    0 => new GalaxyPoint(c, s, 0f),
                    1 => new GalaxyPoint(c, 0f, s),
                    _ => new GalaxyPoint(0f, c, s),
                };
            }
            for (int seg = 0; seg < segments; seg++)
            {
                list.Add((Point(seg / (float)segments * MathF.Tau),
                    Point((seg + 1) / (float)segments * MathF.Tau)));
            }
        }
        return list;
    }
}

/// <summary>The radial experiment behind the geometry seam.</summary>
public sealed class RadialGalaxyGeometry : IGalaxyGeometry
{
    public string Name => "radial";

    public float GalaxyRadius => RadialGalaxyLayout.OuterRadius;

    public GalaxyPoint GalaxyCenter(int g) => RadialGalaxyLayout.GalaxyCenter(g);

    public GalaxyPoint StarPosition(int i, int j, int k, int g) =>
        RadialGalaxyLayout.StarPosition(i, j, k, g);

    public (int I, int J, int K)? SectorOf(GalaxyPoint local) => RadialGalaxyLayout.SectorOf(local);

    public IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> CellWireframe(int i, int j, int k) =>
        RadialGalaxyLayout.CellWireframe(i, j, k);

    public IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> BoundsWireframe() =>
        RadialGalaxyLayout.BoundsWireframe();
}
