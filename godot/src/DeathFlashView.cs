using Godot;

namespace BrawlerGodot;

/// <summary>
/// Death flash (2026-07-22, FEATURES.md §Death Animations; re-rendered per designer
/// 2026-07-22). A Smash-KO-style burst: a TALL, NARROW white streak that shoots from
/// the point of death PERPENDICULARLY into the screen — off the bottom points up, off
/// the right points left (the inward normal of the crossed blast edge). No outline or
/// border; never wider than the character. Juiced: the streak snaps out fast, holds,
/// then fades, with a bright pop at its base. A wider, translucent CONE expands and
/// fades around the needle (2026-07-23, designer: reads as a flame, not a lone
/// needle). Screen-space overlay; purely cosmetic.
/// </summary>
public partial class DeathFlashView : CanvasLayer
{
    private const float LifeSeconds = 0.65f;     // 2026-07-23: slightly longer read
    private const float GrowSeconds = 0.10f;    // snap-out time (juice)
    private const float BaseLenPx = 150f;        // streak length at min intensity
    private const float MaxLenPx = 480f;         // …at full intensity

    private FlashControl _control = null!;

    public override void _Ready()
    {
        _control = new FlashControl();
        AddChild(_control);
    }

    /// <summary>Fire a KO streak. anchorFraction is the death point on the crossed
    /// screen edge (0..1 of the viewport); inwardDir is the screen-space unit normal
    /// pointing INTO the screen; widthPx is the streak width (already ≤ the character);
    /// intensity01 (0..1) scales length and brightness from KO speed + damage.</summary>
    public void Trigger(Vector2 anchorFraction, Vector2 inwardDir, float widthPx, float intensity01)
    {
        _control.Trigger(anchorFraction, inwardDir, widthPx, intensity01);
    }

    private sealed partial class FlashControl : Control
    {
        private Vector2 _anchorFraction;
        private Vector2 _inward = Vector2.Up;
        private float _widthPx;
        private float _intensity;
        private float _life;

        public FlashControl()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        public void Trigger(Vector2 anchorFraction, Vector2 inwardDir, float widthPx, float intensity01)
        {
            _anchorFraction = anchorFraction;
            _inward = inwardDir.Length() > 0.001f ? inwardDir.Normalized() : Vector2.Up;
            _widthPx = widthPx;
            _intensity = Mathf.Clamp(intensity01, 0f, 1f);
            _life = LifeSeconds;
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (_life > 0f)
            {
                _life = Mathf.Max(0f, _life - (float)delta);
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (_life <= 0f)
            {
                return;
            }
            float elapsed = LifeSeconds - _life;
            // Juice: length snaps out (ease-out) over GrowSeconds, then holds; alpha is
            // full during the snap, then fades to zero over the remainder.
            float grow = Mathf.Clamp(elapsed / GrowSeconds, 0f, 1f);
            float lenEnv = 1f - (1f - grow) * (1f - grow); // ease-out
            float alpha = elapsed < GrowSeconds
                ? 1f
                : Mathf.Clamp(_life / (LifeSeconds - GrowSeconds), 0f, 1f);

            Vector2 anchor = _anchorFraction * Size;
            float length = Mathf.Lerp(BaseLenPx, MaxLenPx, _intensity) * lenEnv;
            Vector2 tip = anchor + _inward * length;
            Vector2 perp = new Vector2(-_inward.Y, _inward.X) * (_widthPx * 0.5f);

            // Flame envelope (2026-07-23): a translucent cone that keeps EXPANDING
            // outward over the flash's life while it fades — wider than the needle,
            // widest at its far end.
            float spread = 1.6f + 2.6f * (elapsed / LifeSeconds);
            Vector2 conePerp = perp * spread;
            Vector2 coneTip = anchor + _inward * (length * 1.15f);
            var conePts = new[]
            {
                anchor - perp * 0.6f, anchor + perp * 0.6f,
                coneTip + conePerp, coneTip - conePerp,
            };
            var coneBase = new Color(1f, 1f, 1f, 0.30f * alpha);
            var coneTipCol = new Color(1f, 1f, 1f, 0f);
            DrawPolygon(conePts, new[] { coneBase, coneBase, coneTipCol, coneTipCol });

            // Tall narrow streak: bright, opaque base fading to a transparent, tapered
            // tip. A quad with per-vertex colors — no outline.
            var pts = new[] { anchor - perp, anchor + perp, tip + perp * 0.2f, tip - perp * 0.2f };
            var baseCol = new Color(1f, 1f, 1f, 0.92f * alpha);
            var tipCol = new Color(1f, 1f, 1f, 0f);
            DrawPolygon(pts, new[] { baseCol, baseCol, tipCol, tipCol });

            // Bright pop at the base during the snap-out (the KO "spark").
            if (elapsed < GrowSeconds * 1.5f)
            {
                float popAlpha = alpha * (1f - elapsed / (GrowSeconds * 1.5f));
                DrawCircle(anchor, _widthPx * 0.7f, new Color(1f, 1f, 1f, popAlpha));
            }
        }
    }
}
