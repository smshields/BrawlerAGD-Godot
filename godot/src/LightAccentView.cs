using System.Linq;
using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Lighting;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Budgeted point accents (backgrounds Phase 4 — brief §Phase 4): low-intensity
/// ADDITIVE glow sprites for theme mood — warm ambients get platform underglow
/// (volcanic coals), cool ambients a faint cap shimmer (crystal sheen). Mood, never
/// key light: counts and intensity come from lighting.json, placement is seeded on
/// the widest platforms, and everything sits between the tiles and the fighters.
/// No normal maps anywhere — banded tints only, per the style spec.
/// </summary>
public partial class LightAccentView : Node2D
{
    public void Setup(float ppu, StageGenome stage, LightRigPlan plan, LightingConfig config)
    {
        if (plan.AmbientStrength <= 0f || config.AccentBudget <= 0)
        {
            return;
        }
        float lum = 0.299f * plan.AmbientR + 0.587f * plan.AmbientG + 0.114f * plan.AmbientB;
        bool warm = plan.AmbientR > plan.AmbientB + 0.06f;
        bool cool = plan.AmbientB > plan.AmbientR + 0.06f;
        if (!warm && !cool)
        {
            return; // neutral ambients carry no mood accent
        }
        var rng = new NameGen.Core.Pcg32(BuiltGameNaming.NamingSeed(stage), 0x4247414343454e54UL); // "BGACCENT"
        var color = new Color(plan.AmbientR, plan.AmbientG, plan.AmbientB,
            config.AccentIntensity * Mathf.Clamp(lum + 0.3f, 0.4f, 1f));
        Texture2D glow = GlowTexture();
        var widest = stage.Platforms.OrderByDescending(p => p.XSize)
            .Take(config.AccentBudget).ToList();
        foreach (PlatformGene p in widest)
        {
            float cx = (p.X + p.XSize / 2f + (float)(rng.NextDouble() - 0.5) * p.XSize * 0.4f) * ppu;
            // Warm: underglow beneath the platform; cool: shimmer along the cap.
            float cy = warm ? -(p.Y - 0.6f) * ppu : -(p.Y + p.YSize + 0.25f) * ppu;
            AddChild(new Sprite2D
            {
                Texture = glow,
                Position = new Vector2(cx, cy),
                Scale = new Vector2(p.XSize * ppu / 96f, warm ? 0.9f : 0.45f),
                Modulate = color,
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            });
        }
    }

    /// <summary>A 96x48 soft elliptical glow, generated once.</summary>
    private static ImageTexture? _glow;

    private static ImageTexture GlowTexture()
    {
        if (_glow is not null)
        {
            return _glow;
        }
        var image = Image.CreateEmpty(96, 48, false, Image.Format.Rgba8);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 96; x++)
            {
                float dx = (x - 47.5f) / 47.5f;
                float dy = (y - 23.5f) / 23.5f;
                float a = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy), 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * 0.5f));
            }
        }
        _glow = ImageTexture.CreateFromImage(image);
        return _glow;
    }
}
