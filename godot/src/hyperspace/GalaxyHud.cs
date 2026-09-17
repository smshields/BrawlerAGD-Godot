using Godot;

namespace BrawlerGodot.Hyperspace;

/// <summary>
/// The flight overlay drawn on top of the world viewport: crosshair, steer tether and
/// the transient toast line (galaxy-view.md §1). Deliberately NOT the dashboard —
/// that is its own panel of real Controls.
///
/// Two removals from the PoC are load-bearing (2026-09-12 direction): there is NO
/// help overlay and NO top-of-screen galaxy title or colour legend. They were deleted,
/// not moved. Persistent readouts belong to the dashboard; controls documentation
/// belongs to the app's own help system.
/// </summary>
public partial class GalaxyHud : Control
{
    /// <summary>Cursor offset below which the tether is not worth drawing.</summary>
    private const float TetherMinPixels = 30f;

    private Label _toast = null!;
    private double _toastRemaining;

    /// <summary>Cursor position in this control's space, or null when the pointer is
    /// not steering (off the viewport, over the dashboard, locked, warping).</summary>
    public Vector2? SteerCursor { get; set; }

    /// <summary>Lock reticle: projected centre, apparent radius, and the target's
    /// name. Null when nothing is locked.</summary>
    public (Vector2 Center, float Radius, string Name)? Reticle { get; set; }

    /// <summary>Hover ring — suppressed on the locked object, which has the reticle.</summary>
    public (Vector2 Center, float Radius)? HoverRing { get; set; }

    /// <summary>Bracket rotation, advanced by the view's clock so the reticle reads
    /// as active rather than painted on.</summary>
    private float _bracketAngle;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _toast = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0f),
        };
        _toast.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_toast);
        _toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, LayoutPresetMode.Minsize, 18);
    }

    /// <summary>Transient centre-top message — lock, release, hyperspace, edge of
    /// the universe. The only persistent-free feedback channel in the view.</summary>
    public void Toast(string message, double seconds = 2.2)
    {
        _toast.Text = message.ToUpperInvariant();
        _toastRemaining = seconds;
    }

    public override void _Process(double delta)
    {
        _bracketAngle += (float)delta * 1.6f;
        if (_toastRemaining > 0.0)
        {
            _toastRemaining -= delta;
            float alpha = (float)Mathf.Clamp(_toastRemaining / 0.6, 0.0, 1.0);
            _toast.Modulate = new Color(1f, 1f, 1f, alpha);
        }
        QueueRedraw();
    }

    private void DrawHoverRing()
    {
        if (HoverRing is not { } ring)
        {
            return;
        }
        DrawArc(ring.Center, ring.Radius + 6f, 0f, Mathf.Tau, 32,
            new Color(0.72f, 0.82f, 1f, 0.35f), 1.2f);
    }

    /// <summary>Four rotating arc brackets at the target's apparent radius, name
    /// above. Rotation is what separates "locked" from "hovered" at a glance.</summary>
    private void DrawReticle()
    {
        if (Reticle is not { } reticle)
        {
            return;
        }
        var gold = new Color(1f, 0.82f, 0.35f, 0.9f);
        float radius = reticle.Radius + 10f;
        const float sweep = Mathf.Pi / 7f;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            float start = _bracketAngle + quadrant * Mathf.Pi / 2f;
            DrawArc(reticle.Center, radius, start - sweep, start + sweep, 12, gold, 2f);
        }
        Font font = ThemeDB.FallbackFont;
        Vector2 size = font.GetStringSize(reticle.Name, fontSize: 13);
        DrawString(font, reticle.Center + new Vector2(-size.X / 2f, -radius - 12f),
            reticle.Name, HorizontalAlignment.Left, -1, 13, gold);
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2f;
        var accent = new Color(0.72f, 0.82f, 1f, 0.75f);

        // Crosshair: a gap-centred cross, so it never hides what it is aimed at.
        const float inner = 5f, outer = 13f;
        DrawLine(center + new Vector2(inner, 0f), center + new Vector2(outer, 0f), accent);
        DrawLine(center - new Vector2(outer, 0f), center - new Vector2(inner, 0f), accent);
        DrawLine(center + new Vector2(0f, inner), center + new Vector2(0f, outer), accent);
        DrawLine(center - new Vector2(0f, outer), center - new Vector2(0f, inner), accent);

        DrawHoverRing();
        DrawReticle();

        if (SteerCursor is not { } cursor)
        {
            return;
        }
        Vector2 offset = cursor - center;
        if (offset.Length() < TetherMinPixels)
        {
            return;
        }
        // Tether: shows how hard you are steering, fading out along its length.
        DrawLine(center + offset.Normalized() * outer, cursor,
            new Color(0.72f, 0.82f, 1f, 0.35f), 1.5f);
        DrawArc(cursor, 6f, 0f, Mathf.Tau, 20, new Color(0.72f, 0.82f, 1f, 0.5f), 1.2f);
    }
}
