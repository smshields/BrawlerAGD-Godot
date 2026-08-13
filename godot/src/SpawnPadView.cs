using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// Draws the two temporary spawn platforms (2026-07-22, FEATURES.md §Spawning
/// Behaviors; re-rendered 2026-07-23 per designer: a solid PILL / rounded rectangle
/// that reads as a platform, not an ellipse). Fades in while its owner's pad is
/// active and quickly fades to the background once the pad despawns (the player left
/// it or the timer elapsed). World-space (under the arena camera). Purely cosmetic —
/// reads sim state.
/// </summary>
public partial class SpawnPadView : Node2D
{
    private const float FadeInPerFrame = 0.35f;   // quick appear
    private const float FadeOutPerFrame = 0.12f;   // quick-but-visible dissolve

    private SimWorld _world = null!;
    private float _ppu;
    private float[] _alpha = null!;

    public void Setup(SimWorld world, float ppu)
    {
        _world = world;
        _ppu = ppu;
        _alpha = new float[world.Players.Count]; // one pad per player (2026-08-12)
    }

    public void Sync()
    {
        bool anyVisible = false;
        for (int i = 0; i < _alpha.Length; i++)
        {
            float target = _world.Players[i].SpawnPadActive ? 1f : 0f;
            float rate = target > _alpha[i] ? FadeInPerFrame : FadeOutPerFrame;
            _alpha[i] = Mathf.MoveToward(_alpha[i], target, rate);
            anyVisible |= _alpha[i] > 0.01f;
        }
        Visible = anyVisible;
        if (anyVisible)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        for (int i = 0; i < _alpha.Length; i++)
        {
            if (_alpha[i] <= 0.01f)
            {
                continue;
            }
            DrawPad(_world.SpawnPad(i), _alpha[i]);
        }
    }

    private void DrawPad(in BrawlerSim.Sim.Aabb pad, float alpha)
    {
        // A solid PILL (rounded rectangle, corner radius = half the pad height) that
        // reads as a platform: solid white body, subtle darker underside, bright flat
        // top edge (the standable surface).
        var center = new Vector2(pad.Center.X * _ppu, -pad.Center.Y * _ppu);
        float hx = pad.Half.X * _ppu;
        float hy = pad.Half.Y * _ppu;
        float r = hy; // full-height rounding ⇒ pill ends

        Vector2[] body = PillPoints(center, hx, hy, r);
        DrawColoredPolygon(body, new Color(0.94f, 0.95f, 1f, 0.9f * alpha));
        // Slight underside shade so the pill has platform-like depth.
        var under = new Vector2[]
        {
            center + new Vector2(-hx + r, hy * 0.25f), center + new Vector2(hx - r, hy * 0.25f),
            center + new Vector2(hx - r, hy), center + new Vector2(-hx + r, hy),
        };
        DrawColoredPolygon(under, new Color(0.6f, 0.62f, 0.72f, 0.5f * alpha));
        // Bright flat top edge.
        DrawLine(center + new Vector2(-hx + r, -hy), center + new Vector2(hx - r, -hy),
            new Color(1f, 1f, 1f, alpha), 2f, antialiased: true);
    }

    /// <summary>Pill outline: straight top/bottom runs with semicircular end caps.</summary>
    private static Vector2[] PillPoints(Vector2 center, float hx, float hy, float r)
    {
        const int capSegments = 10;
        var pts = new Vector2[(capSegments + 1) * 2 + 2];
        int n = 0;
        // Right cap: top → bottom (angles -90°..+90°).
        for (int s = 0; s <= capSegments; s++)
        {
            float a = -Mathf.Pi / 2f + Mathf.Pi * s / capSegments;
            pts[n++] = center + new Vector2(hx - r + Mathf.Cos(a) * r, Mathf.Sin(a) * hy);
        }
        pts[n++] = center + new Vector2(-hx + r, hy); // bottom run to the left
        // Left cap: bottom → top (angles +90°..+270°).
        for (int s = 0; s <= capSegments; s++)
        {
            float a = Mathf.Pi / 2f + Mathf.Pi * s / capSegments;
            pts[n++] = center + new Vector2(-hx + r + Mathf.Cos(a) * r, Mathf.Sin(a) * hy);
        }
        pts[n] = center + new Vector2(hx - r, -hy); // top run back to the right
        return pts;
    }
}
