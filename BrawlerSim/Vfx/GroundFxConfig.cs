using System.Text.Json;

namespace BrawlerSim.Vfx;

/// <summary>A landing dust burst, fully resolved from character mass/size and the
/// pre-tick impact speed. Count 0 = soft touchdown, emit nothing.</summary>
public readonly record struct LandingBurst(
    int Count, float Scale, float SpreadSpeed, float Opacity, float Lifetime);

/// <summary>A footstep puff, resolved from ground speed and character mass/size.
/// Count 0 = below the locomotion threshold, emit nothing.</summary>
public readonly record struct FootstepPuff(
    int Count, float Scale, float Opacity, float Lifetime);

/// <summary>
/// Ground FX tuning (godot/assets/ground_fx.json, hot-editable) plus the pure
/// magnitude math for footstep/landing dust (particle prototype, 2026-09-14).
/// View-only feature: the view detects landings/strides from sim state and calls
/// these formulas; nothing here feeds back into gameplay. The readability budget
/// (opacity/lifetime/count/event caps) is clamped at load against hard ceilings,
/// the WeatherConfig budget-language precedent — dust must never obscure fighters.
/// </summary>
public sealed class GroundFxConfig
{
    // Hard ceilings the json cannot exceed (clamped at load, never thrown).
    public const float OpacityCeiling = 0.4f;
    public const float LifetimeCeiling = 0.8f;
    public const int BurstCountCeiling = 32;
    public const int EventsPerFrameCeiling = 16;

    // ── landing (impact-energy model) ────────────────────────────────────────
    /// <summary>Below this impact speed (world units/s) a landing emits nothing —
    /// stepping off a one-cell ledge stays silent.</summary>
    public float LandingMinImpactSpeed { get; init; } = 3.0f;
    /// <summary>energy = mass x impactSpeed^2, normalized between these.</summary>
    public float LandingEnergyMin { get; init; } = 9.0f;
    public float LandingEnergyMax { get; init; } = 250.0f;
    public float LandingCountMin { get; init; } = 4f;
    public float LandingCountMax { get; init; } = 18f;
    public float LandingScaleMin { get; init; } = 1.1f;
    public float LandingScaleMax { get; init; } = 2.4f;
    /// <summary>Initial particle velocity in screen px/s at the reference framing.</summary>
    public float LandingSpreadSpeedMin { get; init; } = 40f;
    public float LandingSpreadSpeedMax { get; init; } = 140f;
    public float LandingOpacityMin { get; init; } = 0.22f;
    public float LandingOpacityMax { get; init; } = 0.35f;
    public float LandingLifeMin { get; init; } = 0.25f;
    public float LandingLifeMax { get; init; } = 0.5f;

    // ── footsteps (stride model) ─────────────────────────────────────────────
    /// <summary>Ground speed (world units/s) below which no footstep dust — idling
    /// and slow shuffles stay clean.</summary>
    public float FootstepSpeedMin { get; init; } = 1.5f;
    public float FootstepCountMin { get; init; } = 1f;
    public float FootstepCountMax { get; init; } = 3f;
    public float FootstepScaleMin { get; init; } = 0.7f;
    public float FootstepScaleMax { get; init; } = 1.2f;
    public float FootstepOpacityMin { get; init; } = 0.14f;
    public float FootstepOpacityMax { get; init; } = 0.26f;
    public float FootstepLifeMin { get; init; } = 0.2f;
    public float FootstepLifeMax { get; init; } = 0.35f;
    /// <summary>Stride length = bodyWidth x strideFactor — bigger characters take
    /// longer strides, so cadence reads proportional to the body.</summary>
    public float StrideFactor { get; init; } = 1.6f;

    // ── body normalization ───────────────────────────────────────────────────
    /// <summary>Reference body half extents (world units) at which width/size
    /// factors are 1 — roughly the default fighter.</summary>
    public float RefBodyHalfX { get; init; } = 0.4f;
    public float RefBodyHalfY { get; init; } = 0.6f;
    /// <summary>Footstep count multiplier from mass: clamp(mass / 1.5, lo, hi).</summary>
    public float MassFactorLo { get; init; } = 0.7f;
    public float MassFactorHi { get; init; } = 1.3f;

    // ── readability budget (clamped at load) ─────────────────────────────────
    public float OpacityCap { get; init; } = 0.35f;
    public float LifetimeCap { get; init; } = 0.6f;
    /// <summary>Particles per burst ceiling — also the pool emitters' fixed
    /// allocation (the view varies visible count via AmountRatio).</summary>
    public int MaxBurstCount { get; init; } = 24;
    /// <summary>Event cap per rendered frame — bounds fast-forward storms.</summary>
    public int MaxEventsPerFrame { get; init; } = 8;
    /// <summary>How far dust color may blend toward the active weather's particle
    /// color at full gate x intensity.</summary>
    public float WeatherBlendMax { get; init; } = 0.7f;

    /// <summary>Stride distance (world units) between footstep puffs.</summary>
    public float StrideLength(float bodyHalfX) => bodyHalfX * 2f * StrideFactor;

    /// <summary>Landing burst from the impact-energy model. impactSpeed is the
    /// PRE-tick |Velocity.Y| — physics zeroes it inside the landing tick.</summary>
    public LandingBurst ComputeLanding(
        float mass, float impactSpeed, float bodyHalfX, float bodyHalfY)
    {
        if (impactSpeed < LandingMinImpactSpeed)
        {
            return default;
        }
        float energy = mass * impactSpeed * impactSpeed;
        float e01 = Math.Clamp(
            (energy - LandingEnergyMin) / (LandingEnergyMax - LandingEnergyMin), 0f, 1f);
        float widthFactor = Math.Clamp(bodyHalfX / RefBodyHalfX, 0.6f, 1.6f);
        int count = Math.Min(MaxBurstCount,
            (int)MathF.Round(Lerp(LandingCountMin, LandingCountMax, e01) * widthFactor));
        return new LandingBurst(
            count,
            Lerp(LandingScaleMin, LandingScaleMax, e01) * SizeFactor(bodyHalfY),
            Lerp(LandingSpreadSpeedMin, LandingSpreadSpeedMax, e01),
            Math.Min(OpacityCap, Lerp(LandingOpacityMin, LandingOpacityMax, e01)),
            Math.Min(LifetimeCap, Lerp(LandingLifeMin, LandingLifeMax, e01)));
    }

    /// <summary>Footstep puff from ground speed (normalized to the character's own
    /// max ground speed) and mass.</summary>
    public FootstepPuff ComputeFootstep(
        float mass, float absVelX, float maxGroundSpeed, float bodyHalfY)
    {
        if (absVelX < FootstepSpeedMin)
        {
            return default;
        }
        float speed01 = Math.Clamp(
            (absVelX - FootstepSpeedMin) / MathF.Max(0.1f, maxGroundSpeed - FootstepSpeedMin),
            0f, 1f);
        float massFactor = Math.Clamp(mass / 1.5f, MassFactorLo, MassFactorHi);
        int count = Math.Min(MaxBurstCount,
            (int)MathF.Round(Lerp(FootstepCountMin, FootstepCountMax, speed01) * massFactor));
        return new FootstepPuff(
            count,
            Lerp(FootstepScaleMin, FootstepScaleMax, speed01) * SizeFactor(bodyHalfY),
            Math.Min(OpacityCap, Lerp(FootstepOpacityMin, FootstepOpacityMax, speed01)),
            Math.Min(LifetimeCap, Lerp(FootstepLifeMin, FootstepLifeMax, speed01)));
    }

    private float SizeFactor(float bodyHalfY) =>
        Math.Clamp(bodyHalfY / RefBodyHalfY, 0.7f, 1.5f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static GroundFxConfig Parse(string json)
    {
        Doc doc = JsonSerializer.Deserialize<Doc>(json, Serialization.JsonOptions.Tuning)
            ?? throw new JsonException("ground fx config parsed to null.");
        var fallback = new GroundFxConfig();
        return new GroundFxConfig
        {
            LandingMinImpactSpeed = doc.LandingMinImpactSpeed ?? fallback.LandingMinImpactSpeed,
            LandingEnergyMin = doc.LandingEnergy?.Count == 2 ? doc.LandingEnergy[0] : fallback.LandingEnergyMin,
            LandingEnergyMax = doc.LandingEnergy?.Count == 2 ? doc.LandingEnergy[1] : fallback.LandingEnergyMax,
            LandingCountMin = doc.LandingCount?.Count == 2 ? doc.LandingCount[0] : fallback.LandingCountMin,
            LandingCountMax = doc.LandingCount?.Count == 2 ? doc.LandingCount[1] : fallback.LandingCountMax,
            LandingScaleMin = doc.LandingScale?.Count == 2 ? doc.LandingScale[0] : fallback.LandingScaleMin,
            LandingScaleMax = doc.LandingScale?.Count == 2 ? doc.LandingScale[1] : fallback.LandingScaleMax,
            LandingSpreadSpeedMin = doc.LandingSpreadSpeed?.Count == 2 ? doc.LandingSpreadSpeed[0] : fallback.LandingSpreadSpeedMin,
            LandingSpreadSpeedMax = doc.LandingSpreadSpeed?.Count == 2 ? doc.LandingSpreadSpeed[1] : fallback.LandingSpreadSpeedMax,
            LandingOpacityMin = doc.LandingOpacity?.Count == 2 ? doc.LandingOpacity[0] : fallback.LandingOpacityMin,
            LandingOpacityMax = doc.LandingOpacity?.Count == 2 ? doc.LandingOpacity[1] : fallback.LandingOpacityMax,
            LandingLifeMin = doc.LandingLife?.Count == 2 ? doc.LandingLife[0] : fallback.LandingLifeMin,
            LandingLifeMax = doc.LandingLife?.Count == 2 ? doc.LandingLife[1] : fallback.LandingLifeMax,
            FootstepSpeedMin = doc.FootstepSpeedMin ?? fallback.FootstepSpeedMin,
            FootstepCountMin = doc.FootstepCount?.Count == 2 ? doc.FootstepCount[0] : fallback.FootstepCountMin,
            FootstepCountMax = doc.FootstepCount?.Count == 2 ? doc.FootstepCount[1] : fallback.FootstepCountMax,
            FootstepScaleMin = doc.FootstepScale?.Count == 2 ? doc.FootstepScale[0] : fallback.FootstepScaleMin,
            FootstepScaleMax = doc.FootstepScale?.Count == 2 ? doc.FootstepScale[1] : fallback.FootstepScaleMax,
            FootstepOpacityMin = doc.FootstepOpacity?.Count == 2 ? doc.FootstepOpacity[0] : fallback.FootstepOpacityMin,
            FootstepOpacityMax = doc.FootstepOpacity?.Count == 2 ? doc.FootstepOpacity[1] : fallback.FootstepOpacityMax,
            FootstepLifeMin = doc.FootstepLife?.Count == 2 ? doc.FootstepLife[0] : fallback.FootstepLifeMin,
            FootstepLifeMax = doc.FootstepLife?.Count == 2 ? doc.FootstepLife[1] : fallback.FootstepLifeMax,
            StrideFactor = doc.StrideFactor ?? fallback.StrideFactor,
            RefBodyHalfX = doc.RefBodyHalfX ?? fallback.RefBodyHalfX,
            RefBodyHalfY = doc.RefBodyHalfY ?? fallback.RefBodyHalfY,
            MassFactorLo = doc.MassFactorRange?.Count == 2 ? doc.MassFactorRange[0] : fallback.MassFactorLo,
            MassFactorHi = doc.MassFactorRange?.Count == 2 ? doc.MassFactorRange[1] : fallback.MassFactorHi,
            // The readability budget clamps against the hard ceilings — a hot-edit
            // can lower the caps, never raise them past legibility.
            OpacityCap = Math.Min(OpacityCeiling, doc.OpacityCap ?? fallback.OpacityCap),
            LifetimeCap = Math.Min(LifetimeCeiling, doc.LifetimeCap ?? fallback.LifetimeCap),
            MaxBurstCount = Math.Min(BurstCountCeiling, doc.MaxBurstCount ?? fallback.MaxBurstCount),
            MaxEventsPerFrame = Math.Min(EventsPerFrameCeiling, doc.MaxEventsPerFrame ?? fallback.MaxEventsPerFrame),
            WeatherBlendMax = Math.Clamp(doc.WeatherBlendMax ?? fallback.WeatherBlendMax, 0f, 1f),
        };
    }

    public static GroundFxConfig LoadFile(string path) => Parse(File.ReadAllText(path));

    private sealed class Doc
    {
        public float? LandingMinImpactSpeed { get; set; }
        public List<float>? LandingEnergy { get; set; }
        public List<float>? LandingCount { get; set; }
        public List<float>? LandingScale { get; set; }
        public List<float>? LandingSpreadSpeed { get; set; }
        public List<float>? LandingOpacity { get; set; }
        public List<float>? LandingLife { get; set; }
        public float? FootstepSpeedMin { get; set; }
        public List<float>? FootstepCount { get; set; }
        public List<float>? FootstepScale { get; set; }
        public List<float>? FootstepOpacity { get; set; }
        public List<float>? FootstepLife { get; set; }
        public float? StrideFactor { get; set; }
        public float? RefBodyHalfX { get; set; }
        public float? RefBodyHalfY { get; set; }
        public List<float>? MassFactorRange { get; set; }
        public float? OpacityCap { get; set; }
        public float? LifetimeCap { get; set; }
        public int? MaxBurstCount { get; set; }
        public int? MaxEventsPerFrame { get; set; }
        public float? WeatherBlendMax { get; set; }
    }
}
