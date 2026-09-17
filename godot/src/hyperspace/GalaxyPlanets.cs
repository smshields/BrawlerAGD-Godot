using System.Collections.Generic;
using Godot;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>One planet: an archive member of its star's cell, on its own orbit.</summary>
public sealed record GalaxyPlanet(
    GalaxyStar Star, int Index, OrbitElements Orbit, Color Color, HyperspaceEntry Entry)
{
    public Vector3 PositionAt(float t) => GalaxyVec.From(
        GalaxyLayout.PlanetPosition(GalaxyVec.To(Star.Position), Orbit, t));
}

/// <summary>
/// The planets and their orbit rings (galaxy-view.md §3). Since the 2026-09-16
/// designer round these are REAL data — a cell's other occupants from CellReservoir,
/// not the PoC's mock orbiters — so every planet is a game you can watch and save.
///
/// ORBIT RINGS ARE REQUIRED, NOT DECORATION. In a dense field the ring is what says
/// which planet belongs to which star, and it is why orbital planes are randomized:
/// once separation comes from ORIENTATION rather than radius, a system can hold many
/// planets without its rings nesting into an indistinguishable disc.
///
/// Rings are one shared unit-circle mesh instanced with a per-orbit transform — the
/// ellipse is an affine image of a circle, with the star at a FOCUS (offset along the
/// major axis), so nothing is rebuilt per frame.
/// </summary>
public sealed partial class GalaxyPlanets : Node3D
{
    private const int RingSegments = 48;
    private const int MaxVisibleBodies = 256;
    private const int MaxVisibleRings = 256;

    private MultiMeshInstance3D _bodies = null!;
    private MultiMeshInstance3D _rings = null!;
    private ShaderMaterial _bodyMaterial = null!;

    /// <summary>The view pushes the per-frame projection uniforms here — planet
    /// bodies share the star shader and need the same real pixel scale.</summary>
    public ShaderMaterial BodyMaterial => _bodyMaterial;
    private StandardMaterial3D _ringMaterial = null!;

    /// <summary>Every planet in the archive, precomputed at snapshot time: orbits are
    /// pure functions of (cell hash, member index), so they never need recomputing.</summary>
    private readonly List<GalaxyPlanet> _planets = new();

    /// <summary>Planets grouped by their star as (star, first index, count), so the
    /// per-frame pass pays one distance test per SYSTEM and skips out-of-range
    /// systems wholesale — the flat per-planet walk grew with the archive
    /// (2026-09-17 performance round).</summary>
    private readonly List<(GalaxyStar Star, int First, int Count)> _systems = new();

    /// <summary>What the last frame actually drew — the pick list for targeting.</summary>
    private readonly List<(GalaxyPlanet Planet, Vector3 Position, float Alpha)> _visible = new();

    public IReadOnlyList<(GalaxyPlanet Planet, Vector3 Position, float Alpha)> Visible => _visible;

    public int Count => _planets.Count;

    /// <summary>Star or planet currently hovered/locked — its ring brightens.</summary>
    public GalaxyStar? HighlightStar { get; set; }
    public GalaxyPlanet? HighlightPlanet { get; set; }

    /// <summary>P toggles the planet layer off for an uncluttered look (§4).</summary>
    public bool Enabled { get; set; } = true;

    public override void _Ready()
    {
        _bodyMaterial = new ShaderMaterial { Shader = GalaxyShaders.Star() };
        // Same opaque sphere the stars wear, but lit DIRECTIONALLY: a fixed key
        // light with a terminator, so a planet reads as a lit ball against its
        // self-luminous star. No distance fade of its own (the range band below owns
        // that); the shader's screen clamps keep a planet you warp to from filling
        // the view.
        _bodyMaterial.SetShaderParameter("sprite_scale", GalaxyShaders.SpriteScale);
        _bodyMaterial.SetShaderParameter("min_pixels", 1.3f);
        _bodyMaterial.SetShaderParameter("max_pixels", 60f);
        _bodyMaterial.SetShaderParameter("fade_numerator", 1e9f);
        _bodyMaterial.SetShaderParameter("shade_directional", 1f);

        _bodies = new MultiMeshInstance3D
        {
            Name = "Bodies",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = new QuadMesh { Size = Vector2.One, Material = _bodyMaterial },
                InstanceCount = MaxVisibleBodies,
                VisibleInstanceCount = 0,
            },
        };
        AddChild(_bodies);

        _ringMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            DisableReceiveShadows = true,
            NoDepthTest = false,
        };
        _rings = new MultiMeshInstance3D
        {
            Name = "Rings",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = UnitCircle(_ringMaterial),
                InstanceCount = MaxVisibleRings,
                VisibleInstanceCount = 0,
            },
        };
        AddChild(_rings);
    }

    /// <summary>Precompute every system's orbits from the snapshot's members.</summary>
    public void Rebuild(GalaxyStarField field, FitnessScale scale)
    {
        _planets.Clear();
        _systems.Clear();
        _visible.Clear();
        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            foreach (GalaxyStar star in field.StarsIn(g))
            {
                IReadOnlyList<HyperspaceEntry> members = star.Entry.Occupants;
                if (members.Count > 0)
                {
                    _systems.Add((star, _planets.Count, members.Count));
                }
                for (int n = 0; n < members.Count; n++)
                {
                    // The member's own PERCENTILE fitness drives its size (length-
                    // scaled: radius linear in it) and its habitability colour.
                    // The first version clamped the RAW score to [0,1] — a raw score
                    // is ~40-120, so every planet rendered at max size and the
                    // encoding never existed. Normalize through the same scale the
                    // stars use, so bodies are comparable across the whole sky.
                    float fitness = scale.Normalize(members[n].Fitness);
                    OrbitElements orbit = GalaxyLayout.Orbit(
                        star.Hash, n, members.Count, star.Radius, fitness);
                    _planets.Add(new GalaxyPlanet(star, n, orbit,
                        GalaxyVec.From(GalaxyLayout.PlanetColor(fitness)),
                        members[n]));
                }
            }
        }
    }

    /// <summary>
    /// Place the near-field planets for this frame. Everything is a fade band on
    /// camera distance (§8.1) — planets and rings are the PoC's worst pop-in
    /// offender, so they ramp out well before the range limit and are never switched.
    /// </summary>
    public void Update(Vector3 eye, float clock)
    {
        _visible.Clear();
        MultiMesh bodies = _bodies.Multimesh;
        MultiMesh rings = _rings.Multimesh;
        if (!Enabled)
        {
            bodies.VisibleInstanceCount = 0;
            rings.VisibleInstanceCount = 0;
            return;
        }

        int bodyCount = 0, ringCount = 0;
        foreach ((GalaxyStar star, int first, int count) in _systems)
        {
            float distance = eye.DistanceTo(star.Position);
            float band = GalaxyNavigation.FadeBand(distance,
                fullAt: 0.62f * GalaxyLayout.PlanetRange, zeroAt: GalaxyLayout.PlanetRange);
            if (band <= 0.001f)
            {
                continue;
            }

            for (int p = first; p < first + count; p++)
            {
                GalaxyPlanet planet = _planets[p];
                Vector3 position = planet.PositionAt(clock);
                if (bodyCount < MaxVisibleBodies)
                {
                    bodies.SetInstanceTransform(bodyCount, new Transform3D(
                        Basis.Identity.Scaled(Vector3.One * planet.Orbit.BodyRadius), position));
                    bodies.SetInstanceColor(bodyCount, planet.Color);
                    // .x rides at 1 (fitness is baked into the body colour); .y is
                    // the range band, a brightness ramp on the opaque body.
                    bodies.SetInstanceCustomData(bodyCount, new Color(1f, band, 0f, 0f));
                    bodyCount++;
                    _visible.Add((planet, position, band));
                }

                if (ringCount < MaxVisibleRings)
                {
                    bool highlighted = ReferenceEquals(HighlightStar, star)
                        || ReferenceEquals(HighlightPlanet, planet);
                    float alpha = (highlighted ? 0.6f : 0.16f) * band;
                    rings.SetInstanceTransform(ringCount, RingTransform(planet));
                    rings.SetInstanceColor(ringCount, new Color(
                        star.Color.R, star.Color.G, star.Color.B, alpha));
                    ringCount++;
                }
            }
        }
        bodies.VisibleInstanceCount = bodyCount;
        rings.VisibleInstanceCount = ringCount;
    }

    /// <summary>
    /// The affine map from the unit circle to this orbit: major axis along the plane's
    /// u by the semi-major axis, minor along v by the semi-minor, and the whole thing
    /// offset so the STAR sits at a focus rather than the centre — which is most of
    /// what makes an ellipse read as an orbit.
    /// </summary>
    private static Transform3D RingTransform(GalaxyPlanet planet)
    {
        OrbitElements orbit = planet.Orbit;
        float a = orbit.SemiMajor;
        float b = a * Mathf.Sqrt(Mathf.Max(0f, 1f - orbit.Eccentricity * orbit.Eccentricity));
        Vector3 u = GalaxyVec.From(orbit.PlaneU);
        Vector3 v = GalaxyVec.From(orbit.PlaneV);
        Vector3 normal = u.Cross(v).Normalized();
        var basis = new Basis(u * a, v * b, normal);
        Vector3 center = planet.Star.Position - u * (a * orbit.Eccentricity);
        return new Transform3D(basis, center);
    }

    private static ArrayMesh UnitCircle(Material material)
    {
        var immediate = new ImmediateMesh();
        immediate.SurfaceBegin(Mesh.PrimitiveType.LineStrip, material);
        for (int i = 0; i <= RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.Tau;
            immediate.SurfaceAddVertex(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }
        immediate.SurfaceEnd();

        // Bake to an ArrayMesh: a MultiMesh cannot instance an ImmediateMesh.
        var baked = new ArrayMesh();
        baked.AddSurfaceFromArrays(Mesh.PrimitiveType.LineStrip,
            immediate.SurfaceGetArrays(0));
        baked.SurfaceSetMaterial(0, material);
        return baked;
    }
}
