using System.Linq;
using Godot;
using BrawlerSim.Backgrounds;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// The stage backdrop (backgrounds track Phases 1-2, 2026-09-02; v0.4 coverage +
/// three-layer contract, 2026-09-11 — docs/background-implementation-brief.md +
/// background-playtest-remediation.md). Drawn FIRST in the arena view stack. Stages
/// render the PARALLAX STACK — L0 far (factor 0.05-0.15), a theme-tinted seam-haze
/// band at the mid skyline, L1 mid (0.35-0.55, alpha above its silhouette, anchored
/// at the arena floor line), and the L2 near bokeh element from the gene (0.75-1.3,
/// heavy blur, capped opacity, dropped to unreadable opacity when platforms reach
/// its band) — everything under the stack's UNIFIED remap. Coverage follows
/// BackgroundCoverage plans: every layer wraps to width (real seam or mirror) and
/// extends to height (solid fill / edge smear), so the clear color is UNREACHABLE —
/// in editor builds a MAGENTA leak detector sits behind the stack (release builds
/// omit it). Vertical parallax runs at VerticalParallaxRatio x the horizontal
/// factor (remediation §2). All variation comes from the selector's seeded
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
    private float _verticalRatio = 0.6f;

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
        _verticalRatio = config.VerticalParallaxRatio;
        FarFactor = layout.FarFactor;
        MidFactor = layout.Mid is null ? MidFactor : layout.MidFactor;
        BrawlerSim.Determinism.Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
        float boxW = blast.X * 2f * ppu;
        float boxH = blast.Y * 2f * ppu;
        BackgroundVariant variant = layout.Variant;
        float baseBlur = config.BlurMaxRadius * variant.BlurScale;

        // The remediation's debug-only clear-color canary: a magenta world-static
        // quad behind the whole stack. Any visible magenta in a capture is a failing
        // coverage test, never a style choice. Exported builds omit it entirely.
        if (OS.HasFeature("editor"))
        {
            AddChild(new Sprite2D
            {
                Texture = SolidTexture(new Color(1f, 0f, 1f)),
                Scale = new Vector2(boxW, boxH),
                ZIndex = -1,
            });
        }

        // L0 — the far (or single) image: bottom-anchored at its density-capped
        // scale, wrapped to width (real seam when canTileX, else mirror), the sky
        // gap above closed by its extendTop rule (BackgroundCoverage.Far).
        BackgroundEntry farEntry = layout.Single ?? layout.Far!;
        CoveragePlan farPlan = BackgroundCoverage.Far(
            farEntry, variant.Crop, boxW, boxH, config.LayerMaxScale);
        ShaderMaterial farMaterial = LayerMaterial(baseBlur, config.BaseDim, variant);
        var farHolder = NewLayer(layout.FarFactor);
        AddPlanSprites(farHolder, farPlan, farEntry, layout.Remap, variant,
            boxW, boxH, farMaterial);

        if (layout.Mid is { } midEntry)
        {
            // L1 — the mid skyline strip: BOTTOM anchored at the arena floor line
            // (the lowest platform's underside) and extended DOWN by its
            // extendBottom rule, so tall arenas never show sky under the ground
            // (remediation §1); width-fitted within the density cap, wrapped past
            // it. The seam haze welds its skyline to the far behind.
            float floorWorldY = stage.Platforms.Min(p => p.Y);
            float floorY = (blast.Y - floorWorldY) * ppu; // box-local, from the top
            CoveragePlan midPlan = BackgroundCoverage.Mid(
                midEntry, boxW, boxH, floorY, config.LayerMaxScale);
            float skylineLocalY = midPlan.BaseY - boxH / 2f;

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
            var midHolder = NewLayer(layout.MidFactor);
            midHolder.AddChild(haze);
            ShaderMaterial midMaterial = LayerMaterial(
                baseBlur * config.MidBlurFraction, config.BaseDim, variant);
            AddPlanSprites(midHolder, midPlan, midEntry, layout.Remap, variant,
                boxW, boxH, midMaterial);
        }

        if (layout.Accent is { } accent && BackgroundBank.IsDiscreteProp(accent.Element))
        {
            // L2 — the near bokeh plane, from the GENE since v0.4: one sparse
            // element, heavy blur, capped opacity — dropped below readability when
            // the platform envelope reaches into its band (three layers minimum
            // beats vanishing; remediation §2). Square-canvas SCENE SLICES mistagged
            // as elements are dropped by the border-alpha guard.
            BackgroundEntry element = accent.Element;
            string? accentRemap = layout.Remap is { } target
                && System.Linq.Enumerable.Contains(selector.LegalRemaps(element), target)
                    ? target
                    : null;
            float envelopeTop = stage.Platforms.Max(p => p.Y + p.YSize);
            float opacity = envelopeTop >= blast.Y * 0.35f
                ? config.NearOverActionOpacity
                : config.NearOpacity;
            float targetH = boxH * accent.Scale;
            float accentScale = targetH / element.Height;
            float anchorX = (accent.Anchor - 1) * blast.X * 0.55f * ppu;
            var accentSprite = new Sprite2D
            {
                Texture = BackgroundBank.TextureFor(element, accentRemap),
                Position = new Vector2(anchorX, -blast.Y * 0.6f * ppu),
                TextureFilter = TextureFilterEnum.Nearest,
                Material = LayerMaterial(config.NearBlurRadius, config.BaseDim, variant),
                Modulate = new Color(1f, 1f, 1f, opacity),
                Scale = new Vector2(accentScale, accentScale),
            };
            NewLayer(accent.Factor).AddChild(accentSprite);
        }

        AttributionLine = BuildAttribution(layout);
        GD.Print($"backdrop: {stage.BackgroundId}"
            + (layout.Remap is { } r ? $" (remap {r})" : ""));

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
        // Parallax: each layer trails the camera by (1 − factor) of its motion;
        // vertically the factor runs at VerticalParallaxRatio x the horizontal one
        // (remediation §2 — a large readability win under a pan+zoom camera, and
        // still coverage-safe: both effective factors stay in [0, 1]). The camera's
        // Position lives in the same parent space as this node's layers.
        if (_camera is not null)
        {
            foreach ((Node2D holder, float factor) in _layers)
            {
                holder.Position = new Vector2(
                    _camera.Position.X * (1f - factor),
                    _camera.Position.Y * (1f - _verticalRatio * factor));
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

    private Node2D NewLayer(float factor)
    {
        var holder = new Node2D();
        AddChild(holder);
        _layers.Add((holder, factor));
        return holder;
    }

    /// <summary>Renders one coverage plan into a layer holder: the base row (wrapped
    /// per the plan), plus its solid/smear extension pieces — all in box-local
    /// coordinates (plan origin = box top-left; node origin = box center) under one
    /// shared blur material.</summary>
    private void AddPlanSprites(Node2D holder, CoveragePlan plan, BackgroundEntry entry,
        string? remap, BackgroundVariant variant, float boxW, float boxH,
        ShaderMaterial material)
    {
        Texture2D texture = BackgroundBank.TextureFor(entry, remap);
        float left = -boxW / 2f;
        float topLocal = plan.BaseY - boxH / 2f;
        BgRect crop = entry.LayerRole is "far" or "full"
            ? variant.Crop
            : new BgRect(0, 0, entry.Width, entry.Height);

        var sprite = new Sprite2D
        {
            Texture = texture,
            Centered = false,
            Position = new Vector2(left + plan.BaseX, topLocal),
            FlipH = variant.FlipX,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = material,
            Scale = new Vector2(plan.Scale, plan.Scale),
            RegionEnabled = true,
        };
        if (plan.TileBothAxes)
        {
            sprite.RegionRect = new Rect2(
                crop.X, crop.Y, boxW / plan.Scale, boxH / plan.Scale);
            sprite.TextureRepeat = TextureRepeatEnum.Enabled;
            sprite.Position = new Vector2(left, -boxH / 2f);
        }
        else if (plan.Wrapped)
        {
            // The crop origin doubles as the seeded tile offset in repeating modes.
            sprite.RegionRect = new Rect2(crop.X, crop.Y, boxW / plan.Scale, crop.H);
            sprite.TextureRepeat = plan.WrapSeamless
                ? TextureRepeatEnum.Enabled
                : TextureRepeatEnum.Mirror;
        }
        else
        {
            sprite.RegionRect = new Rect2(crop.X, crop.Y, crop.W, crop.H);
        }
        holder.AddChild(sprite);

        AddExtendPiece(holder, plan.Top, entry, texture, crop, plan, boxW, boxH,
            material, smearTopRow: true);
        AddExtendPiece(holder, plan.Bottom, entry, texture, crop, plan, boxW, boxH,
            material, smearTopRow: false);
    }

    /// <summary>One vertical extension strip: mode "solid" fills with the index's
    /// color; "smear" clamp-stretches the image's edge row across the strip
    /// (transparent pieces never reach here — coverage plans coerce them opaque
    /// wherever the layer must cover).</summary>
    private static void AddExtendPiece(Node2D holder, CoveragePiece? piece,
        BackgroundEntry entry, Texture2D texture, BgRect crop, CoveragePlan plan,
        float boxW, float boxH, ShaderMaterial material, bool smearTopRow)
    {
        if (piece is null || piece.Extend.Mode == BgExtend.Transparent)
        {
            return;
        }
        float left = -boxW / 2f;
        float yLocal = piece.Y - boxH / 2f;
        if (piece.Extend.Mode == BgExtend.Solid && piece.Extend.Color is { Count: 3 } c)
        {
            holder.AddChild(new Sprite2D
            {
                Texture = SolidTexture(new Color(c[0] / 255f, c[1] / 255f, c[2] / 255f)),
                Centered = false,
                Position = new Vector2(left, yLocal),
                Material = material,
                Scale = new Vector2(boxW, piece.Height),
            });
            return;
        }
        // Edge smear: the crop's outermost row, stretched across the strip. The row
        // wraps horizontally exactly like the base sprite so their seams align.
        int rowY = smearTopRow ? crop.Y : crop.Y + crop.H - 1;
        var smear = new Sprite2D
        {
            Texture = texture,
            Centered = false,
            Position = new Vector2(left, yLocal),
            TextureFilter = TextureFilterEnum.Nearest,
            Material = material,
            RegionEnabled = true,
            RegionRect = new Rect2(
                crop.X, rowY,
                plan.Wrapped ? boxW / plan.Scale : crop.W, 1),
            TextureRepeat = plan.Wrapped && !plan.WrapSeamless
                ? TextureRepeatEnum.Mirror
                : TextureRepeatEnum.Enabled,
            Scale = new Vector2(plan.Scale, piece.Height),
        };
        holder.AddChild(smear);
    }

    private ShaderMaterial LayerMaterial(float blurPx, float dim, BackgroundVariant variant)
    {
        var material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/bg_composite.gdshader"),
        };
        material.SetShaderParameter("blur_px", blurPx);
        material.SetShaderParameter("dim", dim);
        material.SetShaderParameter("falloff_dim", BackgroundBank.Selector.Config.OutOfBandDim);
        material.SetShaderParameter("brightness", variant.Brightness);
        material.SetShaderParameter("contrast", variant.Contrast);
        _materials.Add(material);
        return material;
    }

    /// <summary>A 1x1 solid texture (extend fills, the editor-only leak canary).</summary>
    private static ImageTexture SolidTexture(Color color)
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, color);
        return ImageTexture.CreateFromImage(image);
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
