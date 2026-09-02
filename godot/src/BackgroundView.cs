using Godot;
using BrawlerSim.Backgrounds;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// The stage backdrop (backgrounds track Phases 1-2, 2026-09-02 —
/// docs/background-implementation-brief.md). Drawn FIRST in the arena view stack.
/// Single-image stages render one remapped static texture; recombined stages render
/// the PARALLAX STACK — L0 far (factor 0.05-0.15), a theme-tinted seam-haze band at
/// the mid skyline, L1 mid (0.3-0.5, alpha above its silhouette), and the optional
/// L2 near-accent bokeh element (0.7-1.3, heavy blur, capped opacity) — everything
/// under the pair's UNIFIED remap. Every quad is sized against the kill box, which
/// the camera view is hard-clamped inside, so parallax offsets can never expose an
/// edge (factor in [0,1]). All variation comes from the selector's seeded
/// BackgroundLayout — same genome + seed renders the same backdrop everywhere. The
/// tilt-shift focal band is the platform envelope, converted to screen space every
/// frame; per-layer blur grows with depth (far blurriest). Null-gene (pre-v14)
/// stages render nothing: the legacy clear-color backdrop. Purely cosmetic.
/// </summary>
public partial class BackgroundView : Node2D
{
    private readonly System.Collections.Generic.List<(Node2D Holder, float Factor)> _layers = new();
    private readonly System.Collections.Generic.List<ShaderMaterial> _materials = new();
    private ArenaCamera? _camera;
    private float _bandTopLocalY;
    private float _bandBottomLocalY;

    /// <summary>The attribution lines of the rendered entries (pause-menu credits,
    /// designer 2026-09-02), null when nothing is rendered.</summary>
    public string? AttributionLine { get; private set; }

    /// <summary>The stage's parallax depths, consumed by the weather rows (Phase 3);
    /// sensible defaults stand when the stage has no backdrop.</summary>
    public float FarFactor { get; private set; } = 0.1f;
    public float MidFactor { get; private set; } = 0.4f;

    /// <summary>The resolved layout (Phase 4's light rig derives from it); null on
    /// legacy blank backdrops.</summary>
    public BackgroundLayout? Layout { get; private set; }

    /// <summary>Builds the backdrop for the stage, or stays empty on a null gene.
    /// persistedRemap = a built game's settled SINGLE-image remap
    /// (BuiltStage.BackgroundRemap); composites carry their remap inside the gene.</summary>
    public void Setup(float ppu, StageGenome stage, ArenaCamera? camera = null,
        string? persistedRemap = null)
    {
        _camera = camera;
        BackgroundSelector selector = BackgroundBank.Selector;
        ulong seed = BuiltGameNaming.NamingSeed(stage);
        BackgroundLayout? layout = selector.Layout(stage, seed, persistedRemap);
        Layout = layout;
        if (layout is null)
        {
            return;
        }
        BackgroundSelectionConfig config = selector.Config;
        FarFactor = layout.FarFactor;
        MidFactor = layout.Mid is null ? MidFactor : layout.MidFactor;
        BrawlerSim.Determinism.Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
        float boxW = blast.X * 2f * ppu;
        float boxH = blast.Y * 2f * ppu;
        BackgroundVariant variant = layout.Variant;
        float baseBlur = config.BlurMaxRadius * variant.BlurScale;

        // L0 — the far (or single) image covering the whole box. Scene images take
        // the variant crop at the kill-box aspect; TILEABLE textures instead repeat
        // at design density (one repeat ≈ the legacy 10-world-unit view height), so
        // a big kill box never blows their pixels up — the variant crop origin
        // doubles as the seeded tile offset.
        BackgroundEntry farEntry = layout.Single ?? layout.Far!;
        Sprite2D farSprite;
        if (farEntry.Tileable)
        {
            float tileScale = 10f * ppu / farEntry.Height;
            farSprite = new Sprite2D
            {
                Texture = BackgroundBank.TextureFor(farEntry, layout.Remap),
                RegionEnabled = true,
                RegionRect = new Rect2(
                    variant.Crop.X, variant.Crop.Y, boxW / tileScale, boxH / tileScale),
                TextureRepeat = TextureRepeatEnum.Enabled,
                FlipH = variant.FlipX,
                TextureFilter = TextureFilterEnum.Nearest,
                Material = LayerMaterial(baseBlur, config.BaseDim, variant),
                Scale = new Vector2(tileScale, tileScale),
            };
        }
        else
        {
            farSprite = new Sprite2D
            {
                Texture = BackgroundBank.TextureFor(farEntry, layout.Remap),
                RegionEnabled = true,
                RegionRect = new Rect2(variant.Crop.X, variant.Crop.Y, variant.Crop.W, variant.Crop.H),
                FlipH = variant.FlipX,
                TextureFilter = TextureFilterEnum.Nearest,
                Material = LayerMaterial(baseBlur, config.BaseDim, variant),
                Scale = new Vector2(boxW / variant.Crop.W, boxH / variant.Crop.H),
            };
        }
        AddLayer(farSprite, layout.FarFactor);

        if (layout.Mid is { } midEntry)
        {
            // The mid is width-fitted to the kill box and bottom-anchored: its alpha
            // skyline lands where its aspect puts it, and the seam haze welds it to
            // the far behind (strength raised when atmospheric ordering failed).
            Texture2D midTexture = BackgroundBank.TextureFor(midEntry, layout.Remap);
            float midScale = boxW / midEntry.Width;
            float midH = midEntry.Height * midScale;
            float skylineLocalY = blast.Y * ppu - midH; // the mid quad's top edge

            (byte hr, byte hg, byte hb) = BackgroundBank.Palette.RemapDomLight(farEntry, layout.Remap);
            float hazeH = boxH * 0.18f;
            var haze = new Sprite2D
            {
                Texture = HazeTexture(new Color(hr / 255f, hg / 255f, hb / 255f)),
                Centered = false,
                Position = new Vector2(-boxW / 2f, skylineLocalY - hazeH / 2f),
                Scale = new Vector2(boxW / 8f, hazeH / 64f),
                Modulate = new Color(1f, 1f, 1f, layout.SeamHaze),
            };
            AddLayer(haze, layout.MidFactor);

            var midSprite = new Sprite2D
            {
                Texture = midTexture,
                Centered = false,
                Position = new Vector2(-boxW / 2f, skylineLocalY),
                TextureFilter = TextureFilterEnum.Nearest,
                Material = LayerMaterial(baseBlur * config.MidBlurFraction, config.BaseDim, variant),
                Scale = new Vector2(midScale, midScale),
            };
            AddLayer(midSprite, layout.MidFactor);
        }

        if (layout.Accent is { } accent)
        {
            // L2 — the bokeh plane: one sparse element, heavy blur, capped opacity,
            // anchored in the upper band (the selector already skipped stages whose
            // platforms reach that high).
            BackgroundEntry element = accent.Element;
            string? accentRemap = layout.Remap is { } target
                && System.Linq.Enumerable.Contains(selector.LegalRemaps(element), target)
                    ? target
                    : null;
            float targetH = boxH * accent.Scale;
            float accentScale = targetH / element.Height;
            float anchorX = (accent.Anchor - 1) * blast.X * 0.55f * ppu;
            var accentSprite = new Sprite2D
            {
                Texture = BackgroundBank.TextureFor(element, accentRemap),
                Position = new Vector2(anchorX, -blast.Y * 0.6f * ppu),
                TextureFilter = TextureFilterEnum.Nearest,
                Material = LayerMaterial(config.AccentBlurRadius, config.BaseDim, variant),
                Modulate = new Color(1f, 1f, 1f, config.AccentOpacity),
                Scale = new Vector2(accentScale, accentScale),
            };
            AddLayer(accentSprite, accent.Factor);
        }

        AttributionLine = BuildAttribution(layout);
        GD.Print($"backdrop: {stage.BackgroundId}"
            + (layout.Remap is { } r ? $" (remap {r})" : "")
            + (layout.Accent is { } a ? $" + accent {a.Element.Id}" : ""));

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
        if (_materials.Count == 0)
        {
            return;
        }
        // Parallax: each layer trails the camera by (1 − factor) of its motion; the
        // camera's Position lives in the same parent space as this node's layers.
        if (_camera is not null)
        {
            foreach ((Node2D holder, float factor) in _layers)
            {
                holder.Position = _camera.Position * (1f - factor);
            }
        }
        // Platform envelope → screen UV under the current pan/zoom, so the focal
        // band tracks the platforms at any framing.
        Transform2D toScreen = GetGlobalTransformWithCanvas();
        float viewH = GetViewportRect().Size.Y;
        if (viewH <= 0f)
        {
            return;
        }
        float topUv = (toScreen * new Vector2(0f, _bandTopLocalY)).Y / viewH;
        float bottomUv = (toScreen * new Vector2(0f, _bandBottomLocalY)).Y / viewH;
        foreach (ShaderMaterial material in _materials)
        {
            material.SetShaderParameter("band_top", Mathf.Clamp(topUv, 0f, 1f));
            material.SetShaderParameter("band_bottom", Mathf.Clamp(bottomUv, 0f, 1f));
        }
    }

    private void AddLayer(Node2D sprite, float factor)
    {
        var holder = new Node2D();
        AddChild(holder);
        holder.AddChild(sprite);
        _layers.Add((holder, factor));
    }

    private ShaderMaterial LayerMaterial(float blurPx, float dim, BackgroundVariant variant)
    {
        var material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/bg_composite.gdshader"),
        };
        material.SetShaderParameter("blur_px", blurPx);
        material.SetShaderParameter("dim", dim);
        material.SetShaderParameter("brightness", variant.Brightness);
        material.SetShaderParameter("contrast", variant.Contrast);
        _materials.Add(material);
        return material;
    }

    /// <summary>An 8x64 vertical gradient (transparent → color → transparent), the
    /// seam-haze band's texture; tinted by the far layer's post-remap dominant light.</summary>
    private static ImageTexture HazeTexture(Color color)
    {
        var image = Image.CreateEmpty(8, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        {
            float t = 1f - Mathf.Abs(y - 31.5f) / 31.5f; // peak at the seam
            var c = color with { A = Mathf.SmoothStep(0f, 1f, t) };
            for (int x = 0; x < 8; x++)
            {
                image.SetPixel(x, y, c);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static string? BuildAttribution(BackgroundLayout layout)
    {
        static string Line(BackgroundEntry e) =>
            e.Attribution ?? $"{e.Author ?? e.Source} ({e.License})";
        var parts = new System.Collections.Generic.List<string>();
        if (layout.Single is { } single)
        {
            parts.Add(Line(single));
        }
        if (layout.Far is { } far)
        {
            parts.Add(Line(far));
        }
        if (layout.Mid is { } mid && !parts.Contains(Line(mid)))
        {
            parts.Add(Line(mid));
        }
        if (layout.Accent is { } accent && !parts.Contains(Line(accent.Element)))
        {
            parts.Add(Line(accent.Element));
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
