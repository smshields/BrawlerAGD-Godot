using System.Collections.Generic;
using System.Text;
using NameGen.Data;

namespace NameGen.Core
{
    /// <summary>Normalized substring screening: lowercase, letters only, common leet folded.</summary>
    public sealed class Blocklist
    {
        private readonly List<string> _substrings;

        public Blocklist(BlocklistDef def)
        {
            _substrings = new List<string>();
            foreach (var s in def.Substrings)
            {
                var n = Normalize(s);
                if (n.Length > 0) _substrings.Add(n);
            }
        }

        public bool IsBlocked(string name)
        {
            string n = Normalize(name);
            foreach (var s in _substrings)
                if (n.Contains(s)) return true;
            return false;
        }

        internal static string Normalize(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (char raw in input)
            {
                char c = char.ToLowerInvariant(raw);
                switch (c)
                {
                    case '0': c = 'o'; break;
                    case '1': c = 'i'; break;
                    case '3': c = 'e'; break;
                    case '4': c = 'a'; break;
                    case '5': c = 's'; break;
                    case '7': c = 't'; break;
                    case '$': c = 's'; break;
                    case '@': c = 'a'; break;
                }
                if (c >= 'a' && c <= 'z') sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
