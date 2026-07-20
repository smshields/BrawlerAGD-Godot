using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>Stable param keys for the character schema. Keys are the game.json contract.</summary>
public static class CharacterParams
{
    public const string MaxGroundSpeed = "maxGroundSpeed";
    public const string MaxAirSpeed = "maxAirSpeed";
    public const string GroundAccelerationFactor = "groundAccelerationFactor";
    public const string AirAccelerationFactor = "airAccelerationFactor";
    public const string GroundJumpForce = "groundJumpForce";
    public const string AirJumpForce = "airJumpForce";
    public const string Mass = "mass";
    public const string Drag = "drag";
    public const string WidthScalar = "widthScalar";
    public const string HeightScalar = "heightScalar";
    public const string GravityScalar = "gravityScalar";
    public const string HitstunDamageScalar = "hitstunDamageScalar";

    // Fast fall / crouch / DI (2026-07-13, FEATURES.md; appended — order is
    // crossover semantics). Loader defaults for pre-feature files:
    // GameGenomeJson.CharacterParamDefaults (all mechanics neutral/off).
    public const string FastFallAcceleration = "fastFallAcceleration";
    public const string CrouchAccelerationChange = "crouchAccelerationChange";
    public const string CrouchSpeed = "crouchSpeed";
    public const string CrouchMoveSpeed = "crouchMoveSpeed";
    public const string CrouchHeightRatio = "crouchHeightRatio";
    public const string DirectionalInfluence = "directionalInfluence";
    public const string DiKnockbackReduction = "diKnockbackReduction";
}

/// <summary>Stable param keys for the shield schema (2026-07-12, FEATURES.md §Shield).</summary>
public static class ShieldParams
{
    public const string WindUpDuration = "windUpDuration";
    public const string CoolDownDuration = "coolDownDuration";
    public const string InitialSize = "initialSize";
    public const string HoldDegradationRate = "holdDegradationRate";
    public const string HitDegradationScalar = "hitDegradationScalar";
    public const string KnockbackReduction = "knockbackReduction";
    public const string SpacingPush = "spacingPush";
    public const string RegenRate = "regenRate";
    public const string BreakStunDuration = "breakStunDuration";
}

/// <summary>Stable param keys for the dash schema (2026-07-13, FEATURES.md §Dash).</summary>
public static class DashParams
{
    public const string WindUpDuration = "windUpDuration";
    public const string Acceleration = "acceleration";
    public const string Duration = "duration";
    public const string WarmUpInvulnerable = "warmUpInvulnerable";
    public const string DurationInvulnerable = "durationInvulnerable";
}

/// <summary>Stable param keys for the projectile schema (2026-07-14,
/// FEATURES.md §Projectiles; docs/features/projectiles.md).</summary>
public static class ProjectileParams
{
    public const string PathShape = "pathShape";
    public const string PathScalar = "pathScalar";
    public const string TimeToDecay = "timeToDecay";
    public const string Velocity = "velocity";
    public const string DoesAccelerate = "doesAccelerate";
    public const string Acceleration = "acceleration";
    public const string AffectedByGravity = "affectedByGravity";
    public const string WarmUpDuration = "warmUpDuration";
    public const string ExecutionDuration = "executionDuration";
    public const string CoolDownDuration = "coolDownDuration";
    public const string HitboxSize = "hitboxSize";
    public const string HitboxShape = "hitboxShape";
    public const string DoesRotate = "doesRotate";
    public const string RotationRate = "rotationRate";
    public const string KnockbackScalar = "knockbackScalar";
    public const string KnockbackModX = "knockbackModX";
    public const string KnockbackModY = "knockbackModY";
    public const string DamageFactor = "damageFactor";
    public const string DamageDecay = "damageDecay";
    public const string DecayRate = "decayRate";
    public const string HitstunDuration = "hitstunDuration";
    public const string HitsSelf = "hitsSelf";
    public const string LaunchX = "launchX";
    public const string LaunchY = "launchY";
}

/// <summary>Stable param keys for the move schema.</summary>
public static class MoveParams
{
    public const string MoveDist = "moveDist";
    public const string MoveAngle = "moveAngle";
    public const string WidthScalar = "widthScalar";
    public const string HeightScalar = "heightScalar";
    public const string WarmUpDuration = "warmUpDuration";
    public const string ExecutionDuration = "executionDuration";
    public const string CoolDownDuration = "coolDownDuration";
    public const string DamageFactor = "damageFactor";
    public const string KnockbackScalar = "knockbackScalar";
    public const string KnockbackModX = "knockbackModX";
    public const string KnockbackModY = "knockbackModY";
    public const string HitstunDuration = "hitstunDuration";
}

/// <summary>
/// The base-game schemas, with ranges and ORDER carried over verbatim from the Unity
/// implementation (SerializedPlayer.ranges / SerializedMove.ranges) — order matters to
/// single-point crossover. Note: the AIIDE '22 paper's Table 1 lists cool-down as
/// 0.1–0.4, but the shipped code used 0.1–0.6; the code's range is authoritative here.
/// </summary>
public static class DefaultSchemas
{
    public static readonly ParamSchema Character = new("character", new[]
    {
        new ParamSpec(CharacterParams.MaxGroundSpeed, 2f, 10f),
        new ParamSpec(CharacterParams.MaxAirSpeed, 2f, 10f),
        new ParamSpec(CharacterParams.GroundAccelerationFactor, 0f, 1f),
        new ParamSpec(CharacterParams.AirAccelerationFactor, 0f, 1f),
        new ParamSpec(CharacterParams.GroundJumpForce, 1f, 15f),
        new ParamSpec(CharacterParams.AirJumpForce, 1f, 15f),
        new ParamSpec(CharacterParams.Mass, 0.5f, 2.5f),
        new ParamSpec(CharacterParams.Drag, 1f, 6f),
        new ParamSpec(CharacterParams.WidthScalar, 0.7f, 1.5f),
        new ParamSpec(CharacterParams.HeightScalar, 0.5f, 1.5f),
        new ParamSpec(CharacterParams.GravityScalar, 0.3f, 1.3f),
        new ParamSpec(CharacterParams.HitstunDamageScalar, 0.1f, 0.3f),
        // Appended 2026-07-13 (fastfall-crouch-di.md); append-only preserves the
        // crossover indexing of everything above.
        new ParamSpec(CharacterParams.FastFallAcceleration, 0f, 15f),
        new ParamSpec(CharacterParams.CrouchAccelerationChange, -8f, 8f),
        new ParamSpec(CharacterParams.CrouchSpeed, 0.05f, 0.2f),
        new ParamSpec(CharacterParams.CrouchMoveSpeed, 0.3f, 1.5f),
        new ParamSpec(CharacterParams.CrouchHeightRatio, 0.4f, 0.9f),
        // DI genes GENERATE in the live ranges but VALIDATE down to 0: the loader's
        // neutral default for pre-feature genomes is 0 = mechanic off (same
        // generation-vs-valid-domain split as knockbackModX, DEVIATIONS #13).
        new ParamSpec(CharacterParams.DirectionalInfluence, 0.02f, 0.10f) { ValidMin = 0f },
        new ParamSpec(CharacterParams.DiKnockbackReduction, 0.05f, 0.20f) { ValidMin = 0f },
    });

    public static readonly ParamSchema Move = new("move", new[]
    {
        new ParamSpec(MoveParams.MoveDist, 0.8f, 1.5f),
        new ParamSpec(MoveParams.MoveAngle, 0f, 2f * MathF.PI),
        new ParamSpec(MoveParams.WidthScalar, 0.5f, 1.5f),
        new ParamSpec(MoveParams.HeightScalar, 0.5f, 1.5f),
        new ParamSpec(MoveParams.WarmUpDuration, 0.1f, 0.6f),
        new ParamSpec(MoveParams.ExecutionDuration, 0.1f, 0.4f),
        new ParamSpec(MoveParams.CoolDownDuration, 0.1f, 0.6f),
        new ParamSpec(MoveParams.DamageFactor, 0f, 10f),
        new ParamSpec(MoveParams.KnockbackScalar, 1f, 16f),
        // Knockback components are GENERATED in the narrow ranges below, but the
        // generation-time constraint (MoveRules.ConstrainKnockback) lerps them toward the
        // hitbox location (components within ±moveDistMax = ±1.5), and Unity additionally
        // saved post-flip values into the study-game files. The valid domain is therefore
        // the convex hull of both endpoints: [-1.5, 1.5]. Verified by importer tests
        // against Games A–F.
        new ParamSpec(MoveParams.KnockbackModX, 0f, 1f) { ValidMin = -1.5f, ValidMax = 1.5f },
        new ParamSpec(MoveParams.KnockbackModY, -1f, 1f) { ValidMin = -1.5f, ValidMax = 1.5f },
        new ParamSpec(MoveParams.HitstunDuration, 0f, 1f),
    });

    /// <summary>
    /// Shield move type (2026-07-12, FEATURES.md; ranges designer-reviewed in
    /// docs/features/shield.md). Wind-up/cool-down deliberately shorter than attacks;
    /// InitialSize is the DIAMETER in world units ("no larger than 2× character
    /// size"); degradation/regen rates are radius-units per second; BreakStunDuration
    /// is EXEMPT from MatchConfig.MaxStunSeconds (the break is the counterweight).
    /// </summary>
    public static readonly ParamSchema Shield = new("shield", new[]
    {
        new ParamSpec(ShieldParams.WindUpDuration, 0.05f, 0.3f),
        new ParamSpec(ShieldParams.CoolDownDuration, 0.05f, 0.3f),
        new ParamSpec(ShieldParams.InitialSize, 0.5f, 2.0f),
        new ParamSpec(ShieldParams.HoldDegradationRate, 0.05f, 0.4f),
        new ParamSpec(ShieldParams.HitDegradationScalar, 0.01f, 0.06f),
        new ParamSpec(ShieldParams.KnockbackReduction, 0.5f, 0.9f),
        new ParamSpec(ShieldParams.SpacingPush, 0.5f, 3.0f),
        new ParamSpec(ShieldParams.RegenRate, 0.05f, 0.5f),
        new ParamSpec(ShieldParams.BreakStunDuration, 0.5f, 2.5f),
    });

    /// <summary>
    /// Dash move type (2026-07-13, FEATURES.md §Dash; docs/features/dash.md). No
    /// cool-down by design. With gravity suspended during travel, Acceleration IS the
    /// travel speed (u/s) held for Duration. The two invulnerability params are
    /// bools-as-floats (active ≥ 0.5) so they ride the normal ParamSet genetic ops.
    /// </summary>
    public static readonly ParamSchema Dash = new("dash", new[]
    {
        new ParamSpec(DashParams.WindUpDuration, 0.05f, 0.4f),
        new ParamSpec(DashParams.Acceleration, 6f, 18f),
        new ParamSpec(DashParams.Duration, 0.1f, 0.4f),
        new ParamSpec(DashParams.WarmUpInvulnerable, 0f, 1f),
        new ParamSpec(DashParams.DurationInvulnerable, 0f, 1f),
    });

    /// <summary>
    /// Projectile move type (2026-07-14, FEATURES.md §Projectiles;
    /// docs/features/projectiles.md — designer sketch is authoritative for path
    /// shapes). Bools ride as floats (active ≥ 0.5); the two SHAPE selectors are
    /// ints-as-floats (floor of the value, generated in [0, 3)). Knockback and
    /// damage genes mirror the melee move's semantics and ranges ("knockback
    /// calculation should match a melee attack"); FSM timings mirror the melee
    /// ranges. HitboxSize is a full extent in world units, capped below
    /// PlayerBaseWidth (0.74) per "never larger than the shooting character".
    /// Launch offsets are half-body fractions, clamped at resolve time so the
    /// spawn overlaps the player (the sketch's EXIT point).
    /// </summary>
    public static readonly ParamSchema Projectile = new("projectile", new[]
    {
        new ParamSpec(ProjectileParams.PathShape, 0f, 3f),      // floor → 0 linear, 1 sine, 2 quadratic
        new ParamSpec(ProjectileParams.PathScalar, 0.5f, 6f),   // sine freq (Hz) / quadratic curvature
        new ParamSpec(ProjectileParams.TimeToDecay, 0.5f, 4f),  // TTL seconds
        new ParamSpec(ProjectileParams.Velocity, 3f, 15f),
        new ParamSpec(ProjectileParams.DoesAccelerate, 0f, 1f),
        new ParamSpec(ProjectileParams.Acceleration, -10f, 10f),
        new ParamSpec(ProjectileParams.AffectedByGravity, 0f, 1f),
        new ParamSpec(ProjectileParams.WarmUpDuration, 0.1f, 0.6f),
        new ParamSpec(ProjectileParams.ExecutionDuration, 0.1f, 0.4f),
        new ParamSpec(ProjectileParams.CoolDownDuration, 0.1f, 0.6f),
        new ParamSpec(ProjectileParams.HitboxSize, 0.2f, 0.7f),
        new ParamSpec(ProjectileParams.HitboxShape, 0f, 3f),    // floor → 0 square, 1 circle, 2 triangle
        new ParamSpec(ProjectileParams.DoesRotate, 0f, 1f),
        new ParamSpec(ProjectileParams.RotationRate, 0.5f, 8f), // rad/s
        new ParamSpec(ProjectileParams.KnockbackScalar, 1f, 16f),
        // No ConstrainKnockback for projectiles (that lerp is hitbox-location-relative,
        // a melee concept) — so no widened valid domain either; genes stay as generated.
        new ParamSpec(ProjectileParams.KnockbackModX, 0f, 1f),
        new ParamSpec(ProjectileParams.KnockbackModY, -1f, 1f),
        new ParamSpec(ProjectileParams.DamageFactor, 0f, 10f),
        new ParamSpec(ProjectileParams.DamageDecay, 0f, 1f),
        new ParamSpec(ProjectileParams.DecayRate, 0.1f, 1f),    // damage-scale units per second
        new ParamSpec(ProjectileParams.HitstunDuration, 0f, 1f),
        new ParamSpec(ProjectileParams.HitsSelf, 0f, 1f),
        new ParamSpec(ProjectileParams.LaunchX, -0.5f, 0.5f),   // × body half extents
        new ParamSpec(ProjectileParams.LaunchY, -0.5f, 0.5f),
    });
}
