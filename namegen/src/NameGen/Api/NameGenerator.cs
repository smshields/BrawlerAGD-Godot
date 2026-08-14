using System;
using System.Collections.Generic;
using System.Linq;
using NameGen.Core;
using NameGen.Data;
using NameGen.Engine;
using NameGen.Features;
using NameGen.Traits;

namespace NameGen
{
    /// <summary>
    /// Entry point. Construct once (data load + validation happens here), call per genome.
    /// Thread-safety: Generate methods are stateless; a generator instance can be shared
    /// as long as each call gets its own options/seed.
    /// </summary>
    public sealed class NameGenerator
    {
        private readonly NameGenData _data;
        private readonly FeatureExtractor _extractor;
        private readonly TemplateEngine _engine;
        private readonly Blocklist _blocklist;

        public NameGenerator(NameGenData data)
        {
            _data = data;
            _extractor = new FeatureExtractor(data.Ranges);
            _engine = new TemplateEngine(data);
            _blocklist = new Blocklist(data.Blocklist);
        }

        /// <summary>Generator over the database embedded in this DLL.</summary>
        public static NameGenerator CreateDefault() => new(NameGenData.LoadEmbedded());

        /// <summary>Generator with JSON overrides from a directory (iterate on data without recompiling).</summary>
        public static NameGenerator CreateFromDirectory(string dataDirectory)
            => new(NameGenData.LoadFromDirectory(dataDirectory));

        public NameResult GenerateCharacterName(CharacterGenome genome, NameOptions? options = null)
        {
            var opts = options ?? NameOptions.Default;
            var features = _extractor.ExtractCharacter(genome);
            var salient = TraitScorer.SelectSalient(features, _data.Traits.Character,
                _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
            return Generate(features, salient, opts, "character");
        }

        public NameResult GenerateStageName(StageGenome genome, NameOptions? options = null)
        {
            var opts = options ?? NameOptions.Default;
            var features = _extractor.ExtractStage(genome);
            var salient = TraitScorer.SelectSalient(features, _data.Traits.Stage,
                _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
            return Generate(features, salient, opts, "stage");
        }

        private NameResult Generate(FeatureVector features, List<SalientTrait> salient, NameOptions opts, string kind)
        {
            var rng = new Pcg32(opts.Seed ?? (ulong)DateTime.UtcNow.Ticks ^ (ulong)Guid.NewGuid().GetHashCode());

            var register = PickRegister(opts, rng);
            var shape = PickShape(register, opts, rng);
            double bleedP = opts.BleedProbability ?? register.BleedProbability;
            double mundaneP = opts.MundaneProbability ?? register.MundaneProbability;

            var ofKind = register.Templates.Where(t => t.Kind == kind).ToList();
            var templates = ofKind
                .Where(t => t.Shape == (shape == NameShape.Single ? "single" : "full"))
                .ToList();
            if (templates.Count == 0) templates = ofKind;

            // Best-of-N with sampling among the survivors, not argmax: the scorer's job is
            // to filter garbage, not to converge every name onto one house style.
            var scored = new List<(TemplateEngine.Candidate cand, double score)>();
            int attempts = Math.Max(opts.CandidateCount, 4) * 3;
            for (int i = 0; i < attempts && scored.Count < opts.CandidateCount; i++)
            {
                var template = templates[WeightedSampler.Sample(templates.Select(t => t.Weight).ToList(), rng)];
                var cand = _engine.Fill(register, template, salient, rng, bleedP, mundaneP);
                if (_blocklist.IsBlocked(cand.Display)) continue;

                double score = PhoneticScorer.LegalityPenalty(cand.Display, register.Joiner)
                    + PhoneticScorer.TraitAffinity(cand.Display, salient);
                scored.Add((cand, score));
            }

            if (scored.Count == 0)
                throw new InvalidOperationException(
                    $"All candidates were blocklisted for register '{register.Name}'; check blocklist/database interaction.");

            // Keep the top half by score, sample uniformly within it.
            var survivors = scored.OrderByDescending(s => s.score)
                .Take(Math.Max(1, scored.Count / 2))
                .ToList();
            var picked = survivors[rng.NextInt(survivors.Count)].cand;

            return new NameResult
            {
                Display = picked.Display,
                Register = register.Name,
                Shape = shape,
                Parts = picked.Parts,
                SalientTraits = salient.Select(s => new TraitScore(s.Name, Math.Round(s.Score, 4))).ToList(),
                Features = features.Values.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 4)),
            };
        }

        private RegisterDef PickRegister(NameOptions opts, Pcg32 rng)
        {
            if (opts.Register != null)
            {
                var forced = _data.Registers.FirstOrDefault(r =>
                    string.Equals(r.Name, opts.Register, StringComparison.OrdinalIgnoreCase));
                if (forced == null)
                    throw new ArgumentException($"Unknown register '{opts.Register}'. Available: {string.Join(", ", _data.Registers.Select(r => r.Name))}");
                return forced;
            }
            int idx = WeightedSampler.Sample(_data.Registers.Select(r => r.Weight).ToList(), rng);
            return _data.Registers[idx];
        }

        private static NameShape PickShape(RegisterDef register, NameOptions opts, Pcg32 rng)
        {
            if (opts.Shape != null) return opts.Shape.Value;
            register.ShapeWeights.TryGetValue("single", out double single);
            register.ShapeWeights.TryGetValue("full", out double full);
            if (single <= 0 && full <= 0) { single = 1; full = 1; }
            return rng.NextDouble() * (single + full) < single ? NameShape.Single : NameShape.Full;
        }
    }
}
