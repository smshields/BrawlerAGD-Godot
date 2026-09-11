using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Evolution;

/// <summary>
/// The four MAP-Elites archive descriptors (2026-09-10, docs/features/map-elites.md).
/// All pure functions of the genome — no simulation, no RNG, no noise — so they are
/// free to compute at insertion time and identical wherever they are evaluated.
/// Every axis is n-player from day one (mean over C(n,2) unordered character pairs
/// where pairing applies); n = 2 reduces to the single-pair formulas.
///
/// Axis 1 — move variety: per action button, how differently the paired characters'
///          mapped moves behave (type mismatch = 1, else normalized param distance).
/// Axis 2 — character asymmetry: five physical params (speeds/mass/size), normalized
///          by their generation-range widths.
/// Axis 3 — stage platform ratio: exact platform-cell union over the playable box
///          (thin platforms count at full cell area — designer decision 2026-09-10).
/// Axis 4 — timing commitment: per-move state-machine commitment normalized within
///          the move's own type, plain mean over each kit (designer: plain mean,
///          not button-weighted — frozen 2026-09-10).
/// </summary>
public static class Descriptors
{
    public const int Count = 4;

    public static readonly string[] AxisNames =
    {
        "MOVE VARIETY", "CHAR ASYMMETRY", "PLATFORM RATIO", "TIMING",
    };

    /// <summary>All four descriptors, in axis order. Values are in [0, 1].</summary>
    public static float[] Compute(GameGenome genome) => new[]
    {
        MoveVariety(genome),
        CharacterAsymmetry(genome),
        StagePlatformRatio(genome),
        TimingCommitment(genome),
    };

    /// <summary>
    /// Axis 1: mean over all unordered character pairs and all action buttons of the
    /// distance between the moves each character maps to that button — 1.0 on a type
    /// mismatch, else the mean normalized param distance within the shared type's
    /// schema (GenomeDistance.Accumulate, repointed to button pairing). Button pairing
    /// is the player-facing definition and the only pairing uniform across generation
    /// modes: composed mode's identity mapping degrades it to slot pairing, while
    /// pinned mode's per-character mapping keeps it non-degenerate.
    /// </summary>
    public static float MoveVariety(GameGenome genome)
    {
        float pairSum = 0f;
        int pairs = 0;
        for (int i = 0; i < genome.Characters.Count; i++)
        {
            for (int j = i + 1; j < genome.Characters.Count; j++)
            {
                pairSum += ButtonKitDistance(genome.Characters[i], genome.Characters[j]);
                pairs++;
            }
        }
        return pairs == 0 ? 0f : pairSum / pairs;
    }

    private static float ButtonKitDistance(CharacterGenome a, CharacterGenome b)
    {
        float buttonSum = 0f;
        int buttons = a.ButtonMoves.Count; // always InputFrame.ActionCount
        for (int button = 0; button < buttons; button++)
        {
            MoveGenome moveA = a.Moves[a.ButtonMoves[button]];
            MoveGenome moveB = b.Moves[b.ButtonMoves[button]];
            if (moveA.Type != moveB.Type)
            {
                buttonSum += 1f;
                continue;
            }
            float sum = 0f;
            int dims = 0;
            GenomeDistance.Accumulate(moveA.Params.Schema, moveA.Params, moveB.Params, ref sum, ref dims);
            buttonSum += dims == 0 ? 0f : sum / dims;
        }
        return buttonSum / buttons;
    }

    /// <summary>The five physical-differentiation params of axis 2.</summary>
    private static readonly string[] AsymmetryParams =
    {
        CharacterParams.MaxGroundSpeed,
        CharacterParams.MaxAirSpeed,
        CharacterParams.Mass,
        CharacterParams.WidthScalar,
        CharacterParams.HeightScalar,
    };

    /// <summary>
    /// Axis 2: mean over all unordered character pairs of the mean normalized
    /// |difference| over the five physical params. Normalization uses the genome's
    /// own bound schema, so range overrides are honored without a config parameter.
    /// </summary>
    public static float CharacterAsymmetry(GameGenome genome)
    {
        float pairSum = 0f;
        int pairs = 0;
        for (int i = 0; i < genome.Characters.Count; i++)
        {
            for (int j = i + 1; j < genome.Characters.Count; j++)
            {
                ParamSet pa = genome.Characters[i].Params;
                ParamSet pb = genome.Characters[j].Params;
                float sum = 0f;
                int dims = 0;
                foreach (string key in AsymmetryParams)
                {
                    ParamSpec spec = pa.Schema[pa.Schema.IndexOf(key)];
                    float width = spec.Max - spec.Min;
                    if (width <= 0f)
                    {
                        continue; // clamped param: no expressible asymmetry
                    }
                    sum += Math.Abs(pa.Get(key) - pb.Get(key)) / width;
                    dims++;
                }
                pairSum += dims == 0 ? 0f : sum / dims;
                pairs++;
            }
        }
        return pairs == 0 ? 0f : pairSum / pairs;
    }

    /// <summary>
    /// Axis 3: exact platform-cell union area over the playable-box area. Platforms
    /// are integer grid rects, so the union is exact by stamping cells into a set —
    /// overlapping platforms (crossover can produce them before/despite repair) never
    /// double-count. Computed on the stored (post-repair) platform list — the
    /// phenotype. Thin platforms count their full gene cell (designer, 2026-09-10).
    /// Legacy stages predating the containment rule can hold cells outside the box;
    /// the ratio is clamped to [0, 1] so they land in the top bin instead of escaping
    /// the archive.
    /// </summary>
    public static float StagePlatformRatio(GameGenome genome)
    {
        StageGenome stage = genome.Stage;
        var cells = new HashSet<long>();
        foreach (PlatformGene platform in stage.Platforms)
        {
            for (int x = platform.X; x < platform.X + platform.XSize; x++)
            {
                for (int y = platform.Y; y < platform.Y + platform.YSize; y++)
                {
                    cells.Add(((long)x << 32) ^ (uint)y);
                }
            }
        }
        (Determinism.Vec2 min, Determinism.Vec2 max) = StageRules.PlayableBox(stage.Params);
        float area = (max.X - min.X) * (max.Y - min.Y);
        return area <= 0f ? 0f : Math.Clamp(cells.Count / area, 0f, 1f);
    }

    /// <summary>
    /// Axis 4: mean over all characters of the mean per-move commitment, each move's
    /// total state-machine commitment normalized within its OWN type's generation
    /// range (per-type normalization keeps shield/dash-heavy kits from reading as
    /// snappy by construction). 0 = every move at its snappiest, 1 = at its slowest.
    /// Min/max totals come from the move's bound schema, so range overrides are
    /// honored automatically. Plain mean over the kit (frozen 2026-09-10).
    /// </summary>
    public static float TimingCommitment(GameGenome genome)
    {
        float characterSum = 0f;
        foreach (CharacterGenome character in genome.Characters)
        {
            float moveSum = 0f;
            foreach (MoveGenome move in character.Moves)
            {
                moveSum += NormalizedCommitment(move);
            }
            characterSum += character.Moves.Count == 0 ? 0f : moveSum / character.Moves.Count;
        }
        return characterSum / genome.Characters.Count;
    }

    /// <summary>The commitment params per move type: the full busy window of the
    /// move's state machine (attack/projectile: warm-up + execution + cool-down;
    /// shield: wind-up + cool-down — hold time is player-chosen, not committed;
    /// dash: wind-up + travel duration).</summary>
    private static readonly string[] AttackCommitment =
    {
        MoveParams.WarmUpDuration, MoveParams.ExecutionDuration, MoveParams.CoolDownDuration,
    };

    private static readonly string[] ShieldCommitment =
    {
        ShieldParams.WindUpDuration, ShieldParams.CoolDownDuration,
    };

    private static readonly string[] DashCommitment =
    {
        DashParams.WindUpDuration, DashParams.Duration,
    };

    private static float NormalizedCommitment(MoveGenome move)
    {
        // Attack and projectile share the three keys by name.
        string[] keys = move.Type switch
        {
            MoveType.Shield => ShieldCommitment,
            MoveType.Dash => DashCommitment,
            _ => AttackCommitment,
        };
        float total = 0f, min = 0f, max = 0f;
        foreach (string key in keys)
        {
            ParamSpec spec = move.Params.Schema[move.Params.Schema.IndexOf(key)];
            total += move.Params.Get(key);
            min += spec.Min;
            max += spec.Max;
        }
        return max <= min ? 0f : Math.Clamp((total - min) / (max - min), 0f, 1f);
    }
}
