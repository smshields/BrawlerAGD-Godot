using BrawlerSim.Determinism;

namespace BrawlerSim.Sim;

/// <summary>
/// Exact 2D overlap tests for the projectile hitbox shapes (2026-07-14,
/// FEATURES.md §Projectiles) against player AABBs. Shape is a HITBOX property, not
/// decoration: a rotating square really is an OBB, a triangle really is a triangle
/// (SAT in both cases). All trig through DetMath; no allocation.
/// </summary>
public static class SimShapes
{
    /// <summary>Circle vs AABB: closest-point distance.</summary>
    public static bool CircleOverlapsAabb(Vec2 center, float radius, Aabb box)
    {
        Vec2 closest = box.ClosestPoint(center);
        float dx = closest.X - center.X;
        float dy = closest.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>Square of half-extent h rotated by angle vs AABB — SAT over the two
    /// box axes and the two square axes. Angle 0 reduces to AABB-vs-AABB.</summary>
    public static bool RotatedSquareOverlapsAabb(Vec2 center, float halfExtent, float angle, Aabb box)
    {
        float cos = DetMath.Cos(angle);
        float sin = DetMath.Sin(angle);
        Span<Vec2> corners = stackalloc Vec2[4];
        corners[0] = center + Rotate(new Vec2(+halfExtent, +halfExtent), cos, sin);
        corners[1] = center + Rotate(new Vec2(-halfExtent, +halfExtent), cos, sin);
        corners[2] = center + Rotate(new Vec2(-halfExtent, -halfExtent), cos, sin);
        corners[3] = center + Rotate(new Vec2(+halfExtent, -halfExtent), cos, sin);
        Span<Vec2> axes = stackalloc Vec2[4];
        axes[0] = new Vec2(1f, 0f);
        axes[1] = new Vec2(0f, 1f);
        axes[2] = new Vec2(cos, sin);
        axes[3] = new Vec2(-sin, cos);
        return SatOverlap(corners, axes, box);
    }

    /// <summary>Equilateral triangle (circumradius r, one vertex along +X at angle 0)
    /// rotated by angle vs AABB — SAT over the box axes and the three edge normals.</summary>
    public static bool TriangleOverlapsAabb(Vec2 center, float circumradius, float angle, Aabb box)
    {
        const float TwoThirdsPi = 2.0943951f; // 2π/3
        Span<Vec2> vertices = stackalloc Vec2[3];
        for (int k = 0; k < 3; k++)
        {
            float a = angle + k * TwoThirdsPi;
            vertices[k] = center + new Vec2(DetMath.Cos(a), DetMath.Sin(a)) * circumradius;
        }
        Span<Vec2> axes = stackalloc Vec2[5];
        axes[0] = new Vec2(1f, 0f);
        axes[1] = new Vec2(0f, 1f);
        for (int k = 0; k < 3; k++)
        {
            Vec2 edge = vertices[(k + 1) % 3] - vertices[k];
            axes[2 + k] = new Vec2(-edge.Y, edge.X); // unnormalized is fine for SAT
        }
        return SatOverlap(vertices, axes, box);
    }

    private static Vec2 Rotate(Vec2 v, float cos, float sin) =>
        new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

    /// <summary>Separating-axis test between a convex polygon and an AABB: overlap iff
    /// the projected intervals intersect on every candidate axis.</summary>
    private static bool SatOverlap(ReadOnlySpan<Vec2> polygon, ReadOnlySpan<Vec2> axes, Aabb box)
    {
        Span<Vec2> boxCorners = stackalloc Vec2[4];
        boxCorners[0] = new Vec2(box.Left, box.Bottom);
        boxCorners[1] = new Vec2(box.Left, box.Top);
        boxCorners[2] = new Vec2(box.Right, box.Bottom);
        boxCorners[3] = new Vec2(box.Right, box.Top);
        foreach (Vec2 axis in axes)
        {
            Project(polygon, axis, out float minA, out float maxA);
            Project(boxCorners, axis, out float minB, out float maxB);
            if (maxA < minB || maxB < minA)
            {
                return false;
            }
        }
        return true;
    }

    private static void Project(ReadOnlySpan<Vec2> points, Vec2 axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (Vec2 p in points)
        {
            float d = p.X * axis.X + p.Y * axis.Y;
            min = MathF.Min(min, d);
            max = MathF.Max(max, d);
        }
    }
}
