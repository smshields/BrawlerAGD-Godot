using Godot;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>Minimap-style stage layout preview: kill box, platforms, spawn dots.
/// Used by the Game Builder cards and the Game Player's stage select/preview
/// (promoted out of GameBuilderView 2026-08-14).
/// 2026-08-17 (designer): content is fitted UNIFORMLY (true aspect, letterboxed,
/// centered) to the union of the kill box and every platform/spawn — legacy
/// stages can have platforms outside the kill box, which previously drew past
/// the control's bounds; ClipContents backstops any residue.</summary>
public sealed partial class StageThumb : Control
{
    private StageGenome? _stage;

    public StageThumb()
    {
        MouseFilter = MouseFilterEnum.Ignore; // parents decide clickability
        ClipContents = true;
    }

    public StageThumb(StageGenome stage)
    {
        _stage = stage;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
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

        float minX = -blast.X, maxX = blast.X, minY = -blast.Y, maxY = blast.Y;
        foreach (PlatformGene p in _stage.Platforms)
        {
            minX = Mathf.Min(minX, p.X);
            maxX = Mathf.Max(maxX, p.X + p.XSize);
            minY = Mathf.Min(minY, p.Y);
            maxY = Mathf.Max(maxY, p.Y + p.YSize);
        }
        for (int i = 0; i < 4; i++)
        {
            var s = StageRules.SpawnOf(_stage.Params, i);
            minX = Mathf.Min(minX, s.X);
            maxX = Mathf.Max(maxX, s.X);
            minY = Mathf.Min(minY, s.Y);
            maxY = Mathf.Max(maxY, s.Y);
        }

        float scale = Mathf.Min(Size.X * 0.92f / (maxX - minX), Size.Y * 0.92f / (maxY - minY));
        var mid = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
        Vector2 Map(float x, float y) => new(
            Size.X / 2f + (x - mid.X) * scale,
            Size.Y / 2f - (y - mid.Y) * scale);

        // Arena bounds, faint (same vocabulary as the in-match minimap frame).
        Vector2 boxTl = Map(-blast.X, blast.Y);
        Vector2 boxBr = Map(blast.X, -blast.Y);
        DrawRect(new Rect2(boxTl, boxBr - boxTl), new Color(0.28f, 0.3f, 0.38f), filled: false, width: 1f);

        foreach (PlatformGene p in _stage.Platforms)
        {
            Vector2 tl = Map(p.X, p.Y + p.YSize);
            Vector2 br = Map(p.X + p.XSize, p.Y);
            DrawRect(new Rect2(tl, br - tl), new Color(0.85f, 0.85f, 0.9f));
        }
        for (int i = 0; i < 4; i++)
        {
            var s = StageRules.SpawnOf(_stage.Params, i);
            DrawCircle(Map(s.X, s.Y), 2.5f, PlayerPalette.Of(i));
        }
    }
}
