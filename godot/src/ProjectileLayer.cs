using Godot;
using System.Collections.Generic;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Renders SimWorld.Projectiles (2026-07-14, docs/features/projectiles.md): FILLED
/// shapes in the dedicated projectile GOLD — visually distinct from the shield's
/// white→red OUTLINE circle even when both are circles (designer clarification).
/// Rotation mirrors the sim angle; damage decay fades toward transparent (spec:
/// non-decaying projectiles stay solid). A simple pool keyed by list index — the
/// sim list is spawn-ordered and compacted, so index reuse is stable within a frame.
/// </summary>
public partial class ProjectileLayer : Node2D
{
    private static readonly Color Gold = new(1.0f, 0.8f, 0.25f);

    // Slight motion trail (2026-07-23, designer): a few faint gold afterimages per
    // bolt — less pronounced than the player trail (fixed low alphas, short) and
    // purely cosmetic: hitboxes/hurtboxes are sim-side and untouched by rendering.
    private const int GhostsPerBolt = 3;
    private const int GhostStride = 2; // frames between afterimage samples
    private static readonly float[] GhostAlpha = { 0.28f, 0.16f, 0.07f };

    private SimWorld _world = null!;
    private float _ppu;
    private readonly List<Polygon2D> _pool = new();
    private readonly List<SimProjectileMove?> _poolMove = new();
    private readonly List<SimProjectile?> _poolProj = new();
    private readonly List<Polygon2D[]> _ghostPool = new();
    private readonly List<List<(Vector2 Pos, float Rot)>> _history = new();

    public void Setup(SimWorld world, float ppu)
    {
        _world = world;
        _ppu = ppu;
    }

    public void Sync()
    {
        IReadOnlyList<SimProjectile> projectiles = _world.Projectiles;
        while (_pool.Count < projectiles.Count)
        {
            var ghosts = new Polygon2D[GhostsPerBolt];
            for (int g = 0; g < GhostsPerBolt; g++)
            {
                ghosts[g] = new Polygon2D { Color = Gold, Visible = false, ZIndex = -1 };
                AddChild(ghosts[g]);
            }
            _ghostPool.Add(ghosts);
            _history.Add(new List<(Vector2, float)>());
            var poly = new Polygon2D { Color = Gold };
            AddChild(poly);
            _pool.Add(poly);
            _poolMove.Add(null);
            _poolProj.Add(null);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            Polygon2D poly = _pool[i];
            if (i >= projectiles.Count)
            {
                poly.Visible = false;
                _poolProj[i] = null;
                _history[i].Clear();
                foreach (Polygon2D g in _ghostPool[i])
                {
                    g.Visible = false;
                }
                continue;
            }
            SimProjectile proj = projectiles[i];
            if (!ReferenceEquals(_poolMove[i], proj.Move))
            {
                poly.Polygon = ShapePoints(proj.Move);
                foreach (Polygon2D g in _ghostPool[i])
                {
                    g.Polygon = poly.Polygon;
                }
                _poolMove[i] = proj.Move;
            }
            // Slot reuse (the sim list compacts) starts a fresh trail.
            if (!ReferenceEquals(_poolProj[i], proj))
            {
                _poolProj[i] = proj;
                _history[i].Clear();
            }
            poly.Visible = true;
            poly.Position = new Vector2(proj.Position.X * _ppu, -proj.Position.Y * _ppu);
            poly.Rotation = -proj.Angle; // sim CCW (y-up) → Godot CW (y-down)
            float alpha = proj.Move.DamageDecay ? 0.2f + 0.8f * proj.DamageScale : 1f;
            // Reflect flash (2026-07-20, designer): a just-reflected bolt strobes
            // toward white for ~a quarter second — the "it's coming BACK" read.
            int sinceReflect = proj.ReflectTick < 0 ? int.MaxValue : _world.TickCount - proj.ReflectTick;
            Color color = sinceReflect < 14 && sinceReflect / 3 % 2 == 0
                ? new Color(1f, 1f, 1f)
                : Gold;
            poly.Color = new Color(color.R, color.G, color.B, alpha);
            SyncTrail(i, poly, alpha);
        }
    }

    /// <summary>The bolt's faint afterimages: sampled per frame, one ghost every
    /// GhostStride samples, alphas fixed low so the trail reads as a comet tail,
    /// never a second bolt.</summary>
    private void SyncTrail(int i, Polygon2D poly, float boltAlpha)
    {
        List<(Vector2 Pos, float Rot)> history = _history[i];
        history.Insert(0, (poly.Position, poly.Rotation));
        int keep = GhostsPerBolt * GhostStride + 1;
        if (history.Count > keep)
        {
            history.RemoveAt(history.Count - 1);
        }
        Polygon2D[] ghosts = _ghostPool[i];
        for (int g = 0; g < GhostsPerBolt; g++)
        {
            int sample = (g + 1) * GhostStride;
            if (sample >= history.Count)
            {
                ghosts[g].Visible = false;
                continue;
            }
            ghosts[g].Visible = true;
            ghosts[g].Position = history[sample].Pos;
            ghosts[g].Rotation = history[sample].Rot;
            ghosts[g].Color = new Color(Gold.R, Gold.G, Gold.B, GhostAlpha[g] * boltAlpha);
        }
    }

    private Vector2[] ShapePoints(SimProjectileMove move)
    {
        float h = move.HalfExtent * _ppu;
        switch (move.Shape)
        {
            case ProjectileShape.Circle:
                var circle = new Vector2[20];
                for (int k = 0; k < circle.Length; k++)
                {
                    float a = Mathf.Tau * k / circle.Length;
                    circle[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h;
                }
                return circle;
            case ProjectileShape.Triangle:
                var triangle = new Vector2[3];
                for (int k = 0; k < 3; k++)
                {
                    float a = Mathf.Tau * k / 3f;
                    triangle[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h;
                }
                return triangle;
            default:
                return new[]
                {
                    new Vector2(-h, -h), new Vector2(h, -h),
                    new Vector2(h, h), new Vector2(-h, h),
                };
        }
    }
}
