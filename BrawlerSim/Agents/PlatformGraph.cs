using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>
/// Per-character platform reachability graph (2026-07-10, designer design /
/// docs/features/utility-agent.md behavior log): nodes are the stage's platforms,
/// a directed edge A→B means this character's jumps can plausibly carry it from A
/// to B. Built ONCE per match (platforms and character constants never change), with
/// an all-pairs next-hop table (BFS per destination over ≤ a handful of nodes), so
/// per-decision route lookups are O(1). This is a SENSOR feeding UtilityContext —
/// not a planner: behaviors still score every input, the graph just tells Approach
/// which way "toward the opponent" actually is.
///
/// The hop test is the same coarse ballistic style as UtilityAgent.EstimateReachable:
/// chain both jumps for max rise, spend the flight time drifting at MaxAirSpeed.
/// Deterministic — fixed platform order, plain float arithmetic.
/// </summary>
public sealed class PlatformGraph
{
    private const float RangeSafety = 0.9f;      // demand 10% slack on hop range
    private const float StandTolerance = 0.75f;  // how far above a top "standing on it" reaches

    private readonly IReadOnlyList<Aabb> _platforms;
    private readonly int[,] _nextHop; // [from, to] → next platform index, -1 = no route

    public PlatformGraph(IReadOnlyList<Aabb> platforms, SimPlayer character, float gravity)
    {
        _platforms = platforms;
        int n = platforms.Count;
        var edges = new bool[n, n];
        float g = MathF.Max(0.01f, gravity * character.GravityScale);
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
            {
                edges[a, b] = a != b && HopFeasible(platforms[a], platforms[b], character, g);
            }
        }

        // Next-hop via BFS from every DESTINATION over reversed edges: the first move
        // of a shortest route from any node toward that destination.
        _nextHop = new int[n, n];
        var queue = new Queue<int>();
        for (int target = 0; target < n; target++)
        {
            for (int i = 0; i < n; i++)
            {
                _nextHop[i, target] = -1;
            }
            queue.Clear();
            queue.Enqueue(target);
            var visited = new bool[n];
            visited[target] = true;
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                for (int prev = 0; prev < n; prev++)
                {
                    if (!visited[prev] && edges[prev, node])
                    {
                        visited[prev] = true;
                        _nextHop[prev, target] = node;
                        queue.Enqueue(prev);
                    }
                }
            }
        }
    }

    public Aabb Platform(int index) => _platforms[index];

    /// <summary>The platform this position stands on / falls onto within tolerance:
    /// highest top ≤ y + tolerance whose x-span contains x. −1 = over nothing.</summary>
    public int PlatformAt(Vec2 position)
    {
        int best = -1;
        float bestTop = float.NegativeInfinity;
        for (int i = 0; i < _platforms.Count; i++)
        {
            Aabb p = _platforms[i];
            if (position.X >= p.Left && position.X <= p.Right
                && p.Top <= position.Y + StandTolerance && p.Top > bestTop)
            {
                best = i;
                bestTop = p.Top;
            }
        }
        return best;
    }

    /// <summary>First hop of a shortest route from → to; false when disconnected.</summary>
    public bool TryRoute(int from, int to, out int next)
    {
        next = from >= 0 && to >= 0 ? _nextHop[from, to] : -1;
        return next >= 0;
    }

    /// <summary>Coarse ballistic hop check: can the character's chained jumps gain the
    /// height difference, and does the flight time at MaxAirSpeed cover the horizontal
    /// gap between the platform spans (with safety margin)?</summary>
    private static bool HopFeasible(Aabb from, Aabb to, SimPlayer character, float g)
    {
        float v1 = character.GroundJumpForce;
        float v2 = character.AirJumpForce;
        float maxRise = (v1 * v1 + v2 * v2) / (2f * g);
        float dy = to.Top - from.Top;
        if (dy > maxRise)
        {
            return false;
        }

        float gap = MathF.Max(0f, MathF.Max(to.Left - from.Right, from.Left - to.Right));
        // Flight time: full ascent of both jumps, then descent from the peak to the
        // target height. Coarse by design — the graph needs "plausible", not exact.
        float ascent = (v1 + v2) / g;
        float descent = MathF.Sqrt(2f * MathF.Max(0.1f, maxRise - dy) / g);
        return gap <= character.MaxAirSpeed * (ascent + descent) * RangeSafety;
    }
}
