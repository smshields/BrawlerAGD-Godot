using BrawlerSim.Determinism;
using BrawlerSim.Params;

namespace BrawlerSim.Genome;

/// <summary>StageRules — the per-character platform-fit solver (2026-07-23,
/// CHANGE_LOG #30 and its 2026-07-27 iterative amendment): traversability +
/// asymmetric-body-gap repair, RNG-free and structurally terminating. Split
/// from StageRules.cs 2026-09-01, pure text move.</summary>
public static partial class StageRules
{
    // ── Per-character platform fit (2026-07-22, FEATURES.md §Map Size follow-up) ──────
    // The abstract jump grid (jumpHeight/jumpLength) is character-BLIND, so a stage can
    // let one character move between platforms while the other cannot, and a gap can be
    // fall-through-passable for a small body but a wall for a large one. The designer's
    // fix: after the stage AND both characters are known, MOVE platforms so BOTH
    // characters can traverse every intended edge and no gap is asymmetrically passable
    // — deterministically, never by re-rolling (which would desync the RNG stream).

    /// <summary>A character's hop parameters, mirroring PlatformGraph.HopFeasible (the
    /// agent's ACTUAL reachability model) EXACTLY — jump forces, air speed, and scaled
    /// gravity, no dash. The dash is deliberately excluded because the agent's
    /// pathfinder ignores it: fitting to a dash-inclusive reach would still leave the
    /// agent unable to route the gap, which is the very asymmetry being fixed.</summary>
    public readonly struct CharHop
    {
        public readonly float V1, V2, Air, G;
        public CharHop(CharacterGenome c, float gravity)
        {
            ParamSet p = c.Params;
            V1 = p.Get(CharacterParams.GroundJumpForce);
            V2 = p.Get(CharacterParams.AirJumpForce);
            Air = p.Get(CharacterParams.MaxAirSpeed);
            G = MathF.Max(0.01f, gravity * p.Get(CharacterParams.GravityScalar));
        }

        public float MaxRise => (V1 * V1 + V2 * V2) / (2f * G);

        /// <summary>Horizontal reach when the target sits <paramref name="dy"/> above
        /// (PlatformGraph.HopFeasible's ascent+descent flight time × air speed × 0.9).</summary>
        public float HorizReach(float dy)
        {
            float ascent = (V1 + V2) / G;
            float descent = MathF.Sqrt(2f * MathF.Max(0.1f, MaxRise - dy) / G);
            return Air * (ascent + descent) * 0.9f;
        }

        public bool CanHop(in PlatformGene from, in PlatformGene to)
        {
            float dy = (to.Y + to.YSize) - (from.Y + from.YSize);
            if (dy > MaxRise)
            {
                return false;
            }
            float gap = MathF.Max(0f, MathF.Max(to.X - (from.X + from.XSize), from.X - (to.X + to.XSize)));
            return gap <= HorizReach(dy);
        }
    }

    /// <summary>Body half extents in world units (for the fall-through gap check).</summary>
    private static float BodyWidth(CharacterGenome c, float baseWidth) =>
        baseWidth * c.Params.Get(CharacterParams.WidthScalar);

    /// <summary>A body passes a vertical corridor when the gap clears its width by
    /// this slack; below it the corridor is a wall for that body.</summary>
    private const float GapPassSlack = 0.1f;

    /// <summary>
    /// Adjusts a stage's platforms so BOTH characters can reach every platform from the
    /// first one, and no horizontal gap is fall-through-passable for one body but a wall
    /// for the other. Deterministic, integer moves, overlap-guarded. Rewritten
    /// 2026-07-27 (designer: asymmetric gaps still appeared in play): the body-fit is
    /// now an ITERATIVE solver — it re-scans after every repositioning (a fix can open
    /// a new violation elsewhere), rotates through five strategies per violating pair
    /// (widen right / widen left / dock right / dock left / vertical separation), and
    /// is loop-proof by construction: per-pair attempt counters pick a DIFFERENT
    /// strategy each revisit, a pair that exhausts its attempts is force-resolved by
    /// docking (a contiguous wall is symmetric for every body), and a pair that cannot
    /// even dock is marked unresolvable and skipped — so every pass strictly reduces
    /// open work and the loop always terminates. Connectivity and body-fit alternate
    /// until a full round changes nothing. Spawns are re-repaired against the adjusted
    /// layout. Returns the stage unchanged (same instance) when the layout already
    /// satisfies both characters — the common case.
    /// </summary>
    public static StageGenome FitToCharacters(
        StageGenome stage, CharacterGenome a, CharacterGenome b, float gravity, float playerBaseWidth) =>
        FitToCharacters(stage, new[] { a, b }, gravity, playerBaseWidth);

    /// <summary>N-character fit (2026-08-12, docs/features/four-player.md): identical
    /// semantics, quantified over ALL of a game's characters — every platform reachable
    /// by everyone, and no corridor passable for the smallest body but not the largest
    /// (if the largest passes, everyone does). For two characters this is bit-identical
    /// to the pairwise fit (pure predicates over the same set).</summary>
    public static StageGenome FitToCharacters(
        StageGenome stage, IReadOnlyList<CharacterGenome> characters, float gravity, float playerBaseWidth)
    {
        var hops = new CharHop[characters.Count];
        float smallW = float.MaxValue;
        float largeW = float.MinValue;
        for (int i = 0; i < characters.Count; i++)
        {
            hops[i] = new CharHop(characters[i], gravity);
            float w = BodyWidth(characters[i], playerBaseWidth);
            smallW = MathF.Min(smallW, w);
            largeW = MathF.Max(largeW, w);
        }

        var plats = stage.Platforms.ToArray();
        bool changed = false;
        var attempts = new Dictionary<(int, int), int>();
        var unresolvable = new HashSet<(int, int)>();
        // Containment (2026-08-13): no fit move may push a platform out of the
        // playable box (kill-box containment + the bottom readability clearance).
        (Vec2 playMin, Vec2 playMax) = PlayableBox(stage.Params);

        // Alternate the two phases until a full round is quiet: body-fit moves can
        // break connectivity and connectivity pulls can open new corridors. The round
        // cap is a backstop — per-pair attempt counters and the unresolvable set make
        // the body-fit's open work strictly shrink, so rounds go quiet on their own.
        for (int round = 0; round < 8; round++)
        {
            bool moved = ConnectPhase(plats, hops, playMin, playMax);
            moved |= BodyFitPhase(plats, hops, smallW, largeW, attempts, unresolvable, playMin, playMax);
            changed |= moved;
            if (!moved)
            {
                break;
            }
        }

        if (!changed)
        {
            return stage;
        }
        var list = plats.ToList();
        return new StageGenome(list, RepairSpawns(list, stage.Params), stage.ThemeId,
            stage.BackgroundId);
    }

    /// <summary>Phase 1 — connectivity for ALL characters: grow a connected set from
    /// platform 0 over edges everyone can hop (PlatformGraph's model). Each stalled
    /// round, pull the nearest still-unreachable platform toward its best connector
    /// until all can hop it — integer moves, overlap-guarded.</summary>
    private static bool ConnectPhase(PlatformGene[] plats, CharHop[] hops, Vec2 playMin, Vec2 playMax)
    {
        bool changed = false;
        var connected = new bool[plats.Length];
        connected[0] = true;
        int connectedCount = 1;
        int guard = plats.Length * plats.Length + 8;
        while (connectedCount < plats.Length && guard-- > 0)
        {
            // Any platform already all-hop-reachable from the connected set joins free.
            bool grew = false;
            for (int u = 0; u < plats.Length; u++)
            {
                if (connected[u])
                {
                    continue;
                }
                for (int c = 0; c < plats.Length && !connected[u]; c++)
                {
                    if (connected[c] && AllCanHop(plats[c], plats[u], hops))
                    {
                        connected[u] = true;
                        connectedCount++;
                        grew = true;
                    }
                }
            }
            if (grew)
            {
                continue;
            }
            // None reachable as-is: pick the unreachable platform nearest the connected
            // set, then try to pull it to EACH connector (nearest first) until one takes.
            int bestU = -1;
            float bestDist = float.MaxValue;
            for (int u = 0; u < plats.Length; u++)
            {
                if (connected[u])
                {
                    continue;
                }
                for (int c = 0; c < plats.Length; c++)
                {
                    if (connected[c])
                    {
                        float dist = CenterDistanceSq(plats[c], plats[u]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestU = u;
                        }
                    }
                }
            }
            if (bestU < 0)
            {
                break;
            }
            foreach (int c in ConnectorsByDistance(plats, connected, bestU))
            {
                if (TryPullWithinHop(ref plats[bestU], plats[c], plats, bestU, hops, playMin, playMax)
                    && AllCanHop(plats[c], plats[bestU], hops))
                {
                    changed = true;
                    break;
                }
            }
            connected[bestU] = true; // bounded progress even if every connector was blocked
            connectedCount++;
        }
        return changed;
    }

    /// <summary>Phase 2 — the iterative asymmetric-gap solver (see FitToCharacters).</summary>
    private static bool BodyFitPhase(
        PlatformGene[] plats, CharHop[] hops, float smallW, float largeW,
        Dictionary<(int, int), int> attempts, HashSet<(int, int)> unresolvable,
        Vec2 playMin, Vec2 playMax)
    {
        const int MaxPasses = 12;
        const int StrategyCount = 5;
        bool movedAny = false;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            List<(int I, int J)> violations = FindAsymmetricGaps(plats, smallW, largeW, unresolvable);
            if (violations.Count == 0)
            {
                return movedAny;
            }
            bool progress = false;
            foreach ((int i, int j) in violations)
            {
                int tried = attempts.GetValueOrDefault((i, j));
                attempts[(i, j)] = tried + 1;
                if (tried >= StrategyCount)
                {
                    // This pair has cycled every strategy across passes: force it.
                    if (ForceResolve(plats, i, j, playMin, playMax))
                    {
                        progress = true;
                        movedAny = true;
                    }
                    else
                    {
                        unresolvable.Add((i, j));
                    }
                    continue;
                }
                // Rotate the starting strategy by the attempt count, so a pair that
                // reappears is attacked DIFFERENTLY each time (the loop-prevention).
                for (int s = 0; s < StrategyCount; s++)
                {
                    if (TryGapStrategy((tried + s) % StrategyCount, plats, i, j, hops, largeW, playMin, playMax))
                    {
                        progress = true;
                        movedAny = true;
                        break;
                    }
                }
            }
            if (!progress)
            {
                // Nothing moved this pass: force the first open violation so the pass
                // loop strictly shrinks its work, else retire it as unresolvable.
                (int i, int j) = violations[0];
                if (ForceResolve(plats, i, j, playMin, playMax))
                {
                    movedAny = true;
                }
                else
                {
                    unresolvable.Add((i, j));
                }
            }
        }
        return movedAny;
    }

    /// <summary>Pairs forming a vertical corridor (side-by-side with vertical overlap,
    /// i left of j) whose gap the SMALLER body passes but the larger cannot.</summary>
    private static List<(int I, int J)> FindAsymmetricGaps(
        PlatformGene[] plats, float smallW, float largeW, HashSet<(int, int)> unresolvable)
    {
        var violations = new List<(int, int)>();
        for (int i = 0; i < plats.Length; i++)
        {
            for (int j = 0; j < plats.Length; j++)
            {
                // Thin platforms never wall a corridor (2026-09-01): bodies pass
                // through their sides regardless of width, so a gap bounded by one
                // is passable for EVERY body — never asymmetric.
                if (i == j || plats[i].Thin || plats[j].Thin
                    || !VerticallyOverlap(plats[i], plats[j]))
                {
                    continue;
                }
                float gap = plats[j].X - (plats[i].X + plats[i].XSize); // j right of i
                if (gap <= 0f || unresolvable.Contains((i, j)))
                {
                    continue;
                }
                bool smallPasses = gap >= smallW + GapPassSlack;
                bool largePasses = gap >= largeW + GapPassSlack;
                if (smallPasses && !largePasses)
                {
                    violations.Add((i, j));
                }
            }
        }
        return violations;
    }

    /// <summary>One repositioning strategy for the corridor between i (left) and j.
    /// Prefers WIDENING (both bodies pass — "gaps navigable by all characters") and
    /// falls back to docking (a contiguous wall, symmetric) and vertical separation
    /// (no corridor at all). A strategy is accepted only when it introduces no overlap
    /// and does not shrink both-character connectivity.</summary>
    private static bool TryGapStrategy(
        int strategy, PlatformGene[] plats, int i, int j, CharHop[] hops, float largeW,
        Vec2 playMin, Vec2 playMax)
    {
        float gap = plats[j].X - (plats[i].X + plats[i].XSize);
        int widen = (int)MathF.Ceiling(largeW + GapPassSlack + 0.4f - gap);
        (int idx, PlatformGene cand) = strategy switch
        {
            // Widen so BOTH bodies clear the corridor.
            0 => (j, plats[j] with { X = plats[j].X + widen }),
            1 => (i, plats[i] with { X = plats[i].X - widen }),
            // Dock contiguous: gap 0 is a wall for every body — symmetric.
            2 => (j, plats[j] with { X = plats[i].X + plats[i].XSize }),
            3 => (i, plats[i] with { X = plats[j].X - plats[i].XSize }),
            // Separate vertically: no vertical overlap ⇒ no corridor. Smaller shift wins.
            _ => (j, VerticalSeparation(plats[i], plats[j])),
        };
        if (OverlapsAnyExcept(cand, plats, idx) || !PlatformInPlayableBox(cand, playMin, playMax))
        {
            return false;
        }
        PlatformGene saved = plats[idx];
        int before = AllConnectedCount(plats, hops);
        plats[idx] = cand;
        if (AllConnectedCount(plats, hops) < before)
        {
            plats[idx] = saved; // never trade a gap fix for lost traversability
            return false;
        }
        return true;
    }

    /// <summary>Last-resort resolution, overlap-guarded only: dock contiguous (either
    /// side), then vertical separation. False only when every option overlaps.</summary>
    private static bool ForceResolve(PlatformGene[] plats, int i, int j, Vec2 playMin, Vec2 playMax)
    {
        Span<(int Idx, PlatformGene Cand)> options = stackalloc (int, PlatformGene)[]
        {
            (j, plats[j] with { X = plats[i].X + plats[i].XSize }),
            (i, plats[i] with { X = plats[j].X - plats[i].XSize }),
            (j, VerticalSeparation(plats[i], plats[j])),
        };
        foreach ((int idx, PlatformGene cand) in options)
        {
            if (!OverlapsAnyExcept(cand, plats, idx) && PlatformInPlayableBox(cand, playMin, playMax))
            {
                plats[idx] = cand;
                return true;
            }
        }
        return false;
    }

    /// <summary>j shifted vertically just clear of i's span (up or down, whichever is
    /// the smaller move; +1 clearance so they no longer share a corridor band).</summary>
    private static PlatformGene VerticalSeparation(in PlatformGene i, in PlatformGene j)
    {
        int upShift = (i.Y + i.YSize) - j.Y + 1;   // move j up above i's top
        int downShift = (j.Y + j.YSize) - i.Y + 1; // move j down below i's bottom
        return upShift <= downShift
            ? j with { Y = j.Y + upShift }
            : j with { Y = j.Y - downShift };
    }

    /// <summary>Platforms reachable from platform 0 over ALL-hop edges.</summary>
    private static int AllConnectedCount(PlatformGene[] plats, CharHop[] hops)
    {
        var visited = new bool[plats.Length];
        visited[0] = true;
        var queue = new Queue<int>();
        queue.Enqueue(0);
        int seen = 1;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            for (int u = 0; u < plats.Length; u++)
            {
                if (!visited[u] && AllCanHop(plats[c], plats[u], hops))
                {
                    visited[u] = true;
                    seen++;
                    queue.Enqueue(u);
                }
            }
        }
        return seen;
    }

    /// <summary>Connected platform indices, nearest to <paramref name="u"/> first.</summary>
    private static IEnumerable<int> ConnectorsByDistance(PlatformGene[] plats, bool[] connected, int u)
    {
        var list = new List<int>();
        for (int c = 0; c < plats.Length; c++)
        {
            if (connected[c])
            {
                list.Add(c);
            }
        }
        list.Sort((x, y) => CenterDistanceSq(plats[x], plats[u]).CompareTo(CenterDistanceSq(plats[y], plats[u])));
        return list;
    }

    private static bool AllCanHop(in PlatformGene from, in PlatformGene to, CharHop[] hops)
    {
        foreach (CharHop h in hops)
        {
            if (!h.CanHop(from, to) || !h.CanHop(to, from))
            {
                return false;
            }
        }
        return true;
    }

    private static float CenterDistanceSq(in PlatformGene a, in PlatformGene b)
    {
        float dx = (a.X + a.XSize * 0.5f) - (b.X + b.XSize * 0.5f);
        float dy = (a.Y + a.YSize * 0.5f) - (b.Y + b.YSize * 0.5f);
        return dx * dx + dy * dy;
    }

    /// <summary>Move u toward connector c — lower it until the rise is within EVERY
    /// character's max, then shrink the horizontal gap until all can hop — in integer
    /// steps, leaving ≥ 1 unit and never overlapping (reverts on overlap).</summary>
    private static bool TryPullWithinHop(
        ref PlatformGene u, in PlatformGene c, PlatformGene[] all, int uIndex, CharHop[] hops,
        Vec2 playMin, Vec2 playMax)
    {
        PlatformGene start = u;
        float minMaxRise = float.MaxValue;
        foreach (CharHop h in hops)
        {
            minMaxRise = MathF.Min(minMaxRise, h.MaxRise);
        }
        for (int step = 0; step < 40 && !AllCanHop(c, u, hops); step++)
        {
            float dyUp = (u.Y + u.YSize) - (c.Y + c.YSize);   // u above c
            float dyDown = (c.Y + c.YSize) - (u.Y + u.YSize); // c above u
            if (dyUp > minMaxRise)
            {
                u = u with { Y = u.Y - 1 }; // lower u so the climb onto it is reachable
            }
            else if (dyDown > minMaxRise)
            {
                u = u with { Y = u.Y + 1 }; // raise u so the climb back onto c is reachable
            }
            else
            {
                // Heights ok — close the horizontal gap by one unit toward c (min 1 gap).
                float gap = MathF.Max(u.X - (c.X + c.XSize), c.X - (u.X + u.XSize));
                if (gap <= 1f)
                {
                    break; // adjacent already; cannot close further without overlap
                }
                u = u.X > c.X ? u with { X = u.X - 1 } : u with { X = u.X + 1 };
            }
            if (OverlapsAnyExcept(u, all, uIndex) || !PlatformInPlayableBox(u, playMin, playMax))
            {
                u = start; // never introduce an overlap or leave the playable box
                return false;
            }
        }
        // Last resort for a very weak character (reach < 1 unit): dock u CONTIGUOUS to c
        // at the same top — a continuous surface anyone can simply WALK across, feasible
        // for any reach. Only if it introduces no overlap.
        if (!AllCanHop(c, u, hops))
        {
            int top = c.Y + c.YSize;
            var docked = u with
            {
                Y = top - u.YSize,
                X = u.X >= c.X ? c.X + c.XSize : c.X - u.XSize,
            };
            if (!OverlapsAnyExcept(docked, all, uIndex) && PlatformInPlayableBox(docked, playMin, playMax))
            {
                u = docked;
            }
        }
        return u.X != start.X || u.Y != start.Y;
    }

    private static bool VerticallyOverlap(in PlatformGene a, in PlatformGene b) =>
        a.Y < b.Y + b.YSize && b.Y < a.Y + a.YSize;

    private static bool OverlapsAnyExcept(in PlatformGene candidate, PlatformGene[] all, int except)
    {
        for (int i = 0; i < all.Length; i++)
        {
            if (i != except && Overlaps(candidate, all[i]))
            {
                return true;
            }
        }
        return false;
    }
}
