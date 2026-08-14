using System;
using System.Collections.Generic;

namespace NameGen.Core
{
    internal static class WeightedSampler
    {
        /// <summary>Sample an index proportional to weights. Non-positive weights are treated as 0.</summary>
        public static int Sample(IReadOnlyList<double> weights, Pcg32 rng)
        {
            double total = 0;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0) total += weights[i];

            if (total <= 0)
            {
                // Degenerate pool: fall back to uniform.
                return rng.NextInt(weights.Count);
            }

            double roll = rng.NextDouble() * total;
            double acc = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) continue;
                acc += weights[i];
                if (roll < acc) return i;
            }
            return weights.Count - 1;
        }
    }
}
