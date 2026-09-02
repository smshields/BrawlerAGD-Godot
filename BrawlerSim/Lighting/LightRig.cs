using BrawlerSim.Backgrounds;

namespace BrawlerSim.Lighting;

/// <summary>The derived per-stage rig: the ambient color (the composited, remapped
/// backdrop's dominant light — a data lookup, never runtime image analysis), the
/// cap-light color walking surfaces shift toward, the tint strengths AFTER the
/// contrast self-clamp, and the fighter tint (≤ the cap). Static during play except
/// weather modulation within WeatherAmplitudeCap. Colors are 0-1 floats.</summary>
public sealed record LightRigPlan(
    float AmbientR, float AmbientG, float AmbientB, float AmbientStrength,
    float CapR, float CapG, float CapB, float CapStrength,
    float FighterTint, float WeatherAmplitude)
{
    /// <summary>The rig-off identity plan (legacy blank backdrops).</summary>
    public static readonly LightRigPlan Neutral =
        new(1f, 1f, 1f, 0f, 1f, 1f, 1f, 0f, 0f, 0f);
}

/// <summary>
/// The stage light rig (backgrounds track Phase 4, 2026-09-02 — brief §Phase 4).
/// Derives ambient + cap-light from the backdrop's post-remap dominant light color
/// and SELF-CLAMPS: the action-band contrast between lit caps and the dimmed
/// backdrop is recomputed with the rig applied (from stored metrics), and the rig
/// scales back until the floor holds — never below the rig-off baseline. TintTile
/// is the SHARED CONTRACT with the tile shader (tile_rig.gdshader mirrors it), so
/// the outline exemption is testable bit-for-bit here.
/// </summary>
public static class LightRig
{
    /// <summary>Derive the rig for a resolved backdrop; null layout = Neutral.</summary>
    public static LightRigPlan Derive(BackgroundLayout? layout, BackgroundPalette palette,
        LightingConfig config)
    {
        if (layout is null)
        {
            return LightRigPlan.Neutral;
        }
        // The composited dominant light: the far layer owns the sky (0.6), the mid
        // silhouette moderates it (0.4); single images are their own light.
        (float r, float g, float b) ambient;
        float backdropLum;
        if (layout.Mid is { } mid)
        {
            (byte fr, byte fg, byte fb) = palette.RemapDomLight(layout.Far!, layout.Remap);
            (byte mr, byte mg, byte mb) = palette.RemapDomLight(mid, layout.Remap);
            ambient = (Mix(fr, mr), Mix(fg, mg), Mix(fb, mb));
            backdropLum = 0.6f * Lum(fr, fg, fb) + 0.4f * Lum(mr, mg, mb);
        }
        else
        {
            (byte sr, byte sg, byte sb) = palette.RemapDomLight(layout.Single!, layout.Remap);
            ambient = (sr / 255f, sg / 255f, sb / 255f);
            backdropLum = Lum(sr, sg, sb);
        }

        // Cap light: the ambient hue, lifted and desaturated — the sky the caps
        // reflect, one value step up (banded tints, never re-shading).
        (float cr, float cg, float cb) = Lift(ambient);

        float ambientStrength = config.AmbientStrength;
        float capStrength = config.CapTintStrength;

        // Self-clamp on stored metrics: lit caps vs the DIMMED backdrop. The floor
        // is best-effort down to the rig-off baseline (the dim layer owns that).
        float bgLum = backdropLum * config.AssumedBackdropDim;
        float floorTarget = Math.Min(config.ContrastFloor,
            Math.Abs(config.CapBaseLum - bgLum));
        float capLightLum = 0.299f * cr + 0.587f * cg + 0.114f * cb;
        for (int i = 0; i < 12; i++)
        {
            float cap = config.CapBaseLum + (capLightLum - config.CapBaseLum) * capStrength;
            float ambientMul = 1f + (Lum3(ambient) - 1f) * ambientStrength;
            if (Math.Abs(cap * ambientMul - bgLum) >= floorTarget - 1e-4f)
            {
                break;
            }
            capStrength *= 0.75f;
            ambientStrength *= 0.85f;
        }

        return new LightRigPlan(
            ambient.r, ambient.g, ambient.b, ambientStrength,
            cr, cg, cb, capStrength,
            Math.Min(config.FighterTintCap, config.FighterTintCap),
            config.WeatherAmplitudeCap);
    }

    /// <summary>The tile tint, shared with tile_rig.gdshader: near-black outline
    /// pixels return BIT-IDENTICAL; everything else multiplies toward ambient and,
    /// in the lit-cap luminance band, shifts toward the cap light.</summary>
    public static (byte R, byte G, byte B) TintTile((byte R, byte G, byte B) c,
        LightRigPlan plan, LightingConfig config, float weatherMod = 1f)
    {
        if (Math.Max(c.R, Math.Max(c.G, c.B)) <= config.OutlineValueMax)
        {
            return c; // the readability contract: outlines are never tinted, ever
        }
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float lum = 0.299f * r + 0.587f * g + 0.114f * b;
        r *= 1f + (plan.AmbientR * weatherMod - 1f) * plan.AmbientStrength;
        g *= 1f + (plan.AmbientG * weatherMod - 1f) * plan.AmbientStrength;
        b *= 1f + (plan.AmbientB * weatherMod - 1f) * plan.AmbientStrength;
        float capT = plan.CapStrength
            * SmoothStep(config.CapLumKneeLow, config.CapLumKneeHigh, lum);
        r += (plan.CapR - r) * capT;
        g += (plan.CapG - g) * capT;
        b += (plan.CapB - b) * capT;
        return (ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>The fighters' global tint multiplier: white blended toward ambient
    /// by the (capped) fighter tint — composes UNDER state tints, which stay the
    /// game's readability vocabulary.</summary>
    public static (float R, float G, float B) FighterTint(LightRigPlan plan) => (
        1f + (plan.AmbientR - 1f) * plan.FighterTint,
        1f + (plan.AmbientG - 1f) * plan.FighterTint,
        1f + (plan.AmbientB - 1f) * plan.FighterTint);

    /// <summary>Weather's ambient nudge, hard-clamped to the amplitude cap: embers
    /// warm (+), precipitation and ash darken (−), scaled by the live gate x
    /// intensity. The rig stays static absent weather.</summary>
    public static float WeatherModulation(LightRigPlan plan, string? weatherType,
        float gateTimesIntensity)
    {
        if (weatherType is null || plan.WeatherAmplitude <= 0f)
        {
            return 1f;
        }
        float sign = weatherType == "embers" ? 1f : -1f;
        float mod = sign * plan.WeatherAmplitude * Math.Clamp(gateTimesIntensity, 0f, 1f);
        return 1f + Math.Clamp(mod, -plan.WeatherAmplitude, plan.WeatherAmplitude);
    }

    private static float Mix(byte far, byte mid) => (0.6f * far + 0.4f * mid) / 255f;

    private static float Lum(byte r, byte g, byte b) =>
        (0.299f * r + 0.587f * g + 0.114f * b) / 255f;

    private static float Lum3((float R, float G, float B) c) =>
        0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    private static (float, float, float) Lift((float R, float G, float B) c)
    {
        float max = Math.Max(c.R, Math.Max(c.G, c.B));
        float lift = max <= 0f ? 1f : Math.Min(1f / max, 1.35f);
        // Lift toward white a step: brighten, desaturate a third of the way.
        float r = Math.Min(1f, c.R * lift + 0.08f);
        float g = Math.Min(1f, c.G * lift + 0.08f);
        float b = Math.Min(1f, c.B * lift + 0.08f);
        float lum = 0.299f * r + 0.587f * g + 0.114f * b;
        return (r + (lum - r) * 0.33f, g + (lum - g) * 0.33f, b + (lum - b) * 0.33f);
    }

    private static float SmoothStep(float lo, float hi, float x)
    {
        float t = Math.Clamp((x - lo) / Math.Max(1e-4f, hi - lo), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
