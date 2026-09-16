namespace BrawlerSim.Hyperspace;

/// <summary>
/// Maps raw fitness onto the [0,1] the galaxy view's size, colour and brightness
/// formulas expect (2026-09-16, docs/features/galaxy-view.md §Fitness scale).
///
/// The PoC's archive was already normalized; ours is not — a real score runs from
/// negative to ~120 and its range moves as a run progresses and as fitness versions
/// change (never benchmark across versions). An ABSOLUTE scale would therefore render
/// a young run as a field of uniformly tiny dim stars and a late one as uniformly
/// bright. So the scale is PERCENTILE RANK within the snapshot: the archive's best
/// game is always the brightest thing in the sky and the worst is always the dimmest,
/// which is what the view is for — comparing within one archive, never across two.
///
/// Stars and members share ONE pool, so a genuinely good runner-up renders as a big
/// planet rather than being flattened against its own star.
/// </summary>
public sealed class FitnessScale
{
    private readonly float[] _sorted;

    public FitnessScale(IEnumerable<float> pool)
    {
        _sorted = pool.ToArray();
        Array.Sort(_sorted);
    }

    public int Count => _sorted.Length;

    /// <summary>Midrank percentile of a value in [0,1]: the fraction of the pool
    /// below it plus half its ties, so equal scores map to one value and a lone
    /// entry sits mid-scale rather than at an arbitrary end.</summary>
    public float Normalize(float fitness)
    {
        if (_sorted.Length == 0)
        {
            return 0.5f;
        }
        int lower = LowerBound(fitness);
        int upper = UpperBound(fitness);
        float midrank = (lower + upper) / 2f;
        return Math.Clamp(midrank / _sorted.Length, 0f, 1f);
    }

    private int LowerBound(float value)
    {
        int low = 0, high = _sorted.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (_sorted[mid] < value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return low;
    }

    private int UpperBound(float value)
    {
        int low = 0, high = _sorted.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (_sorted[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return low;
    }
}
