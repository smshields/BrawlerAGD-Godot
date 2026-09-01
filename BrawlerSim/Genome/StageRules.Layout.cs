using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>StageRules — mirror transform, connectivity, and platform overlap
/// predicates. Split from StageRules.cs 2026-09-01, pure text move.</summary>
public static partial class StageRules
{
    /// <summary>
    /// The asymmetric→mirrored mutation transform (designer 2026-07-21): keep the
    /// chosen half's platforms (clamping any that cross x = 0 to the axis), then
    /// reflect them. Degenerate case — no platform mass on the chosen side — returns
    /// null and the caller falls back to full regeneration.
    /// </summary>
    public static List<PlatformGene>? MirrorTransform(
        IReadOnlyList<PlatformGene> platforms, bool rightSideIsSource)
    {
        var source = new List<PlatformGene>();
        foreach (PlatformGene p in platforms)
        {
            int left = p.X;
            int right = p.X + p.XSize;
            // Clamp to the source half; drop platforms fully on the other side.
            int clampedLeft = rightSideIsSource ? Math.Max(left, 0) : left;
            int clampedRight = rightSideIsSource ? right : Math.Min(right, 0);
            if (clampedRight - clampedLeft < 1)
            {
                continue;
            }
            source.Add(p with { X = clampedLeft, XSize = clampedRight - clampedLeft });
        }
        if (source.Count == 0)
        {
            return null;
        }
        var result = new List<PlatformGene>(source.Count * 2);
        result.AddRange(source);
        foreach (PlatformGene p in source)
        {
            PlatformGene mirror = p.MirrorX();
            // A platform ending exactly on the axis mirrors to one starting on it —
            // adjacency, not overlap; only skip exact duplicates (zero-width source
            // halves cannot occur, the clamp guarantees XSize ≥ 1).
            if (mirror != p)
            {
                result.Add(mirror);
            }
        }
        // Thin Platforms (2026-09-01): the chosen half may have held no solid
        // platform — repair keeps the at-least-one-solid rule (mirror-twin aware,
        // so the transformed layout stays symmetric).
        return EnsureSolidPlatform(result);
    }

    /// <summary>
    /// The abstract traversability check (designer option a, 2026-07-21): platforms
    /// form a connected graph where two platforms are adjacent when their rects,
    /// expanded by the jump grid reach, overlap. Used by generator property tests and
    /// available as the option-b hook's baseline.
    /// </summary>
    public static bool IsConnected(IReadOnlyList<PlatformGene> platforms, int jumpHeight, int jumpLength)
    {
        if (platforms.Count <= 1)
        {
            return true;
        }
        // Reach: legacy Left placement allows a horizontal gap up to jumpLength and a
        // vertical offset up to jumpHeight + platform thickness; Above allows a rise
        // of jumpHeight above the parent top. Expansion by (jumpLength, jumpHeight + 2)
        // is the loosest rect containment of those legacy placements.
        int reachX = jumpLength;
        int reachY = jumpHeight + 2;
        var visited = new bool[platforms.Count];
        var queue = new Queue<int>();
        visited[0] = true;
        queue.Enqueue(0);
        int seen = 1;
        while (queue.Count > 0)
        {
            PlatformGene a = platforms[queue.Dequeue()];
            for (int i = 0; i < platforms.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }
                PlatformGene b = platforms[i];
                // Non-strict: a legacy mirrored stage's seam gap is exactly jumpLength
                // (midGap per side), and jumpLength IS the reachable hop by definition.
                bool xTouch = a.X - reachX <= b.X + b.XSize && b.X - reachX <= a.X + a.XSize;
                bool yTouch = a.Y - reachY <= b.Y + b.YSize && b.Y - reachY <= a.Y + a.YSize;
                if (xTouch && yTouch)
                {
                    visited[i] = true;
                    seen++;
                    queue.Enqueue(i);
                }
            }
        }
        return seen == platforms.Count;
    }

    /// <summary>A layout is symmetric when every platform's mirror is also present
    /// (as a set — generation order does not matter).</summary>
    public static bool IsSymmetric(IReadOnlyList<PlatformGene> platforms)
    {
        var set = new HashSet<PlatformGene>(platforms);
        foreach (PlatformGene p in platforms)
        {
            if (!set.Contains(p.MirrorX()))
            {
                return false;
            }
        }
        return true;
    }

    public static bool Overlaps(in PlatformGene a, in PlatformGene b) =>
        a.X < b.X + b.XSize && b.X < a.X + a.XSize
        && a.Y < b.Y + b.YSize && b.Y < a.Y + a.YSize;

    public static bool OverlapsAny(in PlatformGene candidate, IReadOnlyList<PlatformGene> platforms)
    {
        foreach (PlatformGene p in platforms)
        {
            if (Overlaps(candidate, p))
            {
                return true;
            }
        }
        return false;
    }
}
