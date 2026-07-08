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
}
