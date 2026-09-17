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
    private GalaxyPlanets _planets = null!;
    private GalaxyTargeting _targeting = null!;

    /// <summary>The grid everything plots against. CUBE is the shipping default; the
    /// RADIAL experiment (2026-09-17, designer) swaps in a spherical grid with
    /// galaxies scattered through 3D space — same renderer, same targeting, same
    /// dashboard, different geometry. The toggle re-applies the current snapshot.</summary>
    private IGalaxyGeometry _geometry = new CubeGalaxyGeometry();
    private Button _gridToggle = null!;
    private MeshInstance3D _cellHighlight = null!;
    private StandardMaterial3D _cellHighlightMaterial = null!;
    private MeshInstance3D _sectorGrid = null!;
    private StandardMaterial3D _sectorGridMaterial = null!;
    private bool _sectorGridOn;
    private GalaxyTarget? _hover;
    private GalaxyTarget? _lock;
    private bool _warping;
    private GalaxyTarget? _warpTarget;
    private Vector3 _warpFrom, _warpTo;
    private float _warpElapsed, _warpDuration;
    private MultiMeshInstance3D _ambientSky = null!;
    private Label _statusLine = null!;

    private readonly MeshInstance3D[] _halos = new MeshInstance3D[GalaxyLayout.Bins];
    private readonly StandardMaterial3D[] _haloMaterials = new StandardMaterial3D[GalaxyLayout.Bins];
    private readonly Label3D[] _names = new Label3D[GalaxyLayout.Bins];
    private readonly MeshInstance3D[] _bounds = new MeshInstance3D[GalaxyLayout.Bins];
    private readonly StandardMaterial3D[] _boundsMaterials = new StandardMaterial3D[GalaxyLayout.Bins];

    private HyperspaceSnapshot? _snapshot;
    /// <summary>Newest snapshot that arrived while the tab was hidden.</summary>
    private HyperspaceSnapshot? _deferredSnapshot;
    private int _nearestGalaxy;
    /// <summary>Orbit clock. Seconds since the view opened — planets are a pure
    /// function of it, so two clients at the same clock draw the same sky.</summary>
    private float _clock;
    /// <summary>BRAWLER_GALAXY_LOCK: lock on the first frame that has a hover.</summary>
    private bool _pendingAutoLock;
    private GalaxyHud _hud = null!;
    private GalaxyDashboard _dashboard = null!;
    private readonly ShipState _ship = new();

    /// <summary>Ship position in world space.</summary>
    public Vector3 ShipPosition => GalaxyVec.From(_ship.Position);

    public int NearestGalaxy => _nearestGalaxy;

    /// <summary>Raised when a star or planet is locked — the Evolve screen routes it
    /// into the same preview / ADD TO GAMES plumbing a cube pick uses.</summary>
    public System.Action<HyperspaceEntry>? EntrySelected;

    /// <summary>Captures freeze the ship so the cursor's resting position does not
    /// steer it while a run finishes.</summary>
    private bool _automationFreeze;

    /// <summary>Steering is suspended — not zeroed — while a lock or a warp owns the
    /// rotation, or while a capture has frozen the ship (§4). Tracked as separate
    /// REASONS: releasing a lock must not hand steering back to a warp that is still
    /// running, or to a frozen capture.</summary>
    public bool SteeringSuspended => _automationFreeze || _lock is not null || _warping;

    public override void _Ready()
    {
        BuildUi();
        if (AutomationEnv.GalaxyGrid == "radial")
        {
            SetGeometry(new RadialGalaxyGeometry(), announce: false);
        }
        ParkShip();
        _nearestGalaxy = GalaxyNavigation.NearestGalaxy(GalaxyVec.To(ShipPosition), _geometry);
        ApplyAutomation();
        ApplyStatus();
    }

    /// <summary>The boot overlook: outside galaxy 4, looking at it.</summary>
    private void ParkShip()
    {
        Vector3 center = GalaxyVec.From(_geometry.GalaxyCenter(4));
        Vector3 eye = center + new Vector3(0f, _geometry.GalaxyRadius * 0.35f, _geometry.GalaxyRadius * 1.9f);
        _ship.Position = GalaxyVec.To(eye);
        Vector3 toCenter = center - eye;
        _ship.Yaw = Mathf.Atan2(-toCenter.X, -toCenter.Z);
        _ship.Pitch = Mathf.Atan2(toCenter.Y, new Vector2(toCenter.X, toCenter.Z).Length());
        ApplyShipToRig();
    }

    /// <summary>Swap grids: rebuild the markers for the new centres, re-plot the
    /// snapshot, drop any lock (its object may not exist where you now are) and
    /// re-park the ship at the overlook.</summary>
    private void SetGeometry(IGalaxyGeometry geometry, bool announce)
    {
        _geometry = geometry;
        _stars.Geometry = geometry;
        _targeting.Geometry = geometry;
        SetLock(null);
        _warping = false;
        _warpTarget = null;
        RebuildGalaxyMarkers();
        _lastHighlightCell = null;
        _lastSectorCell = null;
        if (_snapshot is { } current)
        {
            ApplySnapshot(current);
        }
        ParkShip();
        _nearestGalaxy = GalaxyNavigation.NearestGalaxy(GalaxyVec.To(ShipPosition), _geometry);
        if (_gridToggle is not null)
        {
            _gridToggle.Text = geometry.Name == "radial" ? "GRID: RADIAL EXP" : "GRID: CUBE";
        }
        if (announce)
        {
            _hud.Toast($"{geometry.Name} grid");
        }
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
            _automationFreeze = true;
        }
        if (cam == "densest")
        {
            // Park just outside the most populated system — the only way to aim a
            // headless capture at planets, which exist wherever the search converged.
            GalaxyStar? best = null;
            for (int g = 0; g < GalaxyLayout.Bins; g++)
            {
                foreach (GalaxyStar star in _stars.StarsIn(g))
                {
                    if (best is null || star.Entry.Occupants.Count > best.Entry.Occupants.Count)
                    {
                        best = star;
                    }
                }
            }
            if (best is not null)
            {
                float standoff = GalaxyLayout.MaxSystemRadius * 2.6f;
                Vector3 eye = best.Position + new Vector3(0f, standoff * 0.35f, standoff);
                _ship.Position = GalaxyVec.To(eye);
                // Aim AT the system, so the crosshair is on it and a lock capture has
                // something to lock.
                Vector3 toStar = best.Position - eye;
                _ship.Yaw = Mathf.Atan2(-toStar.X, -toStar.Z);
                _ship.Pitch = Mathf.Atan2(toStar.Y, new Vector2(toStar.X, toStar.Z).Length());
                ApplyShipToRig();
            }
        }
        else if (cam.Length > 0)
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
    /// snapshot, never per insertion — and only while it can be SEEN: a hidden tab
    /// parks the newest snapshot instead of rebuilding thousands of star instances
    /// per generation behind the RUN tab (2026-09-17 performance round; the cost
    /// grew with the archive and with it, the run's felt speed).</summary>
    public void SetSnapshot(HyperspaceSnapshot snapshot)
    {
        if (!IsVisibleInTree())
        {
            _deferredSnapshot = snapshot;
            return;
        }
        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(HyperspaceSnapshot snapshot)
    {
        _deferredSnapshot = null;
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
        var scale = new FitnessScale(pool);
        _stars.SetSnapshot(snapshot, scale);
        _planets.Rebuild(_stars, scale);
        // A capture aimed at "the densest system" can only resolve once there IS an
        // archive — at _Ready the sky is empty.
        if (AutomationEnv.GalaxyCam == "densest")
        {
            ApplyAutomation();
        }
        if (AutomationEnv.GalaxyLock == "hover")
        {
            _pendingAutoLock = true;
        }
        ApplyStatus();
    }

    public void Clear()
    {
        _deferredSnapshot = null;
        _snapshot = null;
        var empty = new FitnessScale(System.Array.Empty<float>());
        _stars.SetSnapshot(null, empty);
        _planets.Rebuild(_stars, empty);
        ApplyStatus();
    }

    public override void _Process(double delta)
    {
        // A hidden tab does no work: a run is normally watched from RUN, and the
        // planet pass walks every system in the archive.
        if (!IsVisibleInTree())
        {
            return;
        }
        if (_deferredSnapshot is { } parked)
        {
            ApplySnapshot(parked);
        }
        UpdateProjectionUniforms();
        Fly((float)delta);
        UpdateWarp((float)delta);
        _clock += (float)delta;
        _nearestGalaxy = GalaxyNavigation.NearestGalaxy(
            GalaxyVec.To(ShipPosition), _geometry, _nearestGalaxy);
        _planets.Update(ShipPosition, _clock);
        UpdateTargeting((float)delta);
        _dashboard.Refresh(_stars, _geometry, ShipPosition, _ship.Yaw, _ship.Speed,
            Input.IsActionPressed("hs_boost"), _nearestGalaxy, _lock ?? _hover,
            locked: _lock is not null);
        if (_pendingAutoLock && _hover is not null)
        {
            _pendingAutoLock = false;
            SetLock(_hover);
            if (AutomationEnv.GalaxyWarp == "1")
            {
                // Run the warp to completion in one frame: a capture needs the
                // ARRIVAL, and the easing itself is pinned by GalaxyNavigation tests.
                TryWarp();
                for (int guard = 0; _warping && guard < 600; guard++)
                {
                    UpdateWarp(1f / 60f);
                }
            }
        }
        UpdateSectorGrid();
        if (Input.IsActionJustPressed("hs_grid"))
        {
            _sectorGridOn = !_sectorGridOn;
            _hud.Toast(_sectorGridOn ? "sector grid on" : "sector grid off");
        }
        if (Input.IsActionJustPressed("hs_planets"))
        {
            _planets.Enabled = !_planets.Enabled;
            _hud.Toast(_planets.Enabled ? "planets on" : "planets off");
        }
        if (_lock is not null && Input.IsActionJustPressed("hs_release"))
        {
            SetLock(null);
        }
        if (Input.IsActionJustPressed("hs_warp"))
        {
            TryWarp();
        }
        // Hyperdrive steps are ignored mid-warp (§9), not queued.
        if (Input.IsActionJustPressed("hs_galaxy_prev"))
        {
            StepGalaxy(-1);
        }
        if (Input.IsActionJustPressed("hs_galaxy_next"))
        {
            StepGalaxy(1);
        }
        // The sky follows the ship's POSITION but not its rotation — parallax-free
        // backdrop, decorative only.
        _ambientSky.Position = ShipPosition;
        UpdateGalaxyMarkers();
    }

    /// <summary>
    /// Hover, lock tracking, the reticle and the cell highlight (§5). Tracking owns
    /// rotation while locked — the ship turns to keep its target, and a planet is
    /// followed live along its orbit rather than to where it was when you clicked.
    /// </summary>
    private void UpdateTargeting(float delta)
    {
        Vector2 crosshair = _viewportContainer.Size / 2f;
        _hover = _targeting.Hover(crosshair, ShipPosition);

        if (_lock is { } locked)
        {
            Vector3 target = _targeting.PositionOf(locked, _clock);
            TrackToward(target, delta);
            float radius = _targeting.ScreenRadius(locked, _clock);
            _hud.Reticle = _targeting.Project(target) is { } screen
                ? (screen, radius, GalaxyTargeting.Describe(locked))
                : null;
        }
        else
        {
            _hud.Reticle = null;
        }

        // The locked object wears the reticle, so it never also wears a hover ring.
        bool hoverIsLocked = _hover is not null && _hover.SameAs(_lock);
        _hud.HoverRing = _hover is { } hovered && !hoverIsLocked
            && _targeting.Project(_targeting.PositionOf(hovered, _clock)) is { } hoverScreen
                ? (hoverScreen, _targeting.ScreenRadius(hovered, _clock))
                : null;

        GalaxyTarget? focus = _lock ?? _hover;
        _planets.HighlightStar = focus?.Star;
        _planets.HighlightPlanet = focus?.Planet;
        UpdateCellHighlight(focus);
    }

    /// <summary>Shortest-angle turn toward a world point. Steering is suspended while
    /// this runs: tracking owns the rotation (§5).</summary>
    private void TrackToward(Vector3 target, float delta)
    {
        Vector3 delta3 = target - ShipPosition;
        if (delta3.LengthSquared() < 1e-4f)
        {
            return;
        }
        float wantedYaw = Mathf.Atan2(-delta3.X, -delta3.Z);
        float wantedPitch = Mathf.Atan2(delta3.Y, new Vector2(delta3.X, delta3.Z).Length());
        float gain = Mathf.Min(1f, GalaxyNavigation.TrackingGain * delta);
        _ship.Yaw += Mathf.AngleDifference(_ship.Yaw, wantedYaw) * gain;
        _ship.Pitch += Mathf.AngleDifference(_ship.Pitch, wantedPitch) * gain;
        _ship.Pitch = Mathf.Clamp(_ship.Pitch, -GalaxyNavigation.MaxPitch, GalaxyNavigation.MaxPitch);
        ApplyShipToRig();
    }

    /// <summary>
    /// Wireframe the hovered/locked object's OWN archive cell. Without it a star's
    /// bucket membership is not readable in flight, and the galaxy stops reading as
    /// the grid it actually is. Quiets as you fly inside the cell, where its walls
    /// would otherwise swamp the view. Independent of the G sector-grid toggle.
    /// </summary>
    private (int, int, int, int)? _lastHighlightCell;

    private void UpdateCellHighlight(GalaxyTarget? focus)
    {
        if (focus?.Star is not { } star)
        {
            _cellHighlightMaterial.AlbedoColor = new Color(1f, 0.82f, 0.35f, 0f);
            return;
        }
        // The bucket's outline comes from the geometry (a box, or a spherical
        // wedge); rebuilt only when the focused cell changes.
        if (_lastHighlightCell != (star.I, star.J, star.K, star.G))
        {
            _lastHighlightCell = (star.I, star.J, star.K, star.G);
            _cellHighlight.Position = GalaxyVec.From(_geometry.GalaxyCenter(star.G));
            _cellHighlight.Mesh = SegmentsMesh(
                _geometry.CellWireframe(star.I, star.J, star.K), _cellHighlightMaterial);
        }
        float distance = ShipPosition.DistanceTo(star.Position);
        float alpha = 0.45f * Mathf.Clamp(distance / (1.2f * GalaxyLayout.S), 0.12f, 1f);
        _cellHighlightMaterial.AlbedoColor = new Color(1f, 0.82f, 0.35f, alpha);
    }

    /// <summary>
    /// G: the sector grid — a box around the cell the SHIP is in, so you can read
    /// your own bucket while flying. Distinct from the hover/lock cell highlight,
    /// which wireframes the cell of whatever you are pointing at.
    /// </summary>
    private (int, int, int, int)? _lastSectorCell;

    private void UpdateSectorGrid()
    {
        if (!_sectorGridOn)
        {
            _sectorGridMaterial.AlbedoColor = new Color(0.42f, 0.72f, 0.62f, 0f);
            return;
        }
        Vector3 local = ShipPosition - GalaxyVec.From(_geometry.GalaxyCenter(_nearestGalaxy));
        if (_geometry.SectorOf(GalaxyVec.To(local)) is not { } sector)
        {
            // Outside the grid there is no sector to draw — fade, never cut.
            _sectorGridMaterial.AlbedoColor = new Color(0.42f, 0.72f, 0.62f, 0f);
            return;
        }
        if (_lastSectorCell != (sector.I, sector.J, sector.K, _nearestGalaxy))
        {
            _lastSectorCell = (sector.I, sector.J, sector.K, _nearestGalaxy);
            _sectorGrid.Position = GalaxyVec.From(_geometry.GalaxyCenter(_nearestGalaxy));
            _sectorGrid.Mesh = SegmentsMesh(
                _geometry.CellWireframe(sector.I, sector.J, sector.K), _sectorGridMaterial);
        }
        _sectorGridMaterial.AlbedoColor = new Color(0.42f, 0.72f, 0.62f, 0.22f);
    }

    private void BuildCellHighlight()
    {
        _cellHighlightMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.82f, 0.35f, 0f),
            DisableReceiveShadows = true,
        };
        _cellHighlight = new MeshInstance3D { Name = "CellHighlight" };
        _world.AddChild(_cellHighlight);

        _sectorGridMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.42f, 0.72f, 0.62f, 0f),
            DisableReceiveShadows = true,
        };
        _sectorGrid = new MeshInstance3D { Name = "SectorGrid" };
        _world.AddChild(_sectorGrid);
    }

    /// <summary>Left click locks or releases; Esc releases. A click on empty space is
    /// a release, not a no-op — it is how you let go without hunting for a key.
    /// Bound to the viewport container: it has MouseFilter.Stop, so a click never
    /// reaches this Control's own _GuiInput.</summary>
    private void OnViewportGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            SetLock(_targeting.Pick(click.Position, ShipPosition, _clock));
            _viewportContainer.AcceptEvent();
        }
    }

    private void SetLock(GalaxyTarget? target)
    {
        if (target is null)
        {
            if (_lock is not null)
            {
                _hud.Toast("lock released");
            }
            _lock = null;
            return;
        }
        // Tracking owns rotation from here until the lock is released.
        _lock = target;
        _hud.Toast($"locked: {GalaxyTargeting.Describe(target)}");
        if (target.Entry is { } entry)
        {
            EntrySelected?.Invoke(entry);
        }
    }

    /// <summary>
    /// Start a warp to the locked target, else the hovered one (§6). Objects are
    /// approached to a standoff; a galaxy is entered at its nearest EDGE, because
    /// arriving at its centre would put you inside the star cloud with no bearings.
    /// </summary>
    private void TryWarp()
    {
        if (_warping || (_lock ?? _hover) is not { } target)
        {
            return;
        }
        Vector3 destination = _targeting.PositionOf(target, _clock);
        Vector3 arrival;
        if (target.Kind == GalaxyTargetKind.Galaxy)
        {
            Vector3 toShip = ShipPosition - destination;
            Vector3 direction = toShip.LengthSquared() < 1e-3f ? Vector3.Back : toShip.Normalized();
            float arrivalRadius = _geometry.GalaxyRadius + 40f;
            arrival = destination + new Vector3(
                direction.X * arrivalRadius,
                direction.Y * arrivalRadius * 0.4f,
                direction.Z * arrivalRadius);
            _hud.Toast("hyperspace");
        }
        else
        {
            arrival = destination - Forward() * StandoffFor(target);
            _hud.Toast($"warp: {GalaxyTargeting.Describe(target)}");
        }

        _warpFrom = ShipPosition;
        _warpTo = arrival;
        _warpTarget = target;
        _warpElapsed = 0f;
        _warpDuration = GalaxyNavigation.WarpDuration(ShipPosition.DistanceTo(arrival));
        _warping = true;
        _ship.Halt();
    }

    /// <summary>
    /// Advance a running warp. Position is eased; a planet destination is re-aimed
    /// every frame so the ship lands on the moving body, not where it used to be.
    /// Detail at the destination is already fading in during the approach — the fade
    /// bands do that on their own, since they key off camera distance every frame.
    /// </summary>
    private void UpdateWarp(float delta)
    {
        if (!_warping)
        {
            return;
        }
        _warpElapsed += delta;
        float t = _warpDuration <= 0f ? 1f : Mathf.Clamp(_warpElapsed / _warpDuration, 0f, 1f);

        if (_warpTarget is { Kind: GalaxyTargetKind.Planet } moving)
        {
            _warpTo = _targeting.PositionOf(moving, _clock) - Forward() * StandoffFor(moving);
        }
        _ship.Position = GalaxyVec.To(_warpFrom.Lerp(_warpTo, GalaxyNavigation.WarpEase(t)));
        ApplyShipToRig();

        if (t < 1f)
        {
            return;
        }
        _warping = false;
        // Arriving at a galaxy releases the lock so steering returns immediately;
        // arriving at a star or planet keeps it, because you came to look at it.
        if (_warpTarget is { Kind: GalaxyTargetKind.Galaxy })
        {
            SetLock(null);
        }
        _warpTarget = null;
    }

    /// <summary>Hyperdrive segment: lock that galaxy and warp, or say so when you
    /// are already the nearest thing to it.</summary>
    private void WarpToGalaxy(int galaxy)
    {
        if (_warping)
        {
            return;
        }
        if (galaxy == _nearestGalaxy)
        {
            _hud.Toast($"already nearest galaxy {galaxy}");
            return;
        }
        SetLock(GalaxyTarget.OfGalaxy(galaxy));
        TryWarp();
    }

    /// <summary>Hyperdrive step: lock the adjacent galaxy and warp. A no-op with a
    /// toast when you are already at the end of the lane.</summary>
    private void StepGalaxy(int direction)
    {
        if (_warping)
        {
            return;
        }
        int next = _nearestGalaxy + direction;
        if (next < 0 || next >= GalaxyLayout.Bins)
        {
            _hud.Toast("edge of the universe");
            return;
        }
        SetLock(GalaxyTarget.OfGalaxy(next));
        TryWarp();
    }

    private Vector3 Forward() => GalaxyVec.From(_ship.Forward);

    private static float StandoffFor(GalaxyTarget target) =>
        target.Kind == GalaxyTargetKind.Planet
            ? GalaxyNavigation.PlanetStandoff
            : GalaxyNavigation.StarStandoff;

    /// <summary>Gather pilot intent and advance the flight model (§4). Mouse steers
    /// from the cursor's offset to the viewport centre; keys translate.</summary>
    private void Fly(float delta)
    {
        if (_warping)
        {
            // A warp owns the ship outright: no thrust, no steering, no tether —
            // otherwise velocity accumulates under the ease and the ship shoots off
            // the moment it arrives.
            _hud.SteerCursor = null;
            return;
        }
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
        float nearRadius = _geometry.GalaxyRadius * 2f;
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            float distance = ShipPosition.DistanceTo(GalaxyVec.From(_geometry.GalaxyCenter(g)));

            // Halo ramps IN as you leave a galaxy, so it never blinks on at the edge.
            float presence = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.4f * nearRadius, zeroAt: 0.8f * nearRadius);
            // ...and out again at the far end of the lane, where eight labels would
            // otherwise stack on the horizon.
            float reach = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.5f * GalaxyLayout.Gap, zeroAt: 2.4f * GalaxyLayout.Gap);

            // Additive, and deliberately faint: this is a marker saying "a galaxy is
            // over there", not a light source. At 0.55 it washed out every star in
            // front of it. NOT faded by distance — only the LABEL culls at range
            // (§8.1); a halo that dims with distance makes the far end of the lane
            // empty, which §8.1.1 tests against, so the sprite shrinking on screen is
            // the only distance cue it gets.
            float haloAlpha = presence * 0.20f;
            _haloMaterials[g].AlbedoColor = new Color(0.62f, 0.70f, 0.95f, haloAlpha);
            _names[g].Modulate = new Color(1f, 1f, 1f, presence * reach);

            float boxAlpha = GalaxyNavigation.FadeBand(distance,
                fullAt: 1.6f * _geometry.GalaxyRadius, zeroAt: 5f * _geometry.GalaxyRadius) * 0.28f;
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
        _statusLine.Text = _planets.Count > 0
            ? $"{_snapshot.StatusLine} · {_stars.Count} STARS · {_planets.Count} PLANETS"
            : $"{_snapshot.StatusLine} · {_stars.Count} STARS";
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
        _viewportContainer.GuiInput += OnViewportGuiInput;
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

        _dashboard = new GalaxyDashboard { Name = "Dashboard" };
        _dashboard.GalaxyRequested += WarpToGalaxy;
        _dashboard.GalaxyStepRequested += StepGalaxy;
        column.AddChild(_dashboard);

        var statusRow = new HBoxContainer();
        statusRow.AddThemeConstantOverride("separation", 10);
        column.AddChild(statusRow);
        _statusLine = UiWidgets.MakeLabel("", 12);
        _statusLine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        statusRow.AddChild(_statusLine);
        _gridToggle = new Button
        {
            Text = "GRID: CUBE",
            TooltipText = "SWAP BETWEEN THE CUBE GRID AND THE SPHERICAL/RADIAL EXPERIMENT",
        };
        _gridToggle.AddThemeFontSizeOverride("font_size", 11);
        _gridToggle.Pressed += () => SetGeometry(
            _geometry.Name == "cube"
                ? new RadialGalaxyGeometry()
                : new CubeGalaxyGeometry(),
            announce: true);
        statusRow.AddChild(_gridToggle);

        // Deliberately NOT wired to Resized: the signal last fires while the tab is
        // hidden and the SubViewport still has a placeholder size, and it never
        // re-fires when the tab is clicked open — the stale value then inflates the
        // minimum-screen-size clamp by orders of magnitude and every star renders as
        // a giant sphere (designer report, 2026-09-17; reproduced with
        // BRAWLER_GALAXY_TAB_AT). _Process tracks the real size instead.
        // The dashboard's preview is a live mini-sim; leaving the tab stops it.
        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree())
            {
                _dashboard.StopPreview();
            }
        };
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
        _planets = new GalaxyPlanets { Name = "Planets" };
        _world.AddChild(_planets);
        _targeting = new GalaxyTargeting(_camera, _stars, _planets) { Geometry = _geometry };
        _stars.Geometry = _geometry;
        BuildCellHighlight();

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
            Vector3 center = GalaxyVec.From(_geometry.GalaxyCenter(g));

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
                    // Sized so the falloff's visible disc lands on the galaxy itself
                    // (3.4 x its bounding radius; the visible third of the sprite).
                    // History: 2.8x the cube edge was a ~2,000-unit billboard you
                    // flew THROUGH on the way in and it fogged everything.
                    Size = Vector2.One * (_geometry.GalaxyRadius * 3.4f),
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
                Position = center + new Vector3(0f, _geometry.GalaxyRadius * 1.15f, 0f),
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
                Position = center,
                Mesh = SegmentsMesh(_geometry.BoundsWireframe(), _boundsMaterials[g]),
            };
            _world.AddChild(_bounds[g]);
        }
    }

    /// <summary>Tear down and rebuild the per-galaxy markers — the grid toggle moves
    /// every centre and changes the bounds shape (cube edges vs great-circle rings).</summary>
    private void RebuildGalaxyMarkers()
    {
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            _halos[g].QueueFree();
            _names[g].QueueFree();
            _bounds[g].QueueFree();
        }
        BuildGalaxyMarkers();
    }

    /// <summary>Line segments (galaxy-local) as an ImmediateMesh.</summary>
    private static ImmediateMesh SegmentsMesh(
        System.Collections.Generic.IReadOnlyList<(BrawlerSim.Hyperspace.GalaxyPoint A, BrawlerSim.Hyperspace.GalaxyPoint B)> segments,
        Material material)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        foreach ((BrawlerSim.Hyperspace.GalaxyPoint a, BrawlerSim.Hyperspace.GalaxyPoint b) in segments)
        {
            mesh.SurfaceAddVertex(GalaxyVec.From(a));
            mesh.SurfaceAddVertex(GalaxyVec.From(b));
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    /// <summary>The body shader's screen-size clamps are in world units, so they
    /// need the viewport's real pixel scale. Checked every frame (one property read;
    /// the uniforms are only written when the height actually changes) and pushed to
    /// BOTH body materials — the planets' material previously never received it and
    /// ran on the shader default.</summary>
    private void UpdateProjectionUniforms()
    {
        int height = Mathf.Max(1, _viewport.Size.Y);
        if (height == _uniformHeight)
        {
            return;
        }
        _uniformHeight = height;
        float worldPerPixel = GalaxyShaders.WorldPerPixel(CameraFov, height);
        _stars.Material.SetShaderParameter("world_per_pixel", worldPerPixel);
        _planets.BodyMaterial.SetShaderParameter("world_per_pixel", worldPerPixel);
    }

    private int _uniformHeight;
}
