using BrawlerSim.Sim;

namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// THE CONNECTOR (2026-09-16, fitness term refactor): the one seam between the data a
/// match produces and the terms that score it. Terms never touch <see cref="MatchResult"/>
/// directly — they read this, so new inputs (genome facts, richer stage descriptors,
/// run context) can be fed to terms later by widening this type instead of changing
/// thirteen term signatures and every call site.
///
/// Deliberately a pass-through with no caching: the aggregate helpers accumulate in
/// player order, every time, because float addition is not associative and the shipped
/// versions' scores are pinned bit-exactly (FitnessCharacterizationTests). A memoized
/// aggregate would be a silent re-ordering.
///
/// Pure: no RNG, no clock, no Godot — the determinism contract is untouched.
/// </summary>
public readonly struct FitnessInput
{
    public FitnessInput(MatchResult result)
    {
        Result = result;
    }

    public MatchResult Result { get; }

    public IReadOnlyList<PlayerStats> Players => Result.Players;

    public int PlayerCount => Result.Players.Count;

    /// <summary>Match length in TICKS — the denominator for share-of-match terms.
    /// Zero for hand-built fixtures; terms that divide by it must guard.</summary>
    public int Ticks => Result.Ticks;

    public float LengthSeconds => Result.LengthSeconds;

    /// <summary>Null for hand-built legacy fixtures; terms that read it treat null as
    /// a legacy-size stage.</summary>
    public StageMetrics? Stage => Result.Stage;

    /// <summary>Σ over all players, accumulated in player order from 0f. Exactly the
    /// two-player pair sum at N = 2 (0f + a + b == a + b for the non-negative
    /// quantities every term feeds it).</summary>
    public float Sum(Func<PlayerStats, float> value)
    {
        float sum = 0f;
        foreach (PlayerStats player in Result.Players)
        {
            sum += value(player);
        }
        return sum;
    }

    /// <summary>Σ over all players in INTEGER arithmetic — for counter terms whose
    /// shipped form sums ints before touching a float (selfDestructs, dropThroughs).</summary>
    public int SumInt(Func<PlayerStats, int> value)
    {
        int sum = 0;
        foreach (PlayerStats player in Result.Players)
        {
            sum += value(player);
        }
        return sum;
    }

    /// <summary>max − min over players — the N-player fairness generalization,
    /// identical to |a − b| at N = 2 (IEEE negation is exact, so both orderings of the
    /// subtraction give the same magnitude).</summary>
    public float Spread(Func<PlayerStats, float> value)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (PlayerStats player in Result.Players)
        {
            float v = value(player);
            min = MathF.Min(min, v);
            max = MathF.Max(max, v);
        }
        return max - min;
    }
}
