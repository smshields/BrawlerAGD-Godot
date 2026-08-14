using System;
using System.Collections.Generic;
using System.Linq;
using NameGen.Core;
using NameGen.Data;
using NameGen.Traits;

namespace NameGen.Engine
{
    /// <summary>
    /// Fills one template: builds the slot pools (with cross-register bleed and mundane
    /// hijacks), applies trait boosts, samples morphemes, and assembles a candidate.
    /// </summary>
    internal sealed class TemplateEngine
    {
        private readonly NameGenData _data;

        public TemplateEngine(NameGenData data) => _data = data;

        internal sealed record Candidate(string Display, List<NamePart> Parts);

        public Candidate Fill(RegisterDef register, TemplateDef template,
            IReadOnlyList<SalientTrait> salient, Pcg32 rng,
            double bleedProbability, double mundaneProbability)
        {
            var parts = new List<NamePart>();
            var joinParts = new List<Joiner.Part>();
            var usedForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // no "Crash Crashax"
            bool traitSlotGuaranteed = false;
            bool mundaneUsed = false; // one hijack per name keeps the joke a punchline, not a pile-up

            foreach (var slot in template.Slots)
            {
                if (slot.Literal != null)
                {
                    string lit = ExpandLiteral(slot.Literal, rng);
                    parts.Add(new NamePart { Form = lit, Gloss = "literal", SourceRegister = register.Name });
                    joinParts.Add(new Joiner.Part(lit, slot.Join));
                    continue;
                }

                // Mundane may hijack any word-starting slot (a "suffix" is mid-word; fusing
                // a whole mundane word into the middle of a coinage reads as noise, but a
                // mundane base with a register suffix fused on reads as "Picklegrim").
                bool mundaneEligible = slot.AllowMundane ?? !string.Equals(slot.Position, "suffix", StringComparison.OrdinalIgnoreCase);
                bool useMundane = mundaneEligible && !mundaneUsed
                    && _data.Mundane.Morphemes.Count > 0 && rng.NextBool(mundaneProbability);
                bool useBleed = !useMundane && rng.NextBool(bleedProbability) && _data.Registers.Count > 1;

                RegisterDef sourceRegister = register;
                IReadOnlyList<MorphemeDef> pool;

                if (useMundane)
                {
                    // Prefix slots draw whole mundane words as the fusion base.
                    string mundanePosition = string.Equals(slot.Position, "prefix", StringComparison.OrdinalIgnoreCase)
                        ? "standalone" : slot.Position;
                    pool = FilterByPosition(_data.Mundane.Morphemes, mundanePosition);
                    if (pool.Count == 0) { useMundane = false; pool = FilterByPosition(register.Morphemes, slot.Position); }
                    else mundaneUsed = true;
                }
                else if (useBleed)
                {
                    var others = _data.Registers.Where(r => r.Name != register.Name).ToList();
                    sourceRegister = others[rng.NextInt(others.Count)];
                    pool = FilterByPosition(sourceRegister.Morphemes, slot.Position);
                    if (pool.Count == 0) { useBleed = false; sourceRegister = register; pool = FilterByPosition(register.Morphemes, slot.Position); }
                }
                else
                {
                    pool = FilterByPosition(register.Morphemes, slot.Position);
                }

                if (pool.Count == 0)
                    throw new InvalidOperationException(
                        $"No morphemes for position '{slot.Position}' in register '{sourceRegister.Name}'. Data validation should have caught this.");

                // Guaranteed trait slot: the first home-register content slot samples only
                // trait-matching morphemes when any exist, so every name points somewhere.
                IReadOnlyList<MorphemeDef> effectivePool = pool;
                if (!traitSlotGuaranteed && !useMundane && !useBleed && salient.Count > 0)
                {
                    var matching = pool.Where(m => MatchScore(m, salient) > 0).ToList();
                    if (matching.Count > 0)
                    {
                        effectivePool = matching;
                        traitSlotGuaranteed = true;
                    }
                }

                var weights = new double[effectivePool.Count];
                bool anyUnused = false;
                for (int i = 0; i < effectivePool.Count; i++)
                {
                    var m = effectivePool[i];
                    if (usedForms.Contains(m.Form)) { weights[i] = 0; continue; }
                    anyUnused = true;
                    weights[i] = m.Weight * (1 + _data.Traits.BoostFactor * MatchScore(m, salient));
                }
                if (!anyUnused) // tiny pool, everything already used: allow repeats over crashing
                    for (int i = 0; i < effectivePool.Count; i++)
                        weights[i] = effectivePool[i].Weight;

                var chosen = effectivePool[WeightedSampler.Sample(weights, rng)];
                usedForms.Add(chosen.Form);
                parts.Add(new NamePart
                {
                    Form = chosen.Form,
                    Gloss = chosen.Gloss,
                    Tags = chosen.Tags,
                    SourceRegister = useMundane ? "mundane" : sourceRegister.Name,
                    IsBleed = useBleed,
                    IsMundane = useMundane,
                });
                joinParts.Add(new Joiner.Part(chosen.Form, slot.Join));
            }

            string display = Joiner.Assemble(joinParts, register.Joiner);
            return new Candidate(display, parts);
        }

        private static double MatchScore(MorphemeDef m, IReadOnlyList<SalientTrait> salient)
        {
            double score = 0;
            foreach (var trait in salient)
                if (m.Tags.Contains(trait.Name, StringComparer.OrdinalIgnoreCase))
                    score += trait.Score;
            return score;
        }

        private static IReadOnlyList<MorphemeDef> FilterByPosition(IReadOnlyList<MorphemeDef> morphemes, string position)
        {
            var result = new List<MorphemeDef>();
            foreach (var m in morphemes)
                if (m.Positions.Contains(position, StringComparer.OrdinalIgnoreCase))
                    result.Add(m);
            return result;
        }

        /// <summary>Literal expansion: each '#' becomes a digit 0-9, each '@' an uppercase letter.</summary>
        private static string ExpandLiteral(string literal, Pcg32 rng)
        {
            var sb = new System.Text.StringBuilder(literal.Length);
            foreach (char c in literal)
            {
                if (c == '#') sb.Append((char)('0' + rng.NextInt(10)));
                else if (c == '@') sb.Append((char)('A' + rng.NextInt(26)));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
