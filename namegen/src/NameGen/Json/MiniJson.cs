using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NameGen.Json
{
    /// <summary>
    /// Minimal JSON parser so the library ships with zero dependencies (one DLL into
    /// Godot, no NuGet restore). Supports the full JSON grammar plus // line comments
    /// and trailing commas, since the data files are hand-authored.
    /// Produces Dictionary&lt;string, object?&gt;, List&lt;object?&gt;, string, double, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public static object? Parse(string text)
        {
            int pos = 0;
            var value = ParseValue(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (pos != text.Length)
                throw new FormatException($"JSON: trailing content at offset {pos}.");
            return value;
        }

        private static object? ParseValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new FormatException("JSON: unexpected end of input.");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject(s, ref pos);
                case '[': return ParseArray(s, ref pos);
                case '"': return ParseString(s, ref pos);
                case 't': Expect(s, ref pos, "true"); return true;
                case 'f': Expect(s, ref pos, "false"); return false;
                case 'n': Expect(s, ref pos, "null"); return null;
                default: return ParseNumber(s, ref pos);
            }
        }

        private static Dictionary<string, object?> ParseObject(string s, ref int pos)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            pos++; // '{'
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) throw new FormatException("JSON: unterminated object.");
                if (s[pos] == '}') { pos++; return result; }
                string key = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException($"JSON: expected ':' at offset {pos}.");
                pos++;
                result[key] = ParseValue(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                if (pos < s.Length && s[pos] == '}') { pos++; return result; }
                throw new FormatException($"JSON: expected ',' or '}}' at offset {pos}.");
            }
        }

        private static List<object?> ParseArray(string s, ref int pos)
        {
            var result = new List<object?>();
            pos++; // '['
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) throw new FormatException("JSON: unterminated array.");
                if (s[pos] == ']') { pos++; return result; }
                result.Add(ParseValue(s, ref pos));
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                if (pos < s.Length && s[pos] == ']') { pos++; return result; }
                throw new FormatException($"JSON: expected ',' or ']' at offset {pos}.");
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            if (s[pos] != '"') throw new FormatException($"JSON: expected string at offset {pos}.");
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= s.Length) break;
                    char esc = s[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > s.Length) throw new FormatException("JSON: bad \\u escape.");
                            sb.Append((char)ushort.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw new FormatException($"JSON: bad escape '\\{esc}'.");
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("JSON: unterminated string.");
        }

        private static double ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && ("+-0123456789.eE".IndexOf(s[pos]) >= 0)) pos++;
            if (pos == start) throw new FormatException($"JSON: unexpected character '{s[pos]}' at offset {pos}.");
            return double.Parse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string s, ref int pos, string token)
        {
            if (pos + token.Length > s.Length || s.Substring(pos, token.Length) != token)
                throw new FormatException($"JSON: expected '{token}' at offset {pos}.");
            pos += token.Length;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { pos++; continue; }
                if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
                {
                    while (pos < s.Length && s[pos] != '\n') pos++;
                    continue;
                }
                break;
            }
        }
    }
}
