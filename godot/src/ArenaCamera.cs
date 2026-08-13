using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Player-framing camera (2026-07-21, Map Size — docs/features/map-size.md; reworked
/// 2026-08-13, designer: Smash-style rules). Zooms to the LIVE PLAYERS — platforms may
/// leave the frame — with the players fitted into the USABLE part of the screen: the
/// region above the bottom HUD band (BottomUiPixels, 0 for HUD-less hosts like the
/// evolve preview). Hard rules: the FULL view — including the strip behind the HUD
/// panels, whose gaps show the world — never extends past the kill (blast) box, so a
/// kill line is never on screen and characters never vanish mid-screen; the zoom never
/// goes tighter than the legacy framing (10 world units tall — the legibility floor).
/// Generation guarantees the complementary half (DEVIATIONS #33): every platform sits
/// inside the kill box with the floor clear of the HUD band at the widest legal zoom.
/// Purely cosmetic — reads sim state only.
/// </summary>
public partial class ArenaCamera : Camera2D
{
    /// <summary>Legacy framing half height: ortho size 5 (720 px at 72 ppu).</summary>
    private const float MinHalfHeight = 5f;

    /// <summary>World-unit margin kept around each player.</summary>
    private const float FramingMargin = 3f;

    /// <summary>Exponential smoothing rate (per second) for pan/zoom.</summary>
    private const float SmoothRate = 6f;

    /// <summary>Height (design pixels) of the bottom band covered by GUI panels.
    /// Players are framed into the region ABOVE this band (designer, 2026-08-13:
    /// GUIs don't count as screen space). Set by ArenaView from the HUD's live
    /// layout; stays 0 for HUD-less hosts (the evolve-menu match preview).</summary>
    public float BottomUiPixels { get; set; }

    private SimWorld _world = null!;
    private float _ppu;
    private Vector2 _center;   // world units (full view center)
    private float _halfHeight; // world units (full view half height)

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

    /// <summary>The world-space rect of the USABLE (not HUD-covered) screen region —
    /// what the player can actually see; death flashes anchor to its edges.</summary>
    public BrawlerSim.Sim.Aabb UsableWorldRect
    {
        get
        {
            float aspect = Aspect();
            float f = UsableFraction();
            return new BrawlerSim.Sim.Aabb(
                new BrawlerSim.Determinism.Vec2(_center.X, _center.Y + _halfHeight * (1f - f)),
                new BrawlerSim.Determinism.Vec2(_halfHeight * aspect, _halfHeight * f));
        }
    }

    /// <summary>Fraction of the view height NOT covered by the bottom GUI band.</summary>
    public float UsableFraction()
    {
        float viewH = GetViewportRect().Size.Y;
        return viewH > 0f ? Mathf.Clamp((viewH - BottomUiPixels) / viewH, 0.3f, 1f) : 1f;
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
        // Frame the bounding box of every player still IN the match, bounded by the
        // kill box (a dying player never drags the camera toward a kill line).
        // Eliminated players drop out; blacked-out ones are parked at their spawn
        // point, which the old camera also framed.
        var blast = _world.BlastZone.Half;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        int framed = 0;
        foreach (SimPlayer player in _world.Players)
        {
            if (player.Eliminated)
            {
                continue;
            }
            minX = Mathf.Min(minX, Mathf.Max(player.Position.X - FramingMargin, -blast.X));
            maxX = Mathf.Max(maxX, Mathf.Min(player.Position.X + FramingMargin, blast.X));
            minY = Mathf.Min(minY, Mathf.Max(player.Position.Y - FramingMargin, -blast.Y));
            maxY = Mathf.Max(maxY, Mathf.Min(player.Position.Y + FramingMargin, blast.Y));
            framed++;
        }
        if (framed == 0)
        {
            minX = maxX = minY = maxY = 0f; // everyone gone (match over) — hold center
        }
        float aspect = Aspect();
        float f = UsableFraction();

        // Fit the players into the USABLE region: full view width, the top f of the
        // view height (the rest hides behind the HUD band). Hard ceiling: the FULL
        // view must stay inside the kill box — the inter-panel gaps reveal the strip
        // behind the HUD, so that strip must be in-box too (designer). Players who
        // outrun the ceiling are near a kill line and may leave the visible region.
        float needHalfW = (maxX - minX) * 0.5f;
        float needHalfH = (maxY - minY) * 0.5f;
        float halfH = Mathf.Max(MinHalfHeight, Mathf.Max(needHalfH / f, needHalfW / aspect));
        float maxHalfH = Mathf.Min(blast.Y, blast.X / aspect);
        halfH = Mathf.Min(halfH, maxHalfH);
        float halfW = halfH * aspect;

        // Aim the USABLE region's center at the players' midpoint, then clamp the
        // FULL view inside the kill box (degenerate axes center on 0). The full-view
        // center sits LOWER than the usable center by the hidden band's half height.
        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f - halfH * (1f - f);
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
