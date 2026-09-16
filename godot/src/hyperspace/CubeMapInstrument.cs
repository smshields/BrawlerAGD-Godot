using Godot;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>
/// The MapPod's instrument (galaxy-view.md §7): a fixed axonometric, NORTH-UP view of
/// the NEAREST galaxy only — wire cube, one coloured point per star, the ship as an
/// orange dot with a heading tick, and a ring on the hovered or locked star when it
/// lives in this galaxy. It never rotates with the ship; that is the whole point of
/// having it next to a view that does.
///
/// Deliberately its own small instrument rather than the Evolve screen's archive cube.
/// The designer's stated direction is that the hypercube visualization eventually
/// takes this slot as the ship's star map, so the pod talks to it through
/// <see cref="IMapPodInstrument"/> and the swap is a constructor change.
/// </summary>
public interface IMapPodInstrument
{
    Control Root { get; }

    void Refresh(GalaxyStarField stars, int galaxy, Vector3 ship, float yaw, GalaxyStar? highlight);
}

public sealed partial class CubeMapInstrument : SubViewportContainer, IMapPodInstrument
{
    private const float Yaw = 0.72f, Pitch = 0.38f;

    /// <summary>Derived from HALF so the wire cube always fits its monitor, whatever
    /// the world scale is.</summary>
    private const float Scale = 30f / GalaxyLayout.Half;

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private MultiMeshInstance3D _points = null!;
    private MeshInstance3D _cube = null!;
    private MeshInstance3D _shipMark = null!;
    private MeshInstance3D _highlightMark = null!;

    public Control Root => this;

    public override void _Ready()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(150f, 112f);

        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
            TransparentBg = true,
        };
        AddChild(_viewport);

        _camera = new Camera3D
        {
            Current = true,
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 82f,
            Near = 0.1f,
            Far = 600f,
        };
        // North-up axonometric, parked once: the instrument never turns. LookAt needs
        // the node in the tree, so the camera is parented before it is aimed.
        _viewport.AddChild(_camera);
        _camera.LookAtFromPosition(
            new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch),
                Mathf.Cos(Yaw) * Mathf.Cos(Pitch)) * 160f,
            Vector3.Zero, Vector3.Up);

        _points = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = new BoxMesh
                {
                    Size = Vector3.One * 1.1f,
                    Material = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        VertexColorUseAsAlbedo = true,
                    },
                },
            },
        };
        _viewport.AddChild(_points);

        _cube = new MeshInstance3D { Mesh = Wireframe(30f, new Color(0.45f, 0.55f, 0.75f, 0.5f)) };
        _viewport.AddChild(_cube);

        _shipMark = new MeshInstance3D { Mesh = ShipMarker() };
        _viewport.AddChild(_shipMark);

        _highlightMark = new MeshInstance3D { Mesh = Wireframe(2.6f, new Color(1f, 0.82f, 0.35f, 0.95f)), Visible = false };
        _viewport.AddChild(_highlightMark);
    }

    public void Refresh(GalaxyStarField stars, int galaxy, Vector3 ship, float yaw, GalaxyStar? highlight)
    {
        Vector3 center = GalaxyVec.From(GalaxyLayout.GalaxyCenter(galaxy));
        System.Collections.Generic.IReadOnlyList<GalaxyStar> list = stars.StarsIn(galaxy);
        MultiMesh mesh = _points.Multimesh;
        mesh.InstanceCount = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            mesh.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                (list[i].Position - center) * Scale));
            mesh.SetInstanceColor(i, list[i].Color);
        }

        // The ship is drawn even when it is outside this galaxy — clamped to the
        // monitor's edge, so "you are over there" still reads.
        Vector3 local = (ship - center) * Scale;
        _shipMark.Position = local.Clamp(Vector3.One * -34f, Vector3.One * 34f);
        _shipMark.Rotation = new Vector3(0f, yaw, 0f);

        if (highlight is not null && highlight.G == galaxy)
        {
            _highlightMark.Visible = true;
            _highlightMark.Position = (highlight.Position - center) * Scale;
        }
        else
        {
            _highlightMark.Visible = false;
        }
    }

    private static ImmediateMesh Wireframe(float half, Color color)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = color,
        };
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = new Vector3(
                (i & 1) == 0 ? -half : half,
                (i & 2) == 0 ? -half : half,
                (i & 4) == 0 ? -half : half);
        }
        int[,] edges =
        {
            { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 3 }, { 4, 5 }, { 4, 6 },
            { 5, 7 }, { 6, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };
        for (int e = 0; e < edges.GetLength(0); e++)
        {
            mesh.SurfaceAddVertex(corners[edges[e, 0]]);
            mesh.SurfaceAddVertex(corners[edges[e, 1]]);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    /// <summary>Ship dot plus a heading tick down its local −Z, so the instrument
    /// shows which way you are pointing as well as where you are.</summary>
    private static ImmediateMesh ShipMarker()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.6f, 0.2f),
        };
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        foreach (Vector3 axis in new[] { Vector3.Right, Vector3.Up, Vector3.Back })
        {
            mesh.SurfaceAddVertex(-axis * 1.4f);
            mesh.SurfaceAddVertex(axis * 1.4f);
        }
        mesh.SurfaceAddVertex(Vector3.Zero);
        mesh.SurfaceAddVertex(new Vector3(0f, 0f, -6f));
        mesh.SurfaceEnd();
        return mesh;
    }
}
