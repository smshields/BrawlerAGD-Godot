namespace BrawlerSim.Backgrounds;

/// <summary>One vertical extension piece of a coverage plan: the rule to render and
/// the box-local strip (origin = box top-left, y down, px) it must fill.</summary>
public sealed record CoveragePiece(BgExtend Extend, float Y, float Height);

/// <summary>
/// The renderable coverage plan of one background layer: the base image rect, how it
/// repeats to width, and the vertical extension pieces. Local coordinates are
/// kill-box pixels with the origin at the box's TOP-LEFT, y growing down.
/// </summary>
public sealed record CoveragePlan(
    float Scale,        // world px per image px
    bool Wrapped,       // base row repeats horizontally to cover the box width
    bool WrapSeamless,  // wrapped via a real seam (canTileX) vs mirror-wrap
    bool TileBothAxes,  // seamless authored texture: repeat x AND y (subsumes extends)
    float BaseX, float BaseY, float BaseW, float BaseH,
    CoveragePiece? Top, CoveragePiece? Bottom);

/// <summary>
/// The v0.4 coverage contract (playtest remediation §1, 2026-09-11): every layer
/// covers the camera travel bounds — the kill box, which the camera is hard-clamped
/// inside and which parallax factors in [0, 1] can never exceed — at every zoom, so
/// the clear color is UNREACHABLE. Per layer: place at its density-capped scale →
/// wrap to width (canTileX real tile, else mirror-wrap) → extend to height via the
/// entry's extendTop/extendBottom rules (solid fill / edge smear; transparent is
/// COERCED to smear wherever the layer must read opaque). Pure math shared by the
/// arena renderer, the evolve preview, and the stage-select thumbs — and by the
/// property test that proves zero clear-color pixels over 500 seeded stages at
/// arena sizes to 4x/3x the base viewport.
/// </summary>
public static class BackgroundCoverage
{
    /// <summary>The far (or single full) layer: bottom-anchored at the box bottom at
    /// the largest density-legal scale; a capped scale wraps horizontally and extends
    /// the remaining sky upward. Both extends coerce transparent to smear — the far
    /// is the stack's opaque floor, nothing sits behind it.</summary>
    public static CoveragePlan Far(
        BackgroundEntry entry, BgRect crop, float boxW, float boxH, float maxScale)
    {
        if (entry.Tileable)
        {
            // Seamless authored texture: repeat on both axes at design density (one
            // repeat ≈ the legacy 10-world-unit view height) — coverage by repetition.
            float tileScale = Math.Min(boxH / Math.Max(1, crop.H), maxScale);
            return new CoveragePlan(tileScale, Wrapped: true, WrapSeamless: true,
                TileBothAxes: true, 0f, 0f, boxW, boxH, null, null);
        }
        float scale = Math.Min(boxH / Math.Max(1, crop.H), maxScale);
        float baseW = crop.W * scale;
        float baseH = crop.H * scale;
        bool wrapped = baseW < boxW - 0.5f;
        float baseY = boxH - baseH; // bottom-anchored; sky extension grows upward
        CoveragePiece? top = baseY > 0.5f
            ? new CoveragePiece(Opaque(entry.ExtendTop), 0f, baseY)
            : null;
        return new CoveragePlan(scale, wrapped, wrapped && entry.CanTileX,
            TileBothAxes: false,
            0f, baseY, wrapped ? boxW : baseW, baseH, top, null);
    }

    /// <summary>The mid skyline strip: width-fitted within the density cap (wrapped
    /// past it), its BOTTOM anchored at the arena floor line (remediation §1: mids
    /// anchor to the floor and extend DOWN so tall arenas never show sky under the
    /// ground). Above its skyline the mid is alpha by design — extendTop stays
    /// transparent; below, transparent coerces to smear.</summary>
    public static CoveragePlan Mid(
        BackgroundEntry entry, float boxW, float boxH, float floorY, float maxScale)
    {
        float scale = Math.Min(boxW / Math.Max(1, entry.Width), maxScale);
        float baseW = entry.Width * scale;
        float baseH = entry.Height * scale;
        bool wrapped = baseW < boxW - 0.5f;
        float anchorY = Math.Clamp(floorY, 0f, boxH);
        float baseY = anchorY - baseH;
        CoveragePiece? bottom = anchorY < boxH - 0.5f
            ? new CoveragePiece(Opaque(entry.ExtendBottom), anchorY, boxH - anchorY)
            : null;
        return new CoveragePlan(scale, wrapped, wrapped && entry.CanTileX,
            TileBothAxes: false,
            0f, baseY, wrapped ? boxW : baseW, baseH, null, bottom);
    }

    /// <summary>Transparent extension coerced to edge smear: legal for alpha layers,
    /// a coverage breach wherever the strip must read opaque.</summary>
    private static BgExtend Opaque(BgExtend extend) =>
        extend.Mode == BgExtend.Transparent ? new BgExtend(BgExtend.Smear, null) : extend;
}
