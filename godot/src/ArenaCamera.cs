using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Stage-framing camera (2026-07-21, Map Size — docs/features/map-size.md; reworked
/// 2026-08-13, designer: platforms were hiding behind the HUD panels). The framed
/// content is the whole STAGE — every platform and spawn point — plus every live
/// player, and it is fitted into the USABLE part of the screen: the region above the
/// bottom HUD band (BottomUiPixels, 0 when there is no HUD, e.g. the evolve preview).
/// Designer rules: the usable view never shows beyond the KO (blast) box, never zooms
/// in tighter than the legacy framing (10 world units tall — the legibility floor),
/// and platforms/spawns stay on screen so nothing sits invisible behind the GUI.
/// The zoom now varies only when players fly BEYOND the stage bounds (knockback).
/// Purely cosmetic — reads sim state only.
/// </summary>
public partial class ArenaCamera : Camera2D
{
    /// <summary>Legacy framing half height: ortho size 5 (720 px at 72 ppu).</summary>
    private const float MinHalfHeight = 5f;

    /// <summary>World-unit margin kept around each player.</summary>
    private const float FramingMargin = 3f;

    /// <summary>World-unit margin around the static stage box (platforms + spawns).
    /// Smaller than the player margin — a platform at the screen edge is fine.</summary>
    private const float StageMargin = 1f;

    /// <summary>Exponential smoothing rate (per second) for pan/zoom.</summary>
    private const float SmoothRate = 6f;

    /// <summary>Height (design pixels) of the bottom band covered by GUI panels.
    /// The camera frames all content into the region ABOVE this band (designer,
    /// 2026-08-13: GUIs don't count as screen space). Set by ArenaView from the HUD's
    /// live layout; stays 0 for HUD-less hosts (the evolve-menu match preview).</summary>
    public float BottomUiPixels { get; set; }

    private SimWorld _world = null!;
    private float _ppu;
    private Vector2 _center;   // world units (full view center)
    private float _halfHeight; // world units (full view half height)

    // Static stage bounds (platforms + spawn points, margin included) — fixed per
    // match, computed once in Setup.
    private float _stageMinX, _stageMaxX, _stageMinY, _stageMaxY;

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

        // The stage box: every platform and every player's spawn point (padded by a
        // body's extent so a spawning character is fully on screen), plus margin.
        // NOT clipped to the blast box — generated platforms may legitimately poke
        // past the KO line (generator rule 6), and the designer's platform-visibility
        // rule says they still belong on screen.
        _stageMinX = float.MaxValue;
        _stageMaxX = float.MinValue;
        _stageMinY = float.MaxValue;
        _stageMaxY = float.MinValue;
        foreach (BrawlerSim.Sim.Aabb platform in world.Platforms)
        {
            _stageMinX = Mathf.Min(_stageMinX, platform.Left);
            _stageMaxX = Mathf.Max(_stageMaxX, platform.Right);
            _stageMinY = Mathf.Min(_stageMinY, platform.Bottom);
            _stageMaxY = Mathf.Max(_stageMaxY, platform.Top);
        }
        foreach (SimPlayer player in world.Players)
        {
            _stageMinX = Mathf.Min(_stageMinX, player.SpawnPosition.X - player.BodyHalf.X - 0.5f);
            _stageMaxX = Mathf.Max(_stageMaxX, player.SpawnPosition.X + player.BodyHalf.X + 0.5f);
            _stageMinY = Mathf.Min(_stageMinY, player.SpawnPosition.Y - player.BodyHalf.Y - 0.5f);
            _stageMaxY = Mathf.Max(_stageMaxY, player.SpawnPosition.Y + player.BodyHalf.Y + 0.5f);
        }
        _stageMinX -= StageMargin;
        _stageMaxX += StageMargin;
        _stageMinY -= StageMargin;
        _stageMaxY += StageMargin;

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

    /// <summary>Fraction of the view height NOT covered by the bottom GUI band.</summary>
    private float UsableFraction()
    {
        float viewH = GetViewportRect().Size.Y;
        return viewH > 0f ? Mathf.Clamp((viewH - BottomUiPixels) / viewH, 0.3f, 1f) : 1f;
    }

    private (Vector2 Center, float HalfHeight) Target()
    {
        // Content = the static stage box ∪ every live player (with the larger player
        // margin), all bounded by the blast box — the camera never chases a dying
        // player past the KO line. Eliminated players drop out; blacked-out ones are
        // parked at their spawn point, which the stage box already contains.
        var blast = _world.BlastZone.Half;
        float minX = _stageMinX, maxX = _stageMaxX, minY = _stageMinY, maxY = _stageMaxY;
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
        }
        float aspect = Aspect();
        float f = UsableFraction();

        // The content box must fit the USABLE region: full view width, but only the
        // top f of the view height (the rest hides behind the HUD band). Because the
        // content is bounded by the blast box, the zoom is bounded per axis by the
        // box itself. On PORTRAIT maps (blast taller than wide) fitting the stage's
        // height into a 16:9 view necessarily shows the area beyond the side KO
        // lines — the designer's platform-visibility rule (2026-08-13) outranks the
        // hidden-death preference there; landscape maps keep every edge hidden.
        float needHalfW = (maxX - minX) * 0.5f;
        float needHalfH = (maxY - minY) * 0.5f;
        float halfH = Mathf.Max(MinHalfHeight, Mathf.Max(needHalfH / f, needHalfW / aspect));
        float halfW = halfH * aspect;
        float usableHalfH = halfH * f;

        // Center the USABLE region on the content midpoint, clamped inside the HULL
        // of the blast box and the stage box — the region we are willing to reveal.
        // A stage poking past the KO line extends the hull (platform visibility
        // outranks hiding the boundary); everywhere else the KO edges stay hidden.
        // Degenerate axes (usable region larger than the hull) center on the hull.
        // The full-view center then sits LOWER by the hidden band's world half
        // height (the usable region is the top f of the view).
        float ucx = ClampToHull((minX + maxX) * 0.5f, _stageMinX, _stageMaxX, blast.X, halfW);
        float ucy = ClampToHull((minY + maxY) * 0.5f, _stageMinY, _stageMaxY, blast.Y, usableHalfH);
        return (new Vector2(ucx, ucy - halfH * (1f - f)), halfH);
    }

    /// <summary>Clamp a desired region center so [center ± regionHalf] stays inside
    /// the hull of [-blastHalf, blastHalf] and the stage extent on this axis; the
    /// hull's own center when the region outsizes it.</summary>
    private static float ClampToHull(
        float desired, float stageLo, float stageHi, float blastHalf, float regionHalf)
    {
        float lo = Mathf.Min(-blastHalf, stageLo);
        float hi = Mathf.Max(blastHalf, stageHi);
        return hi - lo <= 2f * regionHalf
            ? (lo + hi) * 0.5f
            : Mathf.Clamp(desired, lo + regionHalf, hi - regionHalf);
    }

    private void Apply()
    {
        Position = new Vector2(_center.X * _ppu, -_center.Y * _ppu);
        float zoom = GetViewportRect().Size.Y * 0.5f / (_halfHeight * _ppu);
        Zoom = new Vector2(zoom, zoom);
    }
}
