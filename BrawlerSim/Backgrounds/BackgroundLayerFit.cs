namespace BrawlerSim.Backgrounds;

/// <summary>How one layer covers the kill box: the world-px-per-image-px scale and
/// whether it must TILE (mirror-repeated copies) to get there.</summary>
public readonly record struct LayerFit(float Scale, bool Tiled);

/// <summary>
/// The layer coverage rule (designer 2026-09-03): a background layer must cover the
/// entire camera space (the kill box — the camera is hard-clamped inside it), but
/// may not stretch past LayerMaxScale world px per image px (the policy-D design
/// density is ~2.67 at 720p; stretching far beyond it reads as mush). A layer that
/// cannot cover within that density MIRROR-TILES at the cap instead. Pure math,
/// shared by the arena renderer, the evolve preview, and the stage-select thumbs so
/// every surface shows the same fit.
/// </summary>
public static class BackgroundLayerFit
{
    /// <summary>The far/single quad: the variant crop matches the kill-box aspect,
    /// so a single copy needs boxH / cropH; past the cap it tiles (both axes,
    /// mirror-adjacent — image tops are sky, so the vertical seam lands sky-on-sky).</summary>
    public static LayerFit Far(BgRect crop, float boxH, float maxScale)
    {
        float scale = boxH / Math.Max(1, crop.H);
        return scale <= maxScale ? new LayerFit(scale, false) : new LayerFit(maxScale, true);
    }

    /// <summary>The mid skyline strip: width-fitted to the kill box in one copy when
    /// that stays within density; wider boxes (or tiny mids) tile horizontally at
    /// the cap, bottom-anchored as always.</summary>
    public static LayerFit Mid(int midWidth, float boxW, float maxScale)
    {
        float scale = boxW / Math.Max(1, midWidth);
        return scale <= maxScale ? new LayerFit(scale, false) : new LayerFit(maxScale, true);
    }
}
