using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NameGen.Json;

namespace NameGen.Data
{
    /// <summary>Maps MiniJson's generic values onto the typed model records.</summary>
    internal static class DataMapper
    {
        // ---- generic helpers ----

        private static Dictionary<string, object?> AsObj(object? v, string ctx)
            => v as Dictionary<string, object?> ?? throw new InvalidDataException($"{ctx}: expected object.");

        private static List<object?> AsList(object? v, string ctx)
            => v as List<object?> ?? throw new InvalidDataException($"{ctx}: expected array.");

        private static string Str(Dictionary<string, object?> o, string key, string fallback = "")
            => o.TryGetValue(key, out var v) && v is string s ? s : fallback;

        private static double Num(Dictionary<string, object?> o, string key, double fallback)
            => o.TryGetValue(key, out var v) && v is double d ? d : fallback;

        private static int Int(Dictionary<string, object?> o, string key, int fallback)
            => (int)Num(o, key, fallback);

        private static bool? BoolOrNull(Dictionary<string, object?> o, string key)
            => o.TryGetValue(key, out var v) && v is bool b ? b : (bool?)null;

        private static List<string> StrList(Dictionary<string, object?> o, string key)
            => o.TryGetValue(key, out var v) && v is List<object?> list
                ? list.OfType<string>().ToList()
                : new List<string>();

        private static Dictionary<string, double> NumMap(Dictionary<string, object?> o, string key)
        {
            var result = new Dictionary<string, double>();
            if (o.TryGetValue(key, out var v) && v is Dictionary<string, object?> map)
                foreach (var kv in map)
                    if (kv.Value is double d) result[kv.Key] = d;
            return result;
        }

        // ---- model mapping ----

        public static RegisterDef MapRegister(string json, string source)
        {
            var o = AsObj(MiniJson.Parse(json), source);
            return new RegisterDef
            {
                Name = Str(o, "name"),
                Weight = Num(o, "weight", 1.0),
                ShapeWeights = NumMap(o, "shapeWeights"),
                BleedProbability = Num(o, "bleedProbability", 0),
                MundaneProbability = Num(o, "mundaneProbability", 0),
                Joiner = o.TryGetValue("joiner", out var j) && j != null ? MapJoiner(AsObj(j, source)) : new JoinerRulesDef(),
                Templates = AsList(o.TryGetValue("templates", out var t) ? t : null, $"{source}.templates")
                    .Select(x => MapTemplate(AsObj(x, source))).ToList(),
                Morphemes = AsList(o.TryGetValue("morphemes", out var m) ? m : null, $"{source}.morphemes")
                    .Select(x => MapMorpheme(AsObj(x, source))).ToList(),
            };
        }

        private static JoinerRulesDef MapJoiner(Dictionary<string, object?> o) => new()
        {
            MaxConsonantRun = Int(o, "maxConsonantRun", 3),
            MaxVowelRun = Int(o, "maxVowelRun", 2),
            BannedClusters = StrList(o, "bannedClusters"),
            MinLength = Int(o, "minLength", 3),
            MaxLength = Int(o, "maxLength", 14),
        };

        private static TemplateDef MapTemplate(Dictionary<string, object?> o) => new()
        {
            Kind = Str(o, "kind", "character"),
            Shape = Str(o, "shape", "single"),
            Weight = Num(o, "weight", 1.0),
            Note = Str(o, "note"),
            Slots = AsList(o.TryGetValue("slots", out var s) ? s : null, "template.slots")
                .Select(x => MapSlot(AsObj(x, "slot"))).ToList(),
        };

        private static SlotDef MapSlot(Dictionary<string, object?> o) => new()
        {
            Position = Str(o, "position", "standalone"),
            Join = Str(o, "join", "fuse"),
            Literal = o.TryGetValue("literal", out var l) && l is string ls ? ls : null,
            AllowMundane = BoolOrNull(o, "allowMundane"),
        };

        private static MorphemeDef MapMorpheme(Dictionary<string, object?> o) => new()
        {
            Form = Str(o, "form"),
            Positions = StrList(o, "positions"),
            Tags = StrList(o, "tags"),
            Weight = Num(o, "weight", 1.0),
            Gloss = Str(o, "gloss"),
        };

        public static TraitConfigDef MapTraits(string json, string source)
        {
            var o = AsObj(MiniJson.Parse(json), source);
            return new TraitConfigDef
            {
                BoostFactor = Num(o, "boostFactor", 6.0),
                SalienceTopK = Int(o, "salienceTopK", 3),
                SalienceThreshold = Num(o, "salienceThreshold", 0.12),
                Character = AsList(o.TryGetValue("character", out var c) ? c : null, $"{source}.character")
                    .Select(x => MapTrait(AsObj(x, source))).ToList(),
                Stage = AsList(o.TryGetValue("stage", out var s) ? s : null, $"{source}.stage")
                    .Select(x => MapTrait(AsObj(x, source))).ToList(),
            };
        }

        private static TraitDef MapTrait(Dictionary<string, object?> o) => new()
        {
            Name = Str(o, "name"),
            Features = NumMap(o, "features"),
            Phonemes = NumMap(o, "phonemes"),
        };

        public static SchemaRangesDef MapRanges(string json, string source)
        {
            var o = AsObj(MiniJson.Parse(json), source);
            return new SchemaRangesDef
            {
                Character = RangeMap(o, "character"),
                Move = RangeMap(o, "move"),
                Shield = RangeMap(o, "shield"),
                Dash = RangeMap(o, "dash"),
                Projectile = RangeMap(o, "projectile"),
                Stage = RangeMap(o, "stage"),
            };
        }

        private static Dictionary<string, RangeDef> RangeMap(Dictionary<string, object?> o, string key)
        {
            var result = new Dictionary<string, RangeDef>();
            if (o.TryGetValue(key, out var v) && v is Dictionary<string, object?> map)
                foreach (var kv in map)
                    if (kv.Value is Dictionary<string, object?> r)
                        result[kv.Key] = new RangeDef
                        {
                            Min = Num(r, "min", 0),
                            Max = Num(r, "max", 1),
                            Kind = Str(r, "kind", "linear"),
                        };
            return result;
        }

        public static MundaneDef MapMundane(string json, string source)
        {
            var o = AsObj(MiniJson.Parse(json), source);
            return new MundaneDef
            {
                Morphemes = AsList(o.TryGetValue("morphemes", out var m) ? m : null, $"{source}.morphemes")
                    .Select(x => MapMorpheme(AsObj(x, source))).ToList(),
            };
        }

        public static BlocklistDef MapBlocklist(string json, string source)
        {
            var o = AsObj(MiniJson.Parse(json), source);
            return new BlocklistDef { Substrings = StrList(o, "substrings") };
        }
    }
}
