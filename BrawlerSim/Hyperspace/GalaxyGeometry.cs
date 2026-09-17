namespace BrawlerSim.Hyperspace;

/// <summary>
/// The layout a galaxy view plots against (2026-09-17, designer: a spherical/radial
/// EXPERIMENT alongside the working cube grid, never replacing it). Everything the
/// rendering stack needs to place and read the archive lives behind this seam, so
/// both grids share one renderer, one targeting system, one dashboard.
///
/// Implementations are pure and deterministic — positions are functions of the cell
/// alone, and a snapshot rebuild never moves a star (§8.1).
/// </summary>
public interface IGalaxyGeometry
{
    /// <summary>Automation/status name: "cube" or "radial".</summary>
    string Name { get; }

    /// <summary>Bounding radius of one galaxy around its centre — drives the bounds
    /// marker, halo scale, near/far fade bands and the sector-map zoom.</summary>
    float GalaxyRadius { get; }

    GalaxyPoint GalaxyCenter(int g);

    GalaxyPoint StarPosition(int i, int j, int k, int g);

    /// <summary>Bin coordinates of a point LOCAL to a galaxy centre, or null when it
    /// lies outside the grid — the PositionPod readout and the G sector grid.</summary>
    (int I, int J, int K)? SectorOf(GalaxyPoint local);

    /// <summary>The bucket's outline as line segments local to the galaxy centre —
    /// the hover/lock cell highlight and the ship's sector box. A cube cell is its
    /// 12 edges; a spherical wedge is drawn with chord edges.</summary>
    IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> CellWireframe(int i, int j, int k);

    /// <summary>One galaxy's bounds marker as line segments local to its centre —
    /// the cube's edge box, or three great-circle rings for the ball.</summary>
    IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> BoundsWireframe();
}

/// <summary>The shipping cube grid, exactly as GalaxyLayout defines it — this class
/// only adapts those functions to the seam and must never change their output
/// (pinned by test against the statics).</summary>
public sealed class CubeGalaxyGeometry : IGalaxyGeometry
{
    public string Name => "cube";

    public float GalaxyRadius => GalaxyLayout.Half;

    public GalaxyPoint GalaxyCenter(int g) => GalaxyLayout.GalaxyCenter(g);

    public GalaxyPoint StarPosition(int i, int j, int k, int g) =>
        GalaxyLayout.StarPosition(i, j, k, g);

    public (int I, int J, int K)? SectorOf(GalaxyPoint local)
    {
        int i = GalaxyNavigation.SectorOf(local.X);
        int j = GalaxyNavigation.SectorOf(local.Y);
        int k = GalaxyNavigation.SectorOf(local.Z);
        return i < 0 || j < 0 || k < 0 ? null : (i, j, k);
    }

    public IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> CellWireframe(int i, int j, int k)
    {
        float s = GalaxyLayout.S;
        var min = new GalaxyPoint((i - 4f) * s, (j - 4f) * s, (k - 4f) * s);
        var corners = new GalaxyPoint[8];
        for (int c = 0; c < 8; c++)
        {
            corners[c] = new GalaxyPoint(
                min.X + ((c & 1) == 0 ? 0f : s),
                min.Y + ((c & 2) == 0 ? 0f : s),
                min.Z + ((c & 4) == 0 ? 0f : s));
        }
        return BoxEdges(corners);
    }

    public IReadOnlyList<(GalaxyPoint A, GalaxyPoint B)> BoundsWireframe()
    {
        float h = GalaxyLayout.Half;
        var corners = new GalaxyPoint[8];
        for (int c = 0; c < 8; c++)
        {
            corners[c] = new GalaxyPoint(
                (c & 1) == 0 ? -h : h, (c & 2) == 0 ? -h : h, (c & 4) == 0 ? -h : h);
        }
        return BoxEdges(corners);
    }

    internal static IReadOnlyList<(GalaxyPoint, GalaxyPoint)> BoxEdges(GalaxyPoint[] corners)
    {
        int[,] edges =
        {
            { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 3 }, { 4, 5 }, { 4, 6 },
            { 5, 7 }, { 6, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };
        var list = new List<(GalaxyPoint, GalaxyPoint)>(12);
        for (int e = 0; e < edges.GetLength(0); e++)
        {
            list.Add((corners[edges[e, 0]], corners[edges[e, 1]]));
        }
        return list;
    }
}
