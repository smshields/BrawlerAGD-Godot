using System.Text.Json;

namespace BrawlerSim.Vfx;

/// <summary>A landing dust burst, fully resolved from character mass/size and the
/// pre-tick impact speed. Count 0 = soft touchdown, emit nothing. Scale is the
/// FINAL size — particles are born at ScaleBirthFraction of it and grow into it
/// while they fade.</summary>
public readonly record struct LandingBurst(
    int Count, float Scale, float SpreadSpeed, float Opacity, float Lifetime);

/// <summary>A footstep puff, resolved from ground speed and character mass/size.
/// Count 0 = below the locomotion threshold, emit nothing. Scale is the FINAL
/// size (see LandingBurst).</summary>
public readonly record struct FootstepPuff(
    int Count, float Scale, float SpreadSpeed, float Opacity, float Lifetime);

/// <summary>
/// Ground FX tuning (godot/assets/ground_fx.json, hot-editable) plus the pure
/// magnitude math for footstep/landing dust (particle prototype, 2026-09-14;
/// visibility pass 2026-09-15). View-only feature: the view detects landings and
/// strides from sim state and calls these formulas; nothing here feeds back into
/// gameplay. The readability budget (opacity/lifetime/count/event caps) is clamped
/// at load against hard ceilings, the WeatherConfig budget-language precedent —
/// dust must read clearly without obscuring the fighters it draws behind.
/// </summary>
public sealed class GroundFxConfig
{
    // Hard ceilings the json cannot exceed (clamped at load, never thrown).
    public const float OpacityCeiling = 0.45f;
    public const float LifetimeCeiling = 1.2f;
    public const int BurstCountCeiling = 64;
    public const int EventsPerFrameCeiling = 16;

    // ── landing (impact-energy model) ────────────────────────────────────────
    /// <summary>Below this impact speed (world units/s) a landing emits nothing —
    /// stepping off a one-cell ledge stays silent.</summary>
    public float LandingMinImpactSpeed { get; init; } = 3.0f;
    /// <summary>energy = mass x impactSpeed^2, normalized between these.</summary>
    public float LandingEnergyMin { get; init; } = 9.0f;
    public float LandingEnergyMax { get; init; } = 250.0f;
    /// <summary>Particles per burst, split across the two counter-platform fans.
    /// Dense enough to read as one continuous cloud, not separable dots.</summary>
    public float LandingCountMin { get; init; } = 14f;
    public float LandingCountMax { get; init; } = 40f;
    /// <summary>FINAL particle size (born small, grows into this) — force-driven.</summary>
    public float LandingScaleMin { get; init; } = 1.3f;
    public float LandingScaleMax { get; init; } = 3.2f;
    /// <summary>Initial particle velocity in screen px/s at the reference framing.</summary>
    public float LandingSpreadSpeedMin { get; init; } = 70f;
    public float LandingSpreadSpeedMax { get; init; } = 220f;
    public float LandingOpacityMin { get; init; } = 0.24f;
    public float LandingOpacityMax { get; init; } = 0.38f;
    /// <summary>Lifetime = the grow-and-fade window; harder impacts hang longer.</summary>
    public float LandingLifeMin { get; init; } = 0.35f;
    public float LandingLifeMax { get; init; } = 0.8f;

    // ── footsteps (stride model) ─────────────────────────────────────────────
    /// <summary>Ground speed (world units/s) below which no footstep dust — idling
    /// and slow shuffles stay clean.</summary>
    public float FootstepSpeedMin { get; init; } = 1.5f;
    public float FootstepCountMin { get; init; } = 3f;
    public float FootstepCountMax { get; init; } = 7f;
    public float FootstepScaleMin { get; init; } = 0.8f;
    public float FootstepScaleMax { get; init; } = 1.6f;
    public float FootstepSpreadSpeedMin { get; init; } = 35f;
    public float FootstepSpreadSpeedMax { get; init; } = 90f;
    public float FootstepOpacityMin { get; init; } = 0.16f;
    public float FootstepOpacityMax { get; init; } = 0.30f;
    public float FootstepLifeMin { get; init; } = 0.28f;
    public float FootstepLifeMax { get; init; } = 0.5f;
    /// <summary>Stride length = bodyWidth x strideFactor — bigger characters take
    /// longer strides, so cadence reads proportional to the body. Short enough
    /// that consecutive puffs overlap into a continuous trail at running speed.</summary>
    public float StrideFactor { get; init; } = 0.85f;

    // ── shape over lifetime ──────────────────────────────────────────────────
    /// <summary>Particles are born at this fraction of their final size and grow
    /// into it across the lifetime (dust expands as it disperses).</summary>
    public float ScaleBirthFraction { get; init; } = 0.22f;
    /// <summary>Alpha holds until this fraction of the lifetime, then fades to
    /// zero — so the fade-out window inherits the force/velocity-driven life.</summary>
    public float FadeStartFraction { get; init; } = 0.4f;
    /// <summary>Landings fire TWO fans this many degrees above the platform
    /// surface (left and right) — the counter-platform hemisphere, biased
    /// sideways so the cloud spreads along the ground instead of puffing up.</summary>
    public float LandingFanTiltDeg { get; init; } = 22f;
    /// <summary>Cone width of each landing fan; two fans at the tilt above cover
    /// every direction away from the platform.</summary>
    public float LandingFanSpreadDeg { get; init; } = 55f;
    /// <summary>Footstep dust kicks BACKWARD (opposite the movement) at this angle
    /// above the ground.</summary>
    public float FootstepBackTiltDeg { get; init; } = 28f;
    /// <summary>Cone width of the backward footstep kick — the randomness that
    /// scatters the puff into opposing directions.</summary>
    public float FootstepSpreadDeg { get; init; } = 45f;

    // ── body normalization ───────────────────────────────────────────────────
    /// <summary>Reference body half extents (world units) at which width/size
    /// factors are 1 — roughly the default fighter.</summary>
    public float RefBodyHalfX { get; init; } = 0.4f;
    public float RefBodyHalfY { get; init; } = 0.6f;
    /// <summary>Footstep count multiplier from mass: clamp(mass / 1.5, lo, hi).</summary>
    public float MassFactorLo { get; init; } = 0.7f;
    public float MassFactorHi { get; init; } = 1.3f;

    // ── readability budget (clamped at load) ─────────────────────────────────
    public float OpacityCap { get; init; } = 0.4f;
    public float LifetimeCap { get; init; } = 0.9f;
    /// <summary>Particles per burst ceiling — also the pool emitters' fixed
    /// allocation (the view varies visible count via AmountRatio).</summary>
    public int MaxBurstCount { get; init; } = 48;
    /// <summary>Event cap per rendered frame — bounds fast-forward storms.</summary>
    public int MaxEventsPerFrame { get; init; } = 10;
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
            Lerp(FootstepSpreadSpeedMin, FootstepSpreadSpeedMax, speed01),
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
            LandingEnergyMin = Pair(doc.LandingEnergy, 0, fallback.LandingEnergyMin),
            LandingEnergyMax = Pair(doc.LandingEnergy, 1, fallback.LandingEnergyMax),
            LandingCountMin = Pair(doc.LandingCount, 0, fallback.LandingCountMin),
            LandingCountMax = Pair(doc.LandingCount, 1, fallback.LandingCountMax),
            LandingScaleMin = Pair(doc.LandingScale, 0, fallback.LandingScaleMin),
            LandingScaleMax = Pair(doc.LandingScale, 1, fallback.LandingScaleMax),
            LandingSpreadSpeedMin = Pair(doc.LandingSpreadSpeed, 0, fallback.LandingSpreadSpeedMin),
            LandingSpreadSpeedMax = Pair(doc.LandingSpreadSpeed, 1, fallback.LandingSpreadSpeedMax),
            LandingOpacityMin = Pair(doc.LandingOpacity, 0, fallback.LandingOpacityMin),
            LandingOpacityMax = Pair(doc.LandingOpacity, 1, fallback.LandingOpacityMax),
            LandingLifeMin = Pair(doc.LandingLife, 0, fallback.LandingLifeMin),
            LandingLifeMax = Pair(doc.LandingLife, 1, fallback.LandingLifeMax),
            FootstepSpeedMin = doc.FootstepSpeedMin ?? fallback.FootstepSpeedMin,
            FootstepCountMin = Pair(doc.FootstepCount, 0, fallback.FootstepCountMin),
            FootstepCountMax = Pair(doc.FootstepCount, 1, fallback.FootstepCountMax),
            FootstepScaleMin = Pair(doc.FootstepScale, 0, fallback.FootstepScaleMin),
            FootstepScaleMax = Pair(doc.FootstepScale, 1, fallback.FootstepScaleMax),
            FootstepSpreadSpeedMin = Pair(doc.FootstepSpreadSpeed, 0, fallback.FootstepSpreadSpeedMin),
            FootstepSpreadSpeedMax = Pair(doc.FootstepSpreadSpeed, 1, fallback.FootstepSpreadSpeedMax),
            FootstepOpacityMin = Pair(doc.FootstepOpacity, 0, fallback.FootstepOpacityMin),
            FootstepOpacityMax = Pair(doc.FootstepOpacity, 1, fallback.FootstepOpacityMax),
            FootstepLifeMin = Pair(doc.FootstepLife, 0, fallback.FootstepLifeMin),
            FootstepLifeMax = Pair(doc.FootstepLife, 1, fallback.FootstepLifeMax),
            StrideFactor = doc.StrideFactor ?? fallback.StrideFactor,
            ScaleBirthFraction = Math.Clamp(
                doc.ScaleBirthFraction ?? fallback.ScaleBirthFraction, 0.02f, 1f),
            FadeStartFraction = Math.Clamp(
                doc.FadeStartFraction ?? fallback.FadeStartFraction, 0f, 0.95f),
            LandingFanTiltDeg = doc.LandingFanTiltDeg ?? fallback.LandingFanTiltDeg,
            LandingFanSpreadDeg = doc.LandingFanSpreadDeg ?? fallback.LandingFanSpreadDeg,
            FootstepBackTiltDeg = doc.FootstepBackTiltDeg ?? fallback.FootstepBackTiltDeg,
            FootstepSpreadDeg = doc.FootstepSpreadDeg ?? fallback.FootstepSpreadDeg,
            RefBodyHalfX = doc.RefBodyHalfX ?? fallback.RefBodyHalfX,
            RefBodyHalfY = doc.RefBodyHalfY ?? fallback.RefBodyHalfY,
            MassFactorLo = Pair(doc.MassFactorRange, 0, fallback.MassFactorLo),
            MassFactorHi = Pair(doc.MassFactorRange, 1, fallback.MassFactorHi),
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

    private static float Pair(List<float>? pair, int index, float fallback) =>
        pair is { Count: 2 } ? pair[index] : fallback;

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
        public List<float>? FootstepSpreadSpeed { get; set; }
        public List<float>? FootstepOpacity { get; set; }
        public List<float>? FootstepLife { get; set; }
        public float? StrideFactor { get; set; }
        public float? ScaleBirthFraction { get; set; }
        public float? FadeStartFraction { get; set; }
        public float? LandingFanTiltDeg { get; set; }
        public float? LandingFanSpreadDeg { get; set; }
        public float? FootstepBackTiltDeg { get; set; }
        public float? FootstepSpreadDeg { get; set; }
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
