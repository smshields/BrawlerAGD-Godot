using Godot;
using BrawlerSim.Backgrounds;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// The stage backdrop (backgrounds track Phase 1, 2026-09-02 —
/// docs/background-implementation-brief.md). Drawn FIRST in the arena view stack (its
/// own node behind StageView): one static remapped texture on a quad that covers the
/// whole kill box in world space, so the camera pans and zooms over it naturally and
/// the view (hard-clamped inside the kill box) can never run off its edge. All
/// variation is the selector's seeded parametric variant — crop (kill-box aspect,
/// horizon-banded), flip, brightness/contrast, blur scale — so the same genome + seed
/// renders the same backdrop everywhere. The tilt-shift focal band is the platform
/// envelope, converted to screen space EVERY frame (zoom keeps platforms sharpest).
/// Null-gene (pre-v14) stages render nothing: the legacy clear-color backdrop.
/// Purely cosmetic — reads the stage genome only, never sim state.
/// </summary>
public partial class BackgroundView : Node2D
{
    private ShaderMaterial? _material;
    private float _bandTopLocalY;
    private float _bandBottomLocalY;

    /// <summary>The attribution line of the rendered entry (pause-menu credits,
    /// designer 2026-09-02), null when nothing is rendered.</summary>
    public string? AttributionLine { get; private set; }

    /// <summary>Builds the backdrop for the stage, or stays empty on a null gene.
    /// persistedRemap = a built game's settled remap (BuiltStage.BackgroundRemap);
    /// null falls back to the seeded settle, which matches the persisted value under
    /// the shipped tuning by construction.</summary>
    public void Setup(float ppu, StageGenome stage, string? persistedRemap = null)
    {
        BackgroundEntry? entry = BackgroundBank.Library.ById(stage.BackgroundId);
        if (entry is null)
        {
            return;
        }
        BackgroundSelector selector = BackgroundBank.Selector;
        ulong seed = BuiltGameNaming.NamingSeed(stage);
        string? remap = persistedRemap ?? selector.PickRemap(entry, stage.ThemeId, seed);
        BackgroundVariant variant = selector.Variant(entry, stage, seed);
        BackgroundSelectionConfig config = selector.Config;

        AttributionLine = entry.Attribution
            ?? $"{entry.Author ?? entry.Source} ({entry.License})";

        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/bg_composite.gdshader"),
        };
        _material.SetShaderParameter("blur_px", config.BlurMaxRadius * variant.BlurScale);
        _material.SetShaderParameter("dim", config.BaseDim);
        _material.SetShaderParameter("brightness", variant.Brightness);
        _material.SetShaderParameter("contrast", variant.Contrast);

        // The quad covers the kill box exactly: the camera view is hard-clamped
        // inside it (ArenaCamera), so full coverage needs nothing more.
        BrawlerSim.Determinism.Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
        var sprite = new Sprite2D
        {
            Texture = BackgroundBank.TextureFor(entry, remap),
            RegionEnabled = true,
            RegionRect = new Rect2(variant.Crop.X, variant.Crop.Y, variant.Crop.W, variant.Crop.H),
            FlipH = variant.FlipX,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = _material,
            Scale = new Vector2(
                blast.X * 2f * ppu / variant.Crop.W,
                blast.Y * 2f * ppu / variant.Crop.H),
        };
        AddChild(sprite);

        // The sharp focal band = the platform envelope (world units), pinned here in
        // LOCAL pixels; _Process converts it through the live camera transform.
        float top = float.MinValue;
        float bottom = float.MaxValue;
        foreach (PlatformGene p in stage.Platforms)
        {
            top = Mathf.Max(top, p.Y + p.YSize);
            bottom = Mathf.Min(bottom, p.Y);
        }
        _bandTopLocalY = -(top + config.FocalMarginWorld) * ppu;
        _bandBottomLocalY = -(bottom - config.FocalMarginWorld) * ppu;
    }

    public override void _Process(double delta)
    {
        if (_material is null)
        {
            return;
        }
        // Platform envelope → screen UV under the current camera pan/zoom, so the
        // focal band tracks the platforms at any framing.
        Transform2D toScreen = GetGlobalTransformWithCanvas();
        float viewH = GetViewportRect().Size.Y;
        if (viewH <= 0f)
        {
            return;
        }
        float topUv = (toScreen * new Vector2(0f, _bandTopLocalY)).Y / viewH;
        float bottomUv = (toScreen * new Vector2(0f, _bandBottomLocalY)).Y / viewH;
        _material.SetShaderParameter("band_top", Mathf.Clamp(topUv, 0f, 1f));
        _material.SetShaderParameter("band_bottom", Mathf.Clamp(bottomUv, 0f, 1f));
    }
}
