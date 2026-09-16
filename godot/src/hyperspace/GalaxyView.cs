using System.Collections.Generic;
using Godot;
using BrawlerSim.Determinism;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>
/// GALAXY — the archive as a place you fly through (2026-09-16, the designer's
/// galaxy-view spec; docs/features/galaxy-view.md). Each filled descriptor cell is a
/// star; the cell's other occupants orbit it as planets; the fourth descriptor
/// (timing) is a lane of eight galaxies along +X.
///
/// Additive by designer decision (2026-09-16): this is a THIRD tab beside the cube
/// lattice, which keeps both of its mounts unchanged. Read-only with respect to the
/// run — nothing here touches engine RNG or evaluation order.
///
/// THE NO-POP-IN RULE (§8.1) governs every visibility decision in this file: every
/// range-based one is a GalaxyNavigation.FadeBand multiplied into alpha, never an
/// `if (distance &lt; x)`. Objects reach zero alpha before they stop being drawn.
/// </summary>
public partial class GalaxyView : Control
{
    private const float CameraFov = 55f;

    /// <summary>The lane is 7 x GAP = 24,640 units end to end. A far plane short of
    /// that clips whole galaxies out of existence and pops them back in as you
    /// approach (§8.1.1) — the spec's 20,000 was sized for the un-doubled world.</summary>
    private const float CameraFar = 40_000f;

    /// <summary>Raised from the spec's 0.1: gl_compatibility has no reverse-Z, and a
    /// 400,000:1 depth ratio z-fights. Nothing gets this close — a warp halts 6 units
    /// out and the largest star is 2.4 across.</summary>
    private const float CameraNear = 0.5f;

    private const int AmbientPoints = 420;

    private SubViewport _viewport = null!;
    private SubViewportContainer _viewportContainer = null!;
    private Node3D _world = null!;
    private Node3D _shipRig = null!;
    private Node3D _pitch = null!;
    private Camera3D _camera = null!;
    private GalaxyStarField _stars = null!;
    private MultiMeshInstance3D _ambientSky = null!;
    private Label _statusLine = null!;

    private readonly MeshInstance3D[] _halos = new MeshInstance3D[GalaxyLayout.Bins];
    private readonly StandardMaterial3D[] _haloMaterials = new StandardMaterial3D[GalaxyLayout.Bins];
    private readonly Label3D[] _names = new Label3D[GalaxyLayout.Bins];
    private readonly MeshInstance3D[] _bounds = new MeshInstance3D[GalaxyLayout.Bins];
    private readonly StandardMaterial3D[] _boundsMaterials = new StandardMaterial3D[GalaxyLayout.Bins];

    private HyperspaceSnapshot? _snapshot;
    private int _nearestGalaxy;
    private GalaxyHud _hud = null!;
    private readonly ShipState _ship = new();

    /// <summary>Ship position in world space.</summary>
    public Vector3 ShipPosition => GalaxyVec.From(_ship.Position);

    public int NearestGalaxy => _nearestGalaxy;

    /// <summary>Steering is suspended — not zeroed — while the cursor is off the
    /// viewport, over the dashboard, or while a lock or warp owns the rotation
    /// (§4). Later phases add their own reasons; this is the single gate.</summary>
    public bool SteeringSuspended { get; set; }

    public override void _Ready()
    {
        BuildUi();
        // Parked outside galaxy 4 looking back down the lane.
        _ship.Position = GalaxyVec.To(GalaxyVec.From(GalaxyLayout.GalaxyCenter(4))
            + new Vector3(0f, GalaxyLayout.Half * 0.35f, GalaxyLayout.Half * 1.9f));
        ApplyShipToRig();
        _nearestGalaxy = GalaxyNavigation.NearestGalaxy(ShipPosition.X);
        ApplyAutomation();
        ApplyStatus();
    }

    /// <summary>BRAWLER_GALAXY_CAM / _FLY: the view's only headless handles — park
    /// the ship somewhere, or fly it straight ahead for N seconds first (the §8.1
    /// no-pop-in pass is a boost run down the lane, which needs simulating).</summary>
    private void ApplyAutomation()
    {
        string cam = AutomationEnv.GalaxyCam;
        if (cam.Length > 0 || AutomationEnv.GalaxyFly.Length > 0)
        {
            // Frozen for the capture: otherwise the cursor's resting position keeps
            // steering the ship for the length of the run before the shot.
            SteeringSuspended = true;
        }
        if (cam.Length > 0)
        {
            string[] parts = cam.Split(',');
            if (parts.Length >= 3)
            {
                _ship.Position = new BrawlerSim.Hyperspace.GalaxyPoint(
                    float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            }
            if (parts.Length >= 5)
            {
                _ship.Yaw = float.Parse(parts[3]);
                _ship.Pitch = float.Parse(parts[4]);
            }
            ApplyShipToRig();
        }

        string fly = AutomationEnv.GalaxyFly;
        if (fly.Length > 0)
        {
            string[] parts = fly.Split(',');
            float seconds = float.Parse(parts[0]);
            bool boost = parts.Length > 1 && parts[1] == "boost";
            var input = new ShipInput(0f, 0f, 1f, 0f, boost, false, SteeringEnabled: false);
            for (float t = 0f; t < seconds; t += 1f / 60f)
            {
                _ship.Step(input, 1f / 60f);
            }
            ApplyShipToRig();
        }
    }

    /// <summary>Publish an archive snapshot (main thread). The view re-renders per
    /// snapshot, never per insertion.</summary>
    public void SetSnapshot(HyperspaceSnapshot snapshot)
    {
        _snapshot = snapshot;
        var pool = new List<float>();
        foreach (HyperspaceEntry entry in snapshot.Entries)
        {
            pool.Add(entry.Fitness);
            foreach (HyperspaceEntry member in entry.Occupants)
            {
                pool.Add(member.Fitness);
            }
        }
        _stars.SetSnapshot(snapshot, new FitnessScale(pool));
        ApplyStatus();
    }

    public void Clear()
    {
        _snapshot = null;
        _stars.SetSnapshot(null, new FitnessScale(System.Array.Empty<float>()));
        ApplyStatus();
    }

    public override void _Process(double delta)
    {
        Fly((float)delta);
        _nearestGalaxy = GalaxyNavigation.NearestGalaxy(ShipPosition.X, _nearestGalaxy);
        // The sky follows the ship's POSITION but not its rotation — parallax-free
        // backdrop, decorative only.
        _ambientSky.Position = ShipPosition;
        UpdateGalaxyMarkers();
    }

    /// <summary>Gather pilot intent and advance the flight model (§4). Mouse steers
    /// from the cursor's offset to the viewport centre; keys translate.</summary>
    private void Fly(float delta)
    {
        Vector2 size = _viewportContainer.Size;
        Vector2 cursor = _viewportContainer.GetLocalMousePosition();
        bool pointerInside = size.X > 0f && size.Y > 0f
            && cursor.X >= 0f && cursor.Y >= 0f && cursor.X <= size.X && cursor.Y <= size.Y;
        bool steering = pointerInside && !SteeringSuspended && IsVisibleInTree();

        Vector2 normalized = size.X > 0f && size.Y > 0f
            ? (cursor - size / 2f) / (size / 2f)
            : Vector2.Zero;

        var input = new ShipInput(
            SteerX: normalized.X,
            SteerY: normalized.Y,
            Forward: Axis("hs_thrust_fwd", "hs_thrust_back"),
            Strafe: Axis("hs_strafe_right", "hs_strafe_left"),
            Boost: Input.IsActionPressed("hs_boost"),
            Brake: Input.IsActionPressed("hs_brake"),
            SteeringEnabled: steering);

        _ship.Step(input, delta);
        ApplyShipToRig();
        _hud.SteerCursor = steering ? cursor : null;
    }

    private static float Axis(string positive, string negative) =>
        (Input.IsActionPressed(positive) ? 1f : 0f) - (Input.IsActionPressed(negative) ? 1f : 0f);

    /// <summary>Yaw on the rig, pitch on its child — so pitch never rolls the view.</summary>
    private void ApplyShipToRig()
    {
        _shipRig.Position = GalaxyVec.From(_ship.Position);
        _shipRig.Rotation = new Vector3(0f, _ship.Yaw, 0f);
        _pitch.Rotation = new Vector3(_ship.Pitch, 0f, 0f);
    }

    /// <summary>
    /// Per-galaxy halo, name and bounds-box alpha. Every galaxy is drawn every frame
    /// with its OWN distance fade — nothing keys off the nearest-galaxy readout
    /// (§8.1.6), so nothing jumps when that flips at a lane midpoint.
    /// </summary>
    private void UpdateGalaxyMarkers()
    {
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            float distance = ShipPosition.DistanceTo(GalaxyVec.From(GalaxyLayout.GalaxyCenter(g)));

            // Halo ramps IN as you leave a galaxy, so it never blinks on at the edge.
            float presence = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.4f * GalaxyLayout.NearGalaxy, zeroAt: 0.8f * GalaxyLayout.NearGalaxy);
            // ...and out again at the far end of the lane, where eight labels would
            // otherwise stack on the horizon.
            float reach = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.5f * GalaxyLayout.Gap, zeroAt: 2.4f * GalaxyLayout.Gap);

            // Additive: keep the peak low so a galaxy seen from inside its own
            // neighbour does not blow out the stars in front of it.
            float haloAlpha = presence * Mathf.Max(reach, 0.12f) * 0.55f;
            _haloMaterials[g].AlbedoColor = new Color(0.62f, 0.70f, 0.95f, haloAlpha);
            _names[g].Modulate = new Color(1f, 1f, 1f, presence * reach);

            float boxAlpha = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.6f * GalaxyLayout.Half, zeroAt: 5f * GalaxyLayout.Half) * 0.28f;
            _boundsMaterials[g].AlbedoColor = new Color(0.42f, 0.52f, 0.72f, boxAlpha);
        }
    }

    private void ApplyStatus()
    {
        if (_snapshot is null)
        {
            _statusLine.Text = "NO ARCHIVE — START A RUN";
            return;
        }
        _statusLine.Text = $"{_snapshot.StatusLine} · {_stars.Count} STARS";
    }

    private void BuildUi()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
        };
        column.AddChild(_viewportContainer);
        _viewport = new SubViewport
        {
            OwnWorld3D = true,
            // WhenVisible: no GPU cost while the run is watched from another tab.
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        _viewportContainer.AddChild(_viewport);
        BuildWorld();

        _hud = new GalaxyHud { Name = "Hud" };
        _viewportContainer.AddChild(_hud);

        _statusLine = UiWidgets.MakeLabel("", 12);
        column.AddChild(_statusLine);

        Resized += UpdateProjectionUniforms;
    }

    private void BuildWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.02f, 0.02f, 0.04f),
        };
        _viewport.AddChild(new WorldEnvironment { Environment = environment });

        _world = new Node3D { Name = "World" };
        _viewport.AddChild(_world);

        // Yaw on the rig, pitch on the child — so pitch never rolls the horizon.
        _shipRig = new Node3D { Name = "ShipRig" };
        _world.AddChild(_shipRig);
        _pitch = new Node3D { Name = "Pitch" };
        _shipRig.AddChild(_pitch);
        _camera = new Camera3D
        {
            Current = true,
            Fov = CameraFov,
            Near = CameraNear,
            Far = CameraFar,
        };
        _pitch.AddChild(_camera);

        _stars = new GalaxyStarField { Name = "Stars" };
        _world.AddChild(_stars);

        BuildAmbientSky();
        BuildGalaxyMarkers();
        UpdateProjectionUniforms();
    }

    /// <summary>Far decorative shell — seeded once with Pcg32 (never System.Random,
    /// even in the view layer), parented to ship position but not rotation.</summary>
    private void BuildAmbientSky()
    {
        var rng = new Pcg32(0xA11BE5UL);
        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new QuadMesh
            {
                Size = Vector2.One,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                    VertexColorUseAsAlbedo = true,
                    DisableReceiveShadows = true,
                    AlbedoTexture = GalaxyShaders.HaloTexture(),
                },
            },
            InstanceCount = AmbientPoints,
        };
        const float shell = 9_000f;
        for (int i = 0; i < AmbientPoints; i++)
        {
            float u = rng.NextFloat() * 2f - 1f;
            float theta = rng.NextFloat() * Mathf.Tau;
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            var direction = new Vector3(radial * Mathf.Cos(theta), u, radial * Mathf.Sin(theta));
            float size = 12f + rng.NextFloat() * 26f;
            mesh.SetInstanceTransform(i, new Transform3D(
                Basis.Identity.Scaled(Vector3.One * size), direction * shell));
            float brightness = 0.18f + rng.NextFloat() * 0.3f;
            mesh.SetInstanceColor(i, new Color(brightness, brightness, brightness * 1.1f));
        }
        _ambientSky = new MultiMeshInstance3D { Name = "AmbientSky", Multimesh = mesh };
        _world.AddChild(_ambientSky);
    }

    private void BuildGalaxyMarkers()
    {
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            Vector3 center = GalaxyVec.From(GalaxyLayout.GalaxyCenter(g));

            // The halo is what a galaxy looks like from outside: one additive
            // billboard roughly the size of the galaxy itself. Alpha-blended it
            // reads as grey fog over everything in front of it, and oversized it
            // swamps the stars it is supposed to stand in for.
            _haloMaterials[g] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                DisableReceiveShadows = true,
                AlbedoTexture = GalaxyShaders.HaloTexture(),
                AlbedoColor = new Color(0.62f, 0.70f, 0.95f, 0f),
                NoDepthTest = false,
            };
            _halos[g] = new MeshInstance3D
            {
                Name = $"Halo{g}",
                Mesh = new QuadMesh
                {
                    // 2.8x the galaxy edge: the falloff's visible disc is only about
                    // a third of the sprite, so a quad sized to the galaxy reads as a
                    // dot floating inside its own star cloud.
                    Size = Vector2.One * (GalaxyLayout.Extent * 2.8f),
                    Material = _haloMaterials[g],
                },
                Position = center,
            };
            _world.AddChild(_halos[g]);

            _names[g] = new Label3D
            {
                Name = $"GalaxyName{g}",
                Text = $"GALAXY {g}",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                NoDepthTest = true,
                FontSize = 128,
                PixelSize = GalaxyLayout.Half * 0.0011f,
                Position = center + new Vector3(0f, GalaxyLayout.Half * 1.15f, 0f),
                Modulate = new Color(1f, 1f, 1f, 0f),
            };
            _world.AddChild(_names[g]);

            _boundsMaterials[g] = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.42f, 0.52f, 0.72f, 0f),
                DisableReceiveShadows = true,
            };
            _bounds[g] = new MeshInstance3D
            {
                Name = $"Bounds{g}",
                Mesh = BoxWireframe(center, GalaxyLayout.Half, _boundsMaterials[g]),
            };
            _world.AddChild(_bounds[g]);
        }
    }

    /// <summary>A galaxy's bounds box as line segments.</summary>
    private static ImmediateMesh BoxWireframe(Vector3 center, float half, Material material)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = center + new Vector3(
                (i & 1) == 0 ? -half : half,
                (i & 2) == 0 ? -half : half,
                (i & 4) == 0 ? -half : half);
        }
        int[,] edges =
        {
            { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 3 },
            { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };
        for (int e = 0; e < edges.GetLength(0); e++)
        {
            mesh.SurfaceAddVertex(corners[edges[e, 0]]);
            mesh.SurfaceAddVertex(corners[edges[e, 1]]);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    /// <summary>The star shader's minimum-screen-size clamp is in world units, so it
    /// needs the viewport's pixel scale — which changes with the tab's size.</summary>
    private void UpdateProjectionUniforms()
    {
        float height = Mathf.Max(1f, _viewport.Size.Y);
        _stars.Material.SetShaderParameter("world_per_pixel",
            GalaxyShaders.WorldPerPixel(CameraFov, height));
    }
}
