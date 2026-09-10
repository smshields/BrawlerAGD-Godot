namespace BrawlerSim.Evolution;

/// <summary>The per-individual round-aggregation rule, shared by EvolutionEngine and
/// MapElitesEngine (extracted 2026-09-10 so the two algorithms can never grade the
/// same rounds differently). In-place insertion sort: round counts are tiny and this
/// allocates nothing.</summary>
internal static class FitnessAggregation
{
    internal static float Aggregate(Span<float> rounds, FitnessAggregate aggregate)
    {
        for (int i = 1; i < rounds.Length; i++)
        {
            float value = rounds[i];
            int j = i - 1;
            while (j >= 0 && rounds[j] > value)
            {
                rounds[j + 1] = rounds[j];
                j--;
            }
            rounds[j + 1] = value;
        }
        if (aggregate == FitnessAggregate.Median)
        {
            return rounds[rounds.Length / 2]; // Unity parity: upper median
        }
        float total = 0f;
        foreach (float value in rounds)
        {
            total += value;
        }
        return total / rounds.Length;
    }
}
