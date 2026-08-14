using System;
using System.Collections.Generic;
using NameGen.Data;
using NameGen.Traits;

namespace NameGen.Core
{
    /// <summary>
    /// Scores candidate names: phonotactic legality plus sound-symbolic affinity to the
    /// salient traits (heavy characters toward back vowels and voiced plosives, swift
    /// ones toward front vowels and voiceless stops, and so on). This is the mechanism
    /// that lets even the Normal register point at traits without literal semantics.
    ///
    /// Phoneme class names usable in traits.json "phonemes":
    ///   frontVowel, backVowel, openVowel, voicedPlosive, voicelessPlosive,
    ///   sibilant, liquid, nasal, fricative, shortName, longName
    /// </summary>
    public static class PhoneticScorer
    {
        private static readonly Dictionary<char, string> Classes = new()
        {
            ['i'] = "frontVowel", ['e'] = "frontVowel", ['y'] = "frontVowel",
            ['o'] = "backVowel", ['u'] = "backVowel",
            ['a'] = "openVowel",
            ['b'] = "voicedPlosive", ['d'] = "voicedPlosive", ['g'] = "voicedPlosive",
            ['p'] = "voicelessPlosive", ['t'] = "voicelessPlosive", ['k'] = "voicelessPlosive", ['c'] = "voicelessPlosive", ['q'] = "voicelessPlosive",
            ['s'] = "sibilant", ['z'] = "sibilant", ['x'] = "sibilant", ['j'] = "sibilant",
            ['l'] = "liquid", ['r'] = "liquid", ['w'] = "liquid",
            ['m'] = "nasal", ['n'] = "nasal",
            ['f'] = "fricative", ['v'] = "fricative", ['h'] = "fricative",
        };

        /// <summary>Affinity of a name to the salient traits' phoneme preferences, roughly [-1, 1] per trait unit.</summary>
        public static double TraitAffinity(string name, IReadOnlyList<SalientTrait> salient)
        {
            if (salient.Count == 0) return 0;

            // Class frequency profile of the name.
            var freq = new Dictionary<string, double>();
            int letters = 0;
            foreach (char raw in name)
            {
                char c = char.ToLowerInvariant(raw);
                if (!Classes.TryGetValue(c, out var cls)) continue;
                letters++;
                freq.TryGetValue(cls, out var f);
                freq[cls] = f + 1;
            }
            if (letters == 0) return 0;

            int coreLength = 0;
            foreach (char c in name) if (char.IsLetter(c)) coreLength++;
            double lengthNorm = Math.Max(0.0, Math.Min(1.0, (coreLength - 3) / 11.0)); // 3..14 → 0..1

            double total = 0, weightSum = 0;
            foreach (var trait in salient)
            {
                double affinity = 0;
                foreach (var kv in trait.Def.Phonemes)
                {
                    switch (kv.Key)
                    {
                        case "shortName": affinity += kv.Value * (1 - lengthNorm); break;
                        case "longName": affinity += kv.Value * lengthNorm; break;
                        default:
                            freq.TryGetValue(kv.Key, out var f);
                            affinity += kv.Value * (f / letters);
                            break;
                    }
                }
                total += affinity * trait.Score;
                weightSum += trait.Score;
            }
            return weightSum > 0 ? total / weightSum : 0;
        }

        /// <summary>Legality penalty: 0 for clean names, negative for awkward ones.</summary>
        public static double LegalityPenalty(string name, JoinerRulesDef rules)
        {
            double penalty = 0;
            foreach (var word in name.Split(' '))
            {
                int letters = 0, vowels = 0;
                foreach (char raw in word)
                {
                    char c = char.ToLowerInvariant(raw);
                    if (!char.IsLetter(c)) continue;
                    letters++;
                    if ("aeiouy".IndexOf(c) >= 0) vowels++;
                }
                if (letters == 0) continue;
                if (letters < rules.MinLength) penalty -= 0.5;
                if (letters > rules.MaxLength) penalty -= 0.5 * (letters - rules.MaxLength);
                double vowelRatio = (double)vowels / letters;
                if (vowelRatio < 0.2 || vowelRatio > 0.7) penalty -= 0.4;
            }
            return penalty;
        }
    }
}
