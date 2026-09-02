using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Backgrounds;

/// <summary>The seeded-softmax sampling primitives, as used by every semantic
/// selector since M4. The sprite/theme selectors keep their private copies (their
/// draw streams are pinned by fixtures — consolidating them is the Tier-2 backlog
/// item); the backgrounds track starts on the shared implementation.</summary>
internal static class SelectionMath
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
