using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Player-framing camera (2026-07-21, Map Size — docs/features/map-size.md). Centers
/// on the players' midpoint and zooms so both are on screen with margin. Designer
/// rules: the view never shows beyond the KO (blast) box, never zooms in tighter than
/// the legacy framing (10 world units tall — the legibility floor), and never zooms
/// out past what the blast box allows. On a legacy-size map with players mid-stage the
/// framing equals the pre-feature fixed view. Purely cosmetic — reads sim state only.
/// </summary>
public partial class ArenaCamera : Camera2D
{
    /// <summary>Legacy framing half height: ortho size 5 (720 px at 72 ppu).</summary>
    private const float MinHalfHeight = 5f;

    /// <summary>World-unit margin kept around each player.</summary>
    private const float FramingMargin = 3f;

    /// <summary>Exponential smoothing rate (per second) for pan/zoom.</summary>
    private const float SmoothRate = 6f;

    private SimWorld _world = null!;
    private float _ppu;
    private Vector2 _center;   // world units
    private float _halfHeight; // world units

    /// <summary>The world-space rect the camera currently shows (for the minimap).</summary>
    public BrawlerSim.Sim.Aabb ViewWorldRect
    {
        get
        {
            float aspect = Aspect();
            return new BrawlerSim.Sim.Aabb(
                new BrawlerSim.Determinism.Vec2(_center.X, _center.Y),
                new BrawlerSim.Determinism.Vec2(_halfHeight * aspect, _halfHeight));
        }
    }

    public void Setup(SimWorld world, float ppu)
    {
        _world = world;
        _ppu = ppu;
        (_center, _halfHeight) = Target();
        Apply();
        MakeCurrent();
    }

    public void Sync(float delta)
    {
        (Vector2 targetCenter, float targetHalfH) = Target();
        float t = 1f - Mathf.Exp(-SmoothRate * delta);
        _center = _center.Lerp(targetCenter, t);
        _halfHeight = Mathf.Lerp(_halfHeight, targetHalfH, t);
        Apply();
    }

    private float Aspect()
    {
        Vector2 size = GetViewportRect().Size;
        return size.Y > 0f ? size.X / size.Y : 16f / 9f;
    }

    private (Vector2 Center, float HalfHeight) Target()
    {
        var p0 = _world.Players[0].Position;
        var p1 = _world.Players[1].Position;
        float aspect = Aspect();

        float needHalfW = Mathf.Abs(p0.X - p1.X) * 0.5f + FramingMargin;
        float needHalfH = Mathf.Abs(p0.Y - p1.Y) * 0.5f + FramingMargin;
        float halfH = Mathf.Max(MinHalfHeight, Mathf.Max(needHalfH, needHalfW / aspect));

        // Hard ceiling: the whole view must fit inside the blast (KO) box.
        var blast = _world.BlastZone.Half;
        float maxHalfH = Mathf.Min(blast.Y, blast.X / aspect);
        halfH = Mathf.Min(halfH, maxHalfH);
        float halfW = halfH * aspect;

        // Center on the midpoint, clamped so the view stays inside the blast box.
        float cx = (p0.X + p1.X) * 0.5f;
        float cy = (p0.Y + p1.Y) * 0.5f;
        cx = blast.X - halfW <= 0f ? 0f : Mathf.Clamp(cx, -(blast.X - halfW), blast.X - halfW);
        cy = blast.Y - halfH <= 0f ? 0f : Mathf.Clamp(cy, -(blast.Y - halfH), blast.Y - halfH);
        return (new Vector2(cx, cy), halfH);
    }

    private void Apply()
    {
        Position = new Vector2(_center.X * _ppu, -_center.Y * _ppu);
        float zoom = GetViewportRect().Size.Y * 0.5f / (_halfHeight * _ppu);
        Zoom = new Vector2(zoom, zoom);
    }
}
