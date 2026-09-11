using System.Linq;
using Godot;
using BrawlerSim.Backgrounds;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>Minimap-style stage layout preview: kill box, platforms, spawn dots.
/// Used by the Game Builder cards and the Game Player's stage select/preview
/// (promoted out of GameBuilderView 2026-08-14).
/// 2026-08-17 (designer): content is fitted UNIFORMLY (true aspect, letterboxed,
/// centered) to the union of the kill box and every platform/spawn — legacy
/// stages can have platforms outside the kill box, which previously drew past
/// the control's bounds; ClipContents backstops any residue.
/// 2026-09-02 (designer): the stage's BACKDROP draws inside the kill box (dimmed,
/// far + mid for composites) so stage select previews the real look — the in-match
/// minimap stays schematic on purpose.</summary>
public sealed partial class StageThumb : Control
{
    private StageGenome? _stage;
    private string? _backgroundRemap;

    public StageThumb()
    {
        MouseFilter = MouseFilterEnum.Ignore; // parents decide clickability
        ClipContents = true;
    }

    public StageThumb(StageGenome stage, string? backgroundRemap = null)
    {
        _stage = stage;
        _backgroundRemap = backgroundRemap;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    /// <summary>backgroundRemap: a built stage's persisted single-image remap
    /// (BuiltStage.BackgroundRemap); null derives it from the seed.</summary>
    public void SetStage(StageGenome? stage, string? backgroundRemap = null)
    {
        _stage = stage;
        _backgroundRemap = backgroundRemap;
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

        Vector2 boxTl = Map(-blast.X, blast.Y);
        Vector2 boxBr = Map(blast.X, -blast.Y);
        DrawBackdrop(new Rect2(boxTl, boxBr - boxTl));

        // Arena bounds, faint (same vocabulary as the in-match minimap frame).
        DrawRect(new Rect2(boxTl, boxBr - boxTl), new Color(0.28f, 0.3f, 0.38f), filled: false, width: 1f);

        foreach (PlatformGene p in _stage.Platforms)
        {
            // Thin platforms (2026-09-01) draw as the top slice of their cell —
            // the thumb mirrors the in-match slab height (min 1 px so tiny
            // thumbs still show the platform at all).
            float bottom = p.Thin
                ? p.Y + p.YSize - BrawlerSim.Sim.MatchConfig.Default.ThinPlatformThickness
                : p.Y;
            Vector2 tl = Map(p.X, p.Y + p.YSize);
            Vector2 br = Map(p.X + p.XSize, bottom);
            var rect = new Rect2(tl, br - tl);
            if (rect.Size.Y < 1f)
            {
                rect.Size = new Vector2(rect.Size.X, 1f);
            }
            DrawRect(rect, new Color(0.85f, 0.85f, 0.9f));
        }
        for (int i = 0; i < 4; i++)
        {
            var s = StageRules.SpawnOf(_stage.Params, i);
            DrawCircle(Map(s.X, s.Y), 2.5f, PlayerPalette.Of(i));
        }
    }

    /// <summary>The real backdrop inside the kill-box rect (designer 2026-09-02;
    /// v0.4 coverage plans 2026-09-11): the far/single layer with its top extension,
    /// then the floor-anchored mid with its bottom extension — the SAME
    /// BackgroundCoverage plans the arena renders, scaled into the thumb, dimmed
    /// like the arena, schematic platforms drawn on top. Null-gene stages keep the
    /// plain thumb.</summary>
    private void DrawBackdrop(Rect2 box)
    {
        if (_stage?.BackgroundId is null)
        {
            return;
        }
        BackgroundSelector selector = BackgroundBank.Selector;
        BackgroundLayout? layout = selector.Layout(
            _stage, BrawlerSim.Serialization.BuiltGameNaming.NamingSeed(_stage), _backgroundRemap);
        if (layout is null)
        {
            return;
        }
        float dim = selector.Config.BaseDim;
        var tint = new Color(dim, dim, dim);
        const float ppu = 72f; // the arena's px-per-unit: the fits must match in-game
        var blast = StageRules.BlastHalfExtents(_stage.Params);
        float boxWpx = blast.X * 2f * ppu;
        float boxHpx = blast.Y * 2f * ppu;
        float thumbScale = box.Size.X / boxWpx; // thumb px per arena px

        BackgroundEntry farEntry = layout.Single ?? layout.Far!;
        BackgroundVariant v = layout.Variant;
        CoveragePlan farPlan = BackgroundCoverage.Far(
            farEntry, v.Crop, boxWpx, boxHpx, selector.Config.LayerMaxScale);
        DrawPlan(farPlan, farEntry, v.Crop, layout.Remap, box, thumbScale, tint, v.FlipX);

        if (layout.Mid is { } mid)
        {
            float floorWorldY = _stage.Platforms.Min(p => p.Y);
            float floorY = (blast.Y - floorWorldY) * ppu;
            CoveragePlan midPlan = BackgroundCoverage.Mid(
                mid, boxWpx, boxHpx, floorY, selector.Config.LayerMaxScale);
            DrawPlan(midPlan, mid, new BgRect(0, 0, mid.Width, mid.Height),
                layout.Remap, box, thumbScale, tint, flipX: false);
        }
    }

    /// <summary>One coverage plan into the thumb rect: base row (mirror-tiled when
    /// wrapped), then the solid/smear extension strips.</summary>
    private void DrawPlan(CoveragePlan plan, BackgroundEntry entry, BgRect crop,
        string? remap, Rect2 box, float thumbScale, Color tint, bool flipX)
    {
        Texture2D texture = BackgroundBank.TextureFor(entry, remap);
        var src = new Rect2(crop.X, crop.Y, crop.W, crop.H);
        if (plan.TileBothAxes)
        {
            DrawTiledRow(texture, src, box, box.Size.Y,
                copyWidth: crop.W * plan.Scale * thumbScale, tint, flipX);
            return;
        }
        var baseBand = new Rect2(
            box.Position.X + plan.BaseX * thumbScale,
            box.Position.Y + plan.BaseY * thumbScale,
            plan.BaseW * thumbScale,
            plan.BaseH * thumbScale);
        if (plan.Wrapped)
        {
            DrawTiledRow(texture, src, baseBand, baseBand.Size.Y,
                copyWidth: crop.W * plan.Scale * thumbScale, tint, flipX);
        }
        else
        {
            // Flips go through a NEGATIVE-WIDTH SOURCE rect — a negative destination
            // rect draws nothing (found on a flipped single-image thumb, 2026-09-03).
            Rect2 flippedSrc = flipX
                ? new Rect2(src.Position.X + src.Size.X, src.Position.Y, -src.Size.X, src.Size.Y)
                : src;
            DrawTextureRectRegion(texture, baseBand, flippedSrc, tint);
        }
        DrawExtend(plan.Top, texture, crop, box, thumbScale, tint, smearTopRow: true);
        DrawExtend(plan.Bottom, texture, crop, box, thumbScale, tint, smearTopRow: false);
    }

    private void DrawExtend(CoveragePiece? piece, Texture2D texture, BgRect crop,
        Rect2 box, float thumbScale, Color tint, bool smearTopRow)
    {
        if (piece is null || piece.Extend.Mode == BgExtend.Transparent)
        {
            return;
        }
        var strip = new Rect2(
            box.Position.X, box.Position.Y + piece.Y * thumbScale,
            box.Size.X, piece.Height * thumbScale);
        if (piece.Extend.Mode == BgExtend.Solid && piece.Extend.Color is { Count: 3 } c)
        {
            DrawRect(strip, new Color(c[0] / 255f, c[1] / 255f, c[2] / 255f) * tint);
            return;
        }
        int rowY = smearTopRow ? crop.Y : crop.Y + crop.H - 1;
        DrawTextureRectRegion(texture, strip, new Rect2(crop.X, rowY, crop.W, 1), tint);
    }

    /// <summary>Mirror-adjacent copies across a band — the thumb's rendition of the
    /// arena's tiled repeat (vertical detail is approximated by the band height).</summary>
    private void DrawTiledRow(Texture2D texture, Rect2 src, Rect2 band, float copyHeight,
        float copyWidth, Color tint, bool flipFirst)
    {
        copyWidth = Mathf.Max(2f, copyWidth);
        int copies = Mathf.CeilToInt(band.Size.X / copyWidth);
        for (int i = 0; i < copies; i++)
        {
            bool flip = (i % 2 == 1) != flipFirst;
            float x = band.Position.X + i * copyWidth;
            float w = Mathf.Min(copyWidth, band.End.X - x);
            var dest = new Rect2(x, band.End.Y - copyHeight, w, copyHeight);
            // Partial last copy: crop the source to the same fraction so pixels map
            // 1:1. Mirrored copies flip through a NEGATIVE-WIDTH SOURCE rect (a
            // negative destination rect draws nothing).
            float partW = src.Size.X * w / copyWidth;
            Rect2 partSrc = flip
                ? new Rect2(src.Position.X + src.Size.X, src.Position.Y, -partW, src.Size.Y)
                : new Rect2(src.Position.X, src.Position.Y, partW, src.Size.Y);
            DrawTextureRectRegion(texture, dest, partSrc, tint);
        }
    }
}
