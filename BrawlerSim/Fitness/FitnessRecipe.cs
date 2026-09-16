using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Fitness.Terms;

namespace BrawlerSim.Fitness;

/// <summary>
/// A fitness function AS DATA (2026-09-16, fitness builder phase 3): a name and an
/// ordered list of term ids with their constants. This is the whole instrument in one
/// value — no inheritance, no wrapper chain, no defaults resolved somewhere else at
/// load time.
///
/// WHY IT EXISTS: every result in this project must be reproducible from (genome,
/// seed) — and that means from the instrument that scored it too. A run scored by a
/// designer-built fitness records this document BY VALUE in run.json, so resuming or
/// replaying reconstructs the exact scoring that produced the history, even if the
/// recipe file on disk was edited afterwards or deleted.
///
/// ORDER IS PART OF THE RECIPE. Float addition is not associative, so re-ordering
/// terms changes the score in the last bits. The list is the definition.
///
/// OMITTING A TERM IS HOW YOU TURN IT OFF, rather than zeroing its weight — the
/// breakdown then says what the instrument actually measures instead of printing a row
/// of zeros.
/// </summary>
public sealed record FitnessRecipe(string Name, IReadOnlyList<FitnessRecipe.TermEntry> Terms)
{
    /// <summary>Bumped only when the DOCUMENT SHAPE changes. Term ids and parameter
    /// keys are permanent once shipped, so adding a term is not a format change.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>One term: its catalog id and the constants it was set to. Keys absent
    /// from Params take the term's declared default, so a document only has to carry
    /// what it changed.</summary>
    public sealed record TermEntry(string Id, IReadOnlyDictionary<string, float> Params);

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Build the live fitness. Validates first, so a malformed document fails
    /// at load rather than halfway through an evolution run.</summary>
    public FitnessComposer ToFitness()
    {
        Validate();
        return new FitnessComposer(Name, Terms.Select(t => FitnessTermRegistry.Create(t.Id, t.Params)));
    }

    /// <summary>
    /// Every way a document can be wrong, checked in one place:
    /// - the name may not collide with a SHIPPED version, because a run scored by a
    ///   custom instrument must never be mistakable for a shipped one in a manifest,
    ///   a chart, or a filename;
    /// - every term id must be in the catalog;
    /// - every parameter key must be one that term declares (a typo would otherwise
    ///   read as "that knob does nothing");
    /// - a term may not appear twice, which would silently double its contribution.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A fitness recipe needs a name.");
        }
        if (FitnessRegistry.Names.Contains(Name))
        {
            throw new ArgumentException(
                $"'{Name}' is a shipped fitness version — a custom recipe must take a different name " +
                "so runs scored by it can never be mistaken for a shipped version.");
        }
        if (FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new ArgumentException(
                $"Fitness recipe format version {FormatVersion} is not readable (current is {CurrentFormatVersion}).");
        }
        if (Terms.Count == 0)
        {
            throw new ArgumentException($"Fitness recipe '{Name}' has no terms — it would score every match 0.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TermEntry term in Terms)
        {
            if (!seen.Add(term.Id))
            {
                throw new ArgumentException(
                    $"Fitness recipe '{Name}' lists term '{term.Id}' twice — it would count twice.");
            }
            // Throws on an unknown id, and (via the term's own constructor) on any
            // parameter key that term does not declare.
            FitnessTermRegistry.Create(term.Id, term.Params);
        }
    }

    /// <summary>Capture a live fitness as a recipe — how a shipped version is opened
    /// for editing, and how an assembly is saved. Rejects standard-v2, which predates
    /// term composition.</summary>
    public static FitnessRecipe Capture(string name, IFitnessFunction fitness)
    {
        if (fitness is not IFitnessTermList composed)
        {
            throw new ArgumentException(
                $"'{fitness.Name}' is not built from terms and cannot be captured as a recipe.");
        }
        return new FitnessRecipe(name, composed.Terms
            .Select(term => new TermEntry(term.Id, new Dictionary<string, float>(term.Values, StringComparer.Ordinal)))
            .ToArray());
    }

    /// <summary>A shipped version as an editable recipe, under a new name — the seed a
    /// designer starts from. The result scores identically to the version it came from
    /// (pinned by test); only the name differs.</summary>
    public static FitnessRecipe FromShippedVersion(
        string shippedName,
        string recipeName,
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float? collisionScalar = null,
        int playerCount = 2) =>
        Capture(recipeName, FitnessRegistry.Create(
            shippedName, targetLengthSeconds, maxLengthSeconds, collisionScalar, playerCount));

    /// <summary>The CURRENT DEFAULT version as an editable recipe.</summary>
    public static FitnessRecipe FromDefault(
        string recipeName = "custom/untitled",
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float? collisionScalar = null,
        int playerCount = 2) =>
        FromShippedVersion(
            FitnessRegistry.DefaultNameFor(playerCount), recipeName,
            targetLengthSeconds, maxLengthSeconds, collisionScalar, playerCount);

    /// <summary>
    /// FNV-1a over the recipe's canonical form — the provenance stamp recorded beside
    /// the embedded document in run.json. Parameter VALUES enter as raw IEEE bits, so
    /// two recipes that differ in the last bit of one constant hash differently: this
    /// is the check that a resumed run is being scored by the same instrument, not
    /// merely by one with the same name.
    ///
    /// Deliberately NOT computed from the JSON text: whitespace, key order, and float
    /// formatting are the serializer's business, and the hash must not move when those
    /// do. Keys are sorted ordinally here so a document that round-trips through a
    /// dictionary hashes the same either way.
    /// </summary>
    public ulong ContentHash
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(FormatVersion).Append('|').Append(Name);
            foreach (TermEntry term in Terms)
            {
                sb.Append('|').Append(term.Id);
                foreach (string key in term.Params.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    sb.Append(';').Append(key).Append('=')
                      .Append(BitConverter.SingleToInt32Bits(term.Params[key]).ToString("X8"));
                }
            }
            return Fnv1a.Hash(Encoding.UTF8.GetBytes(sb.ToString()), Fnv1a.OffsetBasis);
        }
    }
}
