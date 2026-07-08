using Godot;
using BrawlerSim.Sim;
using SimAabb = BrawlerSim.Sim.Aabb;

namespace BrawlerGodot;

/// <summary>Draws the platforms and a faint blast-zone boundary from sim data.</summary>
public partial class StageView : Node2D
{
    private SimWorld _world = null!;
    private float _ppu;

    public void Setup(SimWorld world, float ppu)
    {
        _world = world;
        _ppu = ppu;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }

        foreach (SimAabb platform in _world.Platforms)
        {
            Rect2 rect = ToScreen(platform);
            DrawRect(rect, new Color(0.30f, 0.66f, 0.47f));
            DrawRect(rect, new Color(0.82f, 0.94f, 0.86f), filled: false, width: 2f);
        }

        var blast = new SimAabb(
            BrawlerSim.Determinism.Vec2.Zero,
            new BrawlerSim.Determinism.Vec2(
                _world.Config.BlastZoneHalfWidth, _world.Config.BlastZoneHalfHeight));
        DrawRect(ToScreen(blast), new Color(1f, 0.4f, 0.35f, 0.25f), filled: false, width: 2f);
    }

    private Rect2 ToScreen(in SimAabb box) => new(
        (box.Left) * _ppu,
        -box.Top * _ppu,
        (box.Right - box.Left) * _ppu,
        (box.Top - box.Bottom) * _ppu);
}
