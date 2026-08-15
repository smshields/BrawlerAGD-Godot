using Godot;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>Minimap-style stage layout preview: kill box, platforms, spawn dots.
/// Used by the Game Builder cards and the Game Player's stage select/preview
/// (promoted out of GameBuilderView 2026-08-14).</summary>
public sealed partial class StageThumb : Control
{
    private StageGenome? _stage;

    public StageThumb()
    {
        MouseFilter = MouseFilterEnum.Ignore; // parents decide clickability
    }

    public StageThumb(StageGenome stage)
    {
        _stage = stage;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetStage(StageGenome? stage)
    {
        _stage = stage;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.07f, 0.07f, 0.1f));
        if (_stage is null)
        {
            return;
        }
        var blast = StageRules.BlastHalfExtents(_stage.Params);
        if (blast.X <= 0f || blast.Y <= 0f)
        {
            return;
        }
        Vector2 Map(float x, float y) => new(
            (x + blast.X) / (2f * blast.X) * Size.X,
            (blast.Y - y) / (2f * blast.Y) * Size.Y);
        foreach (PlatformGene p in _stage.Platforms)
        {
            Vector2 tl = Map(p.X, p.Y + p.YSize);
            Vector2 br = Map(p.X + p.XSize, p.Y);
            DrawRect(new Rect2(tl, br - tl), new Color(0.85f, 0.85f, 0.9f));
        }
        for (int i = 0; i < 4; i++)
        {
            var s = StageRules.SpawnOf(_stage.Params, i);
            DrawCircle(Map(s.X, s.Y), 2f, PlayerPalette.Of(i));
        }
    }
}
