using System;
using System.Collections.Generic;
using System.Text;
using NameGen.Data;

namespace NameGen.Core
{
    /// <summary>
    /// Assembles morpheme parts into a display name: boundary repair on fused parts,
    /// letter-run collapsing, capitalization. Register joiner rules parameterize it.
    /// </summary>
    public static class Joiner
    {
        private const string Vowels = "aeiouy";

        public sealed record Part(string Text, string Join); // Join: fuse|space|hyphen|apostrophe (first part's join ignored)

        public static string Assemble(IReadOnlyList<Part> parts, JoinerRulesDef rules)
        {
            var words = new List<StringBuilder>();
            foreach (var part in parts)
            {
                string text = part.Text;
                if (words.Count == 0 || part.Join != "fuse")
                {
                    var sb = new StringBuilder();
                    if (words.Count > 0 && part.Join == "hyphen") sb.Append('-');
                    else if (words.Count > 0 && part.Join == "apostrophe") sb.Append('\'');
                    // hyphen/apostrophe start a glued continuation of the previous word
                    if (words.Count > 0 && (part.Join == "hyphen" || part.Join == "apostrophe"))
                    {
                        words[words.Count - 1].Append(sb).Append(text);
                        continue;
                    }
                    sb.Append(text);
                    words.Add(sb);
                }
                else
                {
                    FuseInto(words[words.Count - 1], text);
                }
            }

            var outWords = new List<string>();
            foreach (var w in words)
                outWords.Add(Capitalize(Cleanup(w.ToString(), rules)));
            return string.Join(" ", outWords);
        }

        private static void FuseInto(StringBuilder head, string tail)
        {
            if (head.Length == 0) { head.Append(tail); return; }
            if (tail.Length == 0) return;

            char last = char.ToLowerInvariant(head[head.Length - 1]);
            char first = char.ToLowerInvariant(tail[0]);

            // Same letter at the boundary: collapse one ("kag"+"gor" -> "kagor").
            if (last == first)
            {
                head.Append(tail, 1, tail.Length - 1);
                return;
            }
            // Vowel collision: drop the tail's leading vowel ("kaga"+"ago" -> "kagago"... no:
            // "kaga"+"or" keeps both since l/f differ in class; here both vowels -> drop tail's.
            if (IsVowel(last) && IsVowel(first))
            {
                head.Append(tail, 1, tail.Length - 1);
                return;
            }
            head.Append(tail);
        }

        /// <summary>Collapse letter runs beyond the rules' limits and strip banned clusters by vowel insertion.</summary>
        internal static string Cleanup(string word, JoinerRulesDef rules)
        {
            var sb = new StringBuilder(word.Length + 2);
            int consonantRun = 0, vowelRun = 0;
            char prev = '\0';
            int repeat = 0;

            foreach (char raw in word)
            {
                char c = raw;
                bool letter = char.IsLetter(c);
                if (letter && char.ToLowerInvariant(c) == char.ToLowerInvariant(prev)) repeat++; else repeat = 0;
                if (repeat >= 2) continue; // never three of the same letter

                if (letter && IsVowel(char.ToLowerInvariant(c))) { vowelRun++; consonantRun = 0; }
                else if (letter) { consonantRun++; vowelRun = 0; }
                else { consonantRun = 0; vowelRun = 0; }

                if (vowelRun > rules.MaxVowelRun) continue;
                if (consonantRun > rules.MaxConsonantRun)
                {
                    // Break the cluster with a neutral vowel rather than dropping information.
                    sb.Append('a');
                    consonantRun = 1;
                }
                sb.Append(c);
                prev = c;
            }

            string result = sb.ToString();
            foreach (var cluster in rules.BannedClusters)
            {
                int idx;
                while ((idx = result.IndexOf(cluster, StringComparison.OrdinalIgnoreCase)) >= 0)
                    result = result.Substring(0, idx + 1) + "a" + result.Substring(idx + 1);
            }
            return result;
        }

        internal static string Capitalize(string word)
        {
            if (word.Length == 0) return word;
            var sb = new StringBuilder(word.Length);
            bool capNext = true;
            foreach (char c in word)
            {
                if (capNext && char.IsLetter(c)) { sb.Append(char.ToUpperInvariant(c)); capNext = false; }
                else sb.Append(char.ToLowerInvariant(c));
                if (c == '-' || c == '\'') capNext = false; // Vekta-9 stays; O'grim style handled upstream
                if (c == ' ') capNext = true;
            }
            return sb.ToString();
        }

        private static bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;
    }
}
