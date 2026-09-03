using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Backgrounds;

/// <summary>The seeded-softmax sampling primitives, as used by every semantic
/// selector since M4. The sprite/theme selectors keep their private copies (their
/// draw streams are pinned by fixtures — consolidating them is the Tier-2 backlog
/// item); the backgrounds track starts on the shared implementation. Public so the
/// water-fill rules are unit-testable in isolation.</summary>
public static class SelectionMath
{
    public static double[] Softmax(double[] scores, float temperature)
    {
        double max = double.NegativeInfinity;
        foreach (double s in scores)
        {
            max = Math.Max(max, s);
        }
        double t = Math.Max(1e-3, temperature);
        var probs = new double[scores.Length];
        double sum = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            probs[i] = Math.Exp((scores[i] - max) / t);
            sum += probs[i];
        }
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] /= sum;
        }
        return probs;
    }

    /// <summary>The no-monopoly water-fill, exactly the SpriteSelector rule.</summary>
    public static void CapShare(double[] probs, float cap)
    {
        if (cap <= 0 || cap * probs.Length <= 1.0)
        {
            return;
        }
        var capped = new bool[probs.Length];
        for (int pass = 0; pass < probs.Length; pass++)
        {
            int cappedCount = 0;
            double freeMass = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    cappedCount++;
                }
                else
                {
                    freeMass += probs[i];
                }
            }
            double target = 1.0 - cappedCount * (double)cap;
            if (freeMass <= 0 || target <= 0)
            {
                return;
            }
            double scale = target / freeMass;
            bool cappedAnother = false;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    probs[i] = cap;
                    continue;
                }
                probs[i] *= scale;
                if (probs[i] > cap)
                {
                    probs[i] = cap;
                    capped[i] = true;
                    cappedAnother = true;
                }
            }
            if (!cappedAnother)
            {
                return;
            }
        }
    }

    /// <summary>The no-monopoly water-fill at GROUP level (source packs, designer
    /// 2026-09-03): no group may hold more than `cap` of the pool's probability
    /// mass. Groups over the cap are scaled down and frozen; the freed mass lifts
    /// the rest, iterating because a lift can push another group over. A no-op when
    /// the cap is infeasible (cap x groups &lt;= 1) — tiny pools stay untouched.</summary>
    public static void CapGroupShare(double[] probs, int[] groups, float cap)
    {
        int groupCount = 0;
        foreach (int g in groups)
        {
            groupCount = Math.Max(groupCount, g + 1);
        }
        if (cap <= 0 || groupCount == 0 || cap * groupCount <= 1.0)
        {
            return;
        }
        double total = 0;
        foreach (double p in probs)
        {
            total += p;
        }
        if (total <= 0)
        {
            return;
        }
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] /= total;
        }
        var frozen = new bool[groupCount];
        var mass = new double[groupCount];
        for (int pass = 0; pass < groupCount; pass++)
        {
            Array.Clear(mass);
            for (int i = 0; i < probs.Length; i++)
            {
                mass[groups[i]] += probs[i];
            }
            bool frozeAnother = false;
            int frozenCount = 0;
            double freeMass = 0;
            for (int g = 0; g < groupCount; g++)
            {
                if (frozen[g])
                {
                    frozenCount++;
                }
                else if (mass[g] > cap + 1e-9)
                {
                    frozen[g] = true;
                    frozenCount++;
                    frozeAnother = true;
                }
            }
            if (!frozeAnother)
            {
                return;
            }
            for (int i = 0; i < probs.Length; i++)
            {
                int g = groups[i];
                if (frozen[g] && mass[g] > cap)
                {
                    probs[i] *= cap / mass[g];
                }
            }
            for (int g = 0; g < groupCount; g++)
            {
                if (!frozen[g])
                {
                    freeMass += mass[g];
                }
            }
            double target = 1.0 - frozenCount * (double)cap;
            if (freeMass <= 0 || target <= 0)
            {
                return;
            }
            double lift = target / freeMass;
            for (int i = 0; i < probs.Length; i++)
            {
                if (!frozen[groups[i]])
                {
                    probs[i] *= lift;
                }
            }
        }
    }

    public static int SampleIndex(double[] probs, NgPcg rng)
    {
        double total = 0;
        foreach (double p in probs)
        {
            total += p;
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        int last = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (probs[i] <= 0)
            {
                continue;
            }
            acc += probs[i];
            last = i;
            if (roll < acc)
            {
                return i;
            }
        }
        return last;
    }
}
