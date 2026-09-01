using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BrawlerGodot;

/// <summary>The hotspot registry and pad cursors (FULL PAD PARITY): every
/// interactive element is a registered hotspot; the mouse clicks them natively and
/// each joined pad steers its own colored cursor over the same hit-test. Split from
/// CharacterSelectView.cs (pure text move).</summary>
public partial class CharacterSelectView
{
    private const float CursorSpeed = 640f; // px/s at design resolution

    /// <summary>An interactive region: pads hit-test these; the mouse clicks them
    /// through the same handler (one interaction path for every device).</summary>
    private sealed record Hotspot(Control Area, System.Action<int> Activate, System.Func<bool>? Enabled = null)
    {
        public bool IsEnabled => Enabled?.Invoke() ?? true;
    }

    private readonly List<Hotspot> _hotspots = new();
    private readonly Dictionary<int, int> _padPane = new();   // pad device -> pane index
    private readonly Dictionary<int, Vector2> _padCursor = new(); // pad device -> position
    private CursorLayer _cursors = null!;

    private void Register(Control area, System.Action<int> activate, System.Func<bool>? enabled = null)
        => _hotspots.Add(new Hotspot(area, activate, enabled));

    private void ActivateAt(Vector2 position, int actor)
    {
        // Later registrations sit visually on top (keyboards, overlays) — scan last-first.
        for (int i = _hotspots.Count - 1; i >= 0; i--)
        {
            Hotspot spot = _hotspots[i];
            if (!IsInstanceValid(spot.Area) || !spot.Area.IsVisibleInTree())
            {
                continue;
            }
            if (spot.Area.GetGlobalRect().HasPoint(position))
            {
                if (spot.IsEnabled)
                {
                    spot.Activate(actor);
                }
                return;
            }
        }
    }

    public override void _Process(double delta)
    {
        // Pad cursors: left stick / dpad, polled per frame.
        foreach ((int device, int _) in _padPane)
        {
            var move = new Vector2(
                Input.GetJoyAxis(device, JoyAxis.LeftX), Input.GetJoyAxis(device, JoyAxis.LeftY));
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadLeft)) move.X -= 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadRight)) move.X += 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadUp)) move.Y -= 1f;
            if (Input.IsJoyButtonPressed(device, JoyButton.DpadDown)) move.Y += 1f;
            if (move.LengthSquared() < 0.04f)
            {
                continue;
            }
            Vector2 next = _padCursor[device] + move.LimitLength(1f) * CursorSpeed * (float)delta;
            _padCursor[device] = next.Clamp(Vector2.Zero, GetViewportRect().Size);
        }
        _cursors.QueueRedraw();
    }

    /// <summary>Draws each joined pad's cursor arrow in its pane color, topmost.</summary>
    private sealed partial class CursorLayer : Control
    {
        private readonly CharacterSelectView _view;

        public CursorLayer(CharacterSelectView view)
        {
            _view = view;
            AnchorRight = 1f;
            AnchorBottom = 1f;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            foreach ((int device, Vector2 pos) in _view._padCursor)
            {
                Color color = PlayerPalette.Of(_view._padPane[device]);
                var points = new[]
                {
                    pos, pos + new Vector2(18f, 7f), pos + new Vector2(11f, 11f),
                    pos + new Vector2(7f, 18f),
                };
                DrawColoredPolygon(points, color);
                DrawPolyline(points.Append(pos).ToArray(), Colors.White, 1.5f);
            }
        }
    }
}
