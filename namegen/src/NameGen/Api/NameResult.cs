using System.Collections.Generic;

namespace NameGen
{
    public enum NameShape { Single, Full }

    /// <summary>One assembled piece of the name and why it was chosen.</summary>
    public sealed record NamePart
    {
        public string Form { get; init; } = "";
        public string Gloss { get; init; } = "";
        public string SourceRegister { get; init; } = "";
        public IReadOnlyList<string> Tags { get; init; } = new List<string>();
        /// <summary>True when this part came from cross-register bleed or the mundane pool.</summary>
        public bool IsBleed { get; init; }
        public bool IsMundane { get; init; }
    }

    /// <summary>A salient trait that drove generation, with its score.</summary>
    public sealed record TraitScore(string Name, double Score);

    public sealed record NameResult
    {
        public string Display { get; init; } = "";
        public string Register { get; init; } = "";
        public NameShape Shape { get; init; }
        public IReadOnlyList<NamePart> Parts { get; init; } = new List<NamePart>();
        /// <summary>The traits that biased morpheme selection and scoring, most salient first.</summary>
        public IReadOnlyList<TraitScore> SalientTraits { get; init; } = new List<TraitScore>();
        /// <summary>Extracted feature values, for debugging and tuning.</summary>
        public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();
    }

    public sealed record NameOptions
    {
        /// <summary>Seed for reproducibility (tests, debugging). Null = time-derived entropy.</summary>
        public ulong? Seed { get; init; }
        /// <summary>Force a register by name ("fantasy", "scifi", "horror", "normal"). Null = weighted random.</summary>
        public string? Register { get; init; }
        /// <summary>Force single or full. Null = register-weighted random.</summary>
        public NameShape? Shape { get; init; }
        /// <summary>Candidates generated before scoring picks one. Higher = cleaner but more uniform.</summary>
        public int CandidateCount { get; init; } = 12;
        /// <summary>Override the per-register cross-register bleed probability. Null = register default.</summary>
        public double? BleedProbability { get; init; }
        /// <summary>Override the per-register mundane-pool probability. Null = register default.</summary>
        public double? MundaneProbability { get; init; }

        public static readonly NameOptions Default = new();
    }
}
