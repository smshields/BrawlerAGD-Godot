namespace BrawlerSim.Determinism;

/// <summary>
/// The single sanctioned entry point for transcendental math in sim code.
///
/// Basic IEEE 754 float arithmetic (+, -, *, /, sqrt) is bit-deterministic everywhere,
/// but Cos/Sin/Acos are library implementations that may differ across CPU architectures.
/// Routing every call site through this facade means that if the cross-platform
/// determinism test ever fails, the fix (table/polynomial implementations or fixed-point)
/// is a change to this one file, not a hunt through the codebase.
/// </summary>
public static class DetMath
{
    public const float Pi = MathF.PI;
    public const float RadToDeg = 180f / MathF.PI;

    public static float Cos(float radians) => MathF.Cos(radians);

    public static float Sin(float radians) => MathF.Sin(radians);

    public static float Acos(float value) => MathF.Acos(value);

    public static float Sqrt(float value) => MathF.Sqrt(value);

    public static float Abs(float value) => MathF.Abs(value);

    public static float Clamp(float value, float min, float max) =>
        value < min ? min : (value > max ? max : value);
}
