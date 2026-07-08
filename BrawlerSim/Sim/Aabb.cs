using BrawlerSim.Determinism;

namespace BrawlerSim.Sim;

/// <summary>Axis-aligned box: center + half extents. The sim's only collision shape.</summary>
public readonly record struct Aabb(Vec2 Center, Vec2 Half)
{
    public float Left => Center.X - Half.X;
    public float Right => Center.X + Half.X;
    public float Bottom => Center.Y - Half.Y;
    public float Top => Center.Y + Half.Y;

    public static Aabb FromRect(float x, float y, float width, float height) =>
        new(new Vec2(x + width / 2f, y + height / 2f), new Vec2(width / 2f, height / 2f));

    public bool Overlaps(in Aabb other) =>
        Left < other.Right && Right > other.Left && Bottom < other.Top && Top > other.Bottom;

    public bool Contains(Vec2 point) =>
        point.X >= Left && point.X <= Right && point.Y >= Bottom && point.Y <= Top;

    public Vec2 ClosestPoint(Vec2 point) =>
        new(DetMath.Clamp(point.X, Left, Right), DetMath.Clamp(point.Y, Bottom, Top));

    /// <summary>Penetration depths when overlapping (positive on both axes).</summary>
    public Vec2 Penetration(in Aabb other) =>
        new(
            Half.X + other.Half.X - DetMath.Abs(Center.X - other.Center.X),
            Half.Y + other.Half.Y - DetMath.Abs(Center.Y - other.Center.Y));
}
