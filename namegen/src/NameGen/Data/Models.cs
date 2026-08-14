using System.Collections.Generic;

namespace NameGen.Data
{
    /// <summary>A root/name unit in the database. Data-authored (JSON).</summary>
    public sealed record MorphemeDef
    {
        /// <summary>Surface string, lowercase; capitalization is applied at assembly.</summary>
        public string Form { get; init; } = "";
        /// <summary>Slots this morpheme may fill: prefix, suffix, standalone, given, family, adjective, place.</summary>
        public IReadOnlyList<string> Positions { get; init; } = new List<string>();
        /// <summary>Trait tags this morpheme evokes (must match trait names in traits.json).</summary>
        public IReadOnlyList<string> Tags { get; init; } = new List<string>();
        public double Weight { get; init; } = 1.0;
        /// <summary>Authoring note; also surfaced in provenance.</summary>
        public string Gloss { get; init; } = "";
    }

    /// <summary>One slot in a template. Attaches to the previous slot via Join.</summary>
    public sealed record SlotDef
    {
        public string Position { get; init; } = "standalone";
        /// <summary>fuse | space | hyphen | apostrophe. Ignored on the first slot.</summary>
        public string Join { get; init; } = "fuse";
        /// <summary>Literal text instead of a sampled morpheme. '#' expands to a digit, '@' to an uppercase letter.</summary>
        public string? Literal { get; init; }
        /// <summary>Whether the mundane pool may hijack this slot. Null = default by position.</summary>
        public bool? AllowMundane { get; init; }
    }

    public sealed record TemplateDef
    {
        /// <summary>"character" or "stage".</summary>
        public string Kind { get; init; } = "character";
        /// <summary>"single" or "full".</summary>
        public string Shape { get; init; } = "single";
        public double Weight { get; init; } = 1.0;
        public IReadOnlyList<SlotDef> Slots { get; init; } = new List<SlotDef>();
        public string Note { get; init; } = "";
    }

    public sealed record JoinerRulesDef
    {
        public int MaxConsonantRun { get; init; } = 3;
        public int MaxVowelRun { get; init; } = 2;
        public IReadOnlyList<string> BannedClusters { get; init; } = new List<string>();
        public int MinLength { get; init; } = 3;
        public int MaxLength { get; init; } = 14;
    }

    public sealed record RegisterDef
    {
        public string Name { get; init; } = "";
        public double Weight { get; init; } = 1.0;
        public IReadOnlyDictionary<string, double> ShapeWeights { get; init; } = new Dictionary<string, double>();
        /// <summary>Probability per content slot of sampling from another register's pool.</summary>
        public double BleedProbability { get; init; }
        /// <summary>Probability per eligible slot of sampling from the mundane pool.</summary>
        public double MundaneProbability { get; init; }
        public JoinerRulesDef Joiner { get; init; } = new();
        public IReadOnlyList<TemplateDef> Templates { get; init; } = new List<TemplateDef>();
        public IReadOnlyList<MorphemeDef> Morphemes { get; init; } = new List<MorphemeDef>();
    }

    /// <summary>A trait: weighted feature combination plus phoneme affinity for scoring.</summary>
    public sealed record TraitDef
    {
        public string Name { get; init; } = "";
        /// <summary>featureName → weight. Features contribute weight*(2f-1).</summary>
        public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();
        /// <summary>Phoneme-class affinities used by the candidate scorer.</summary>
        public IReadOnlyDictionary<string, double> Phonemes { get; init; } = new Dictionary<string, double>();
    }

    public sealed record TraitConfigDef
    {
        public double BoostFactor { get; init; } = 6.0;
        public int SalienceTopK { get; init; } = 3;
        public double SalienceThreshold { get; init; } = 0.12;
        public IReadOnlyList<TraitDef> Character { get; init; } = new List<TraitDef>();
        public IReadOnlyList<TraitDef> Stage { get; init; } = new List<TraitDef>();
    }

    /// <summary>Generation range + interpretation for one param key.</summary>
    public sealed record RangeDef
    {
        public double Min { get; init; }
        public double Max { get; init; }
        /// <summary>linear | bool | int | offAtZero.</summary>
        public string Kind { get; init; } = "linear";
    }

    public sealed record SchemaRangesDef
    {
        public IReadOnlyDictionary<string, RangeDef> Character { get; init; } = new Dictionary<string, RangeDef>();
        public IReadOnlyDictionary<string, RangeDef> Move { get; init; } = new Dictionary<string, RangeDef>();
        public IReadOnlyDictionary<string, RangeDef> Shield { get; init; } = new Dictionary<string, RangeDef>();
        public IReadOnlyDictionary<string, RangeDef> Dash { get; init; } = new Dictionary<string, RangeDef>();
        public IReadOnlyDictionary<string, RangeDef> Projectile { get; init; } = new Dictionary<string, RangeDef>();
        public IReadOnlyDictionary<string, RangeDef> Stage { get; init; } = new Dictionary<string, RangeDef>();
    }

    public sealed record MundaneDef
    {
        public IReadOnlyList<MorphemeDef> Morphemes { get; init; } = new List<MorphemeDef>();
    }

    public sealed record BlocklistDef
    {
        public IReadOnlyList<string> Substrings { get; init; } = new List<string>();
    }
}
