using System;
using System.Collections.Generic;
using System.Linq;
using NameGen.Data;
using NameGen.Features;

namespace NameGen.Traits
{
    public sealed record SalientTrait(string Name, double Score, TraitDef Def);

    /// <summary>
    /// Scores traits from features and picks the salient few. The name should point
    /// at what is distinctive about this genome, not enumerate the whole sheet.
    /// </summary>
    public static class TraitScorer
    {
        /// <summary>
        /// Trait score = sum over features of weight * (2*value - 1), clamped at 0.
        /// A scalar at its neutral 0.5 contributes nothing; extremes contribute ±weight.
        /// Flags contribute +weight when set, -weight when explicitly unset (0),
        /// nothing when the feature was never extracted (reads as 0.5).
        /// </summary>
        public static List<SalientTrait> ScoreAll(FeatureVector features, IReadOnlyList<TraitDef> traits)
        {
            var results = new List<SalientTrait>(traits.Count);
            foreach (var trait in traits)
            {
                double score = 0;
                foreach (var kv in trait.Features)
                    score += kv.Value * (2 * features.Get(kv.Key) - 1);
                results.Add(new SalientTrait(trait.Name, Math.Max(0, score), trait));
            }
            return results;
        }

        public static List<SalientTrait> SelectSalient(FeatureVector features, IReadOnlyList<TraitDef> traits,
            int topK, double threshold)
        {
            return ScoreAll(features, traits)
                .Where(t => t.Score >= threshold)
                .OrderByDescending(t => t.Score)
                .Take(topK)
                .ToList();
        }
    }
}
