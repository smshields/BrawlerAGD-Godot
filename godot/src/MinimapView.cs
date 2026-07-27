using Godot;
using BrawlerSim.Sim;
using SimAabb = BrawlerSim.Sim.Aabb;

namespace BrawlerGodot;

/// <summary>
/// Semi-transparent minimap overlay (2026-07-21, Map Size — designer: on by default,
/// upper-right, 50% transparency; corner/size/opacity configurable in SETTINGS). Shows
/// the whole KO box: platforms, both players (P1 filled, P2 hollow), live projectiles
/// (gold, matching the projectile vocabulary), the visible-map bounds, and the current
/// camera view rect. Screen-space (CanvasLayer) so the arena camera cannot move it.
/// Purely cosmetic — reads sim state only.
/// </summary>
public partial class MinimapView : CanvasLayer
{
    private const float Margin = 16f;

    private SimWorld _world = null!;
    private ArenaCamera _camera = null!;
    private Panel _frame = null!;
    private MapControl _map = null!;

    public void Setup(SimWorld world, ArenaCamera camera)
    {
        _world = world;
        _camera = camera;
        Visible = AppSettings.MinimapEnabled;

        _frame = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.09f, 0.12f, 0.85f),
            BorderColor = new Color(0.55f, 0.6f, 0.68f, 0.9f),
        };
        style.SetBorderWidthAll(1);
        _frame.AddThemeStyleboxOverride("panel", style);
        AddChild(_frame);

        _map = new MapControl { View = this };
        _frame.AddChild(_map);

        Layout();
    }

    /// <summary>Size/corner/opacity from settings — cheap enough to run per frame, so
    /// settings changes apply without restarting the match.</summary>
    private void Layout()
    {
        Vector2 viewport = _frame.GetViewportRect().Size;
        var blast = _world.BlastZone.Half;
        float width = viewport.X * AppSettings.MinimapSize;
        float height = blast.X > 0f ? width * (blast.Y / blast.X) : width * 0.5f;
        height = Mathf.Min(height, viewport.Y * 0.4f);

        float x = AppSettings.MinimapCorner is AppSettings.Corner.UpperLeft or AppSettings.Corner.LowerLeft
            ? Margin
            : viewport.X - width - Margin;
        float y = AppSettings.MinimapCorner is AppSettings.Corner.UpperLeft or AppSettings.Corner.UpperRight
            ? Margin
            : viewport.Y - height - Margin;

        _frame.Position = new Vector2(x, y);
        _frame.Size = new Vector2(width, height);
        _frame.Modulate = new Color(1f, 1f, 1f, AppSettings.MinimapOpacity);
        _map.Position = Vector2.Zero;
        _map.Size = _frame.Size;
    }

    public void Sync()
    {
        Visible = AppSettings.MinimapEnabled;
        if (!Visible)
        {
            return;
        }
        Layout();
        _map.QueueRedraw();
    }

    private partial class MapControl : Control
    {
        public MinimapView View = null!;

        public override void _Draw()
        {
            SimWorld world = View._world;
            var blast = world.BlastZone.Half;
            if (blast.X <= 0f || blast.Y <= 0f)
            {
                return;
            }

            // Visible-map bounds, faint (the KO box is the panel edge itself).
            DrawRect(WorldRect(new SimAabb(BrawlerSim.Determinism.Vec2.Zero, world.VisibleHalf)),
                new Color(0.4f, 0.45f, 0.52f, 0.6f), filled: false);

            foreach (SimAabb platform in world.Platforms)
            {
                DrawRect(WorldRect(platform), new Color(0.85f, 0.85f, 0.9f));
            }

            foreach (SimProjectile projectile in world.Projectiles)
            {
                DrawCircle(WorldPoint(projectile.Position), 2f, new Color(1f, 0.84f, 0.25f));
            }

            // P1 filled, P2 hollow — matches the HUD's left/right panel identities.
            DrawCircle(WorldPoint(world.Players[0].Position), 3f, Colors.White);
            Vector2 p2 = WorldPoint(world.Players[1].Position);
            DrawArc(p2, 3f, 0f, Mathf.Tau, 16, Colors.White, 1.5f);

            DrawRect(WorldRect(View._camera.ViewWorldRect),
                new Color(1f, 0.9f, 0.6f, 0.9f), filled: false);
        }

        private Vector2 WorldPoint(BrawlerSim.Determinism.Vec2 world)
        {
            var blast = View._world.BlastZone.Half;
            return new Vector2(
                (world.X + blast.X) / (2f * blast.X) * Size.X,
                (blast.Y - world.Y) / (2f * blast.Y) * Size.Y);
        }

        private Rect2 WorldRect(in SimAabb box)
        {
            Vector2 topLeft = WorldPoint(new BrawlerSim.Determinism.Vec2(box.Left, box.Top));
            Vector2 bottomRight = WorldPoint(new BrawlerSim.Determinism.Vec2(box.Right, box.Bottom));
            return new Rect2(topLeft, bottomRight - topLeft);
        }
    }
}
