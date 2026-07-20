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

    private SimWorld _world = null!;
    private float _ppu;
    private readonly List<Polygon2D> _pool = new();
    private readonly List<SimProjectileMove?> _poolMove = new();

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
            var poly = new Polygon2D { Color = Gold };
            AddChild(poly);
            _pool.Add(poly);
            _poolMove.Add(null);
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            Polygon2D poly = _pool[i];
            if (i >= projectiles.Count)
            {
                poly.Visible = false;
                continue;
            }
            SimProjectile proj = projectiles[i];
            if (!ReferenceEquals(_poolMove[i], proj.Move))
            {
                poly.Polygon = ShapePoints(proj.Move);
                _poolMove[i] = proj.Move;
            }
            poly.Visible = true;
            poly.Position = new Vector2(proj.Position.X * _ppu, -proj.Position.Y * _ppu);
            poly.Rotation = -proj.Angle; // sim CCW (y-up) → Godot CW (y-down)
            float alpha = proj.Move.DamageDecay ? 0.2f + 0.8f * proj.DamageScale : 1f;
            poly.Color = new Color(Gold.R, Gold.G, Gold.B, alpha);
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
