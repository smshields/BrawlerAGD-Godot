namespace BrawlerSim.Determinism;

/// <summary>
/// Minimal 2D float vector for sim code. Godot's Vector2 must never appear inside
/// BrawlerSim; this type carries the handful of operations the sim needs, with
/// semantics matching the Unity originals where genome rules depend on them.
/// </summary>
public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0f, 0f);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 v, float s) => new(v.X * s, v.Y * s);

    public float Length() => DetMath.Sqrt(X * X + Y * Y);

    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Linear interpolation with t clamped to [0, 1] (UnityEngine.Vector2.Lerp parity).</summary>
    public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
    {
        t = DetMath.Clamp(t, 0f, 1f);
        return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>
    /// Unsigned angle between two vectors in degrees, in [0, 180].
    /// UnityEngine.Vector2.Angle parity: returns 0 when either vector is ~zero.
    /// </summary>
    public static float AngleDeg(Vec2 a, Vec2 b)
    {
        float denominator = DetMath.Sqrt((a.X * a.X + a.Y * a.Y) * (b.X * b.X + b.Y * b.Y));
        if (denominator < 1e-15f)
        {
            return 0f;
        }
        float cos = DetMath.Clamp(Dot(a, b) / denominator, -1f, 1f);
        return DetMath.Acos(cos) * DetMath.RadToDeg;
    }
}
