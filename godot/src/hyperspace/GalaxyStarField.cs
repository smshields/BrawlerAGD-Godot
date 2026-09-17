using System.Collections.Generic;
using Godot;
using BrawlerSim.Evolution;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>One star as the view holds it: where it is, how it looks, and the archive
/// entry it stands for. Planets hang off Entry.Occupants.</summary>
public sealed record GalaxyStar(
    int Cell, int I, int J, int K, int G,
    Vector3 Position, float Radius, Color Color, float Fitness, ulong Hash,
    HyperspaceEntry Entry);

/// <summary>
/// Builds and maintains the star MultiMeshes — one per galaxy so each can carry its
/// own explicit AABB (§8.1.2: Godot frustum-culls a MultiMeshInstance3D as a single
/// unit, and an auto-computed AABB from a partially filled buffer blinks a whole
/// galaxy out when its centre leaves the frustum).
///
/// Buffer writes happen once per SNAPSHOT, never per frame: distance fade lives in
/// the shader (GalaxyLayout.StarFade is its tested C# twin — the LightRig.TintTile
/// precedent), so flying around costs nothing. The only per-frame writes are the
/// handful of instances mid-ignition after a rebuild.
/// </summary>
public sealed partial class GalaxyStarField : Node3D
{
    /// <summary>Seconds a newly filled cell takes to ignite, and a replaced elite to
    /// cross-fade (§8.1.7) — a star must never blink into existence.</summary>
    public const float IgnitionSeconds = 0.4f;

    private readonly MultiMeshInstance3D[] _galaxies = new MultiMeshInstance3D[GalaxyLayout.Bins];
    private readonly List<GalaxyStar>[] _stars = new List<GalaxyStar>[GalaxyLayout.Bins];

    /// <summary>Cell index -> (galaxy, instance) so a rebuild can find what moved.</summary>
    private readonly Dictionary<int, (int Galaxy, int Instance)> _index = new();

    /// <summary>Instances still ramping in: (galaxy, instance, seconds elapsed).</summary>
    private readonly List<(int Galaxy, int Instance, float Elapsed)> _igniting = new();

    /// <summary>The star material — GalaxyView sets the projection-dependent
    /// uniforms on it (the minimum-screen-size clamp is in world units).</summary>
    public ShaderMaterial Material { get; private set; } = null!;

    public IReadOnlyList<GalaxyStar> StarsIn(int galaxy) => _stars[galaxy];

    public int Count { get; private set; }

    public override void _Ready()
    {
        Material = new ShaderMaterial { Shader = GalaxyShaders.Star() };
        Material.SetShaderParameter("halo", GalaxyShaders.HaloTexture());
        Material.SetShaderParameter("fade_numerator", GalaxyLayout.FadeNumerator);
        Material.SetShaderParameter("sprite_scale", GalaxyShaders.SpriteScale);
        Material.SetShaderParameter("core_fraction", GalaxyShaders.CoreFraction);
        Material.SetShaderParameter("min_pixels", GalaxyShaders.MinStarPixels);
        Material.SetShaderParameter("max_pixels", GalaxyShaders.MaxStarPixels);

        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            _stars[g] = new List<GalaxyStar>();
            var instance = new MultiMeshInstance3D
            {
                Name = $"Galaxy{g}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    UseCustomData = true,
                    Mesh = new QuadMesh { Size = Vector2.One, Material = Material },
                },
                // Explicit bounds: the galaxy cube plus system envelope margin, so the
                // whole galaxy is never culled as one unit (§8.1.2).
                CustomAabb = new Aabb(
                    GalaxyVec.From(GalaxyLayout.GalaxyCenter(g))
                        - Vector3.One * (GalaxyLayout.Half + GalaxyLayout.MaxSystemRadius),
                    Vector3.One * 2f * (GalaxyLayout.Half + GalaxyLayout.MaxSystemRadius)),
            };
            _galaxies[g] = instance;
            AddChild(instance);
        }
    }

    /// <summary>
    /// Rebuild from a snapshot. Positions never move for a given cell, so a star that
    /// was already there keeps its place and only ignites (new), cross-fades
    /// (replaced elite) or fades out (re-binned away) — nothing cuts.
    /// </summary>
    public void SetSnapshot(HyperspaceSnapshot? snapshot, FitnessScale scale)
    {
        var previous = new Dictionary<int, int>(); // cell -> nothing, just membership
        foreach (KeyValuePair<int, (int Galaxy, int Instance)> kv in _index)
        {
            previous[kv.Key] = 0;
        }

        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            _stars[g].Clear();
        }
        _index.Clear();
        _igniting.Clear();
        Count = 0;

        if (snapshot is not null)
        {
            foreach (HyperspaceEntry entry in snapshot.Entries)
            {
                (int i, int j, int k, int g) = CellOf(snapshot.Bins, entry.Descriptor);
                float fitness = scale.Normalize(entry.Fitness);
                ulong hash = GalaxyLayout.CellHash(i, j, k, g);
                _stars[g].Add(new GalaxyStar(
                    CellIndex(i, j, k, g), i, j, k, g,
                    GalaxyVec.From(GalaxyLayout.StarPosition(i, j, k, g)),
                    GalaxyLayout.StarRadius(fitness),
                    GalaxyVec.From(GalaxyLayout.StarColor(i, j, k, fitness)),
                    fitness, hash, entry));
            }
        }

        for (int g = 0; g < GalaxyLayout.Bins; g++)
        {
            List<GalaxyStar> stars = _stars[g];
            MultiMesh mesh = _galaxies[g].Multimesh;
            mesh.InstanceCount = stars.Count;
            Count += stars.Count;
            for (int n = 0; n < stars.Count; n++)
            {
                GalaxyStar star = stars[n];
                mesh.SetInstanceTransform(n, new Transform3D(
                    Basis.Identity.Scaled(Vector3.One * star.Radius), star.Position));
                mesh.SetInstanceColor(n, star.Color);
                bool fresh = !previous.ContainsKey(star.Cell);
                mesh.SetInstanceCustomData(n, new Color(star.Fitness, fresh ? 0f : 1f, 0f, 0f));
                _index[star.Cell] = (g, n);
                if (fresh)
                {
                    _igniting.Add((g, n, 0f));
                }
            }
        }
    }

    /// <summary>Drive the ignition ramps. Only instances inside their 0.4 s window
    /// are touched — a snapshot arriving mid-flight must not rewrite the buffers.</summary>
    public override void _Process(double delta)
    {
        if (_igniting.Count == 0)
        {
            return;
        }
        for (int i = _igniting.Count - 1; i >= 0; i--)
        {
            (int galaxy, int instance, float elapsed) = _igniting[i];
            elapsed += (float)delta;
            float t = Mathf.Clamp(elapsed / IgnitionSeconds, 0f, 1f);
            MultiMesh mesh = _galaxies[galaxy].Multimesh;
            if (instance >= mesh.InstanceCount)
            {
                _igniting.RemoveAt(i);
                continue;
            }
            Color custom = mesh.GetInstanceCustomData(instance);
            mesh.SetInstanceCustomData(instance, new Color(custom.R, t, custom.B, custom.A));
            if (t >= 1f)
            {
                _igniting.RemoveAt(i);
            }
            else
            {
                _igniting[i] = (galaxy, instance, elapsed);
            }
        }
    }

    /// <summary>The bin 4-tuple a raw descriptor falls in — the galaxy view's lane
    /// (axis 4, timing) plus the three spatial axes.</summary>
    public static (int I, int J, int K, int G) CellOf(DescriptorBins bins, float[] descriptor)
    {
        int[] coords = DescriptorBins.CellCoordinates(bins.CellIndex(descriptor));
        return (coords[0], coords[1], coords[2], coords[3]);
    }

    public static int CellIndex(int i, int j, int k, int g)
    {
        int b = DescriptorBins.BinsPerAxis;
        return ((i * b + j) * b + k) * b + g;
    }
}
