using System.Text.Json;
using BrawlerSim.Fitness;

namespace BrawlerSim.Serialization;

/// <summary>The on-disk / in-manifest shape of one term.</summary>
public sealed class FitnessTermDoc
{
    public string? Id { get; set; }

    /// <summary>Constants this term was set to. Keys are term parameter keys and are
    /// written VERBATIM — JsonOptions.Document sets no DictionaryKeyPolicy, so the
    /// camelCase property policy does not rewrite them.</summary>
    public Dictionary<string, float>? Params { get; set; }
}

/// <summary>The on-disk / in-manifest shape of a fitness recipe. Public because
/// run.json embeds it by value (see RunStore) as well as it living in its own file.</summary>
public sealed class FitnessRecipeDoc
{
    public int FormatVersion { get; set; }
    public string? Name { get; set; }
    public List<FitnessTermDoc>? Terms { get; set; }
}

/// <summary>
/// Read/write for <see cref="FitnessRecipe"/> (2026-09-16, fitness builder phase 3).
///
/// FLOAT EXACTNESS IS THE CONTRACT. A recipe's constants are floats persisted as JSON
/// text; if a value does not round-trip exactly, a resumed run scores differently in
/// the last bit from the run it is continuing — which is precisely the drift the
/// characterization harness exists to catch. System.Text.Json writes the shortest
/// round-trippable form for Single, and FitnessRecipeJsonTests pins that over random
/// bit patterns rather than trusting it.
/// </summary>
public static class FitnessRecipeJson
{
    private static readonly JsonSerializerOptions Options = JsonOptions.Document;

    public static FitnessRecipeDoc ToDoc(FitnessRecipe recipe) => new()
    {
        FormatVersion = recipe.FormatVersion,
        Name = recipe.Name,
        Terms = recipe.Terms
            .Select(term => new FitnessTermDoc
            {
                Id = term.Id,
                Params = new Dictionary<string, float>(term.Params, StringComparer.Ordinal),
            })
            .ToList(),
    };

    /// <summary>Rebuild a recipe from a document, validating it. Throws rather than
    /// silently dropping a malformed term — a run must not start under an instrument
    /// nobody can reconstruct.</summary>
    public static FitnessRecipe FromDoc(FitnessRecipeDoc doc)
    {
        var recipe = new FitnessRecipe(
            doc.Name ?? throw new JsonException("Fitness recipe is missing 'name'."),
            (doc.Terms ?? throw new JsonException("Fitness recipe is missing 'terms'."))
                .Select(term => new FitnessRecipe.TermEntry(
                    term.Id ?? throw new JsonException("A fitness recipe term is missing 'id'."),
                    new Dictionary<string, float>(
                        term.Params ?? new Dictionary<string, float>(), StringComparer.Ordinal)))
                .ToArray())
        {
            // A document with no formatVersion is treated as the current one rather
            // than as version 0, so a hand-written recipe does not have to carry it.
            FormatVersion = doc.FormatVersion == 0 ? FitnessRecipe.CurrentFormatVersion : doc.FormatVersion,
        };
        recipe.Validate();
        return recipe;
    }

    public static string Serialize(FitnessRecipe recipe) =>
        JsonSerializer.Serialize(ToDoc(recipe), Options);

    public static FitnessRecipe Deserialize(string json) =>
        FromDoc(JsonSerializer.Deserialize<FitnessRecipeDoc>(json, Options)
            ?? throw new JsonException("Could not parse the fitness recipe."));

    public static void Save(FitnessRecipe recipe, string path) =>
        File.WriteAllText(path, Serialize(recipe));

    public static FitnessRecipe Load(string path)
    {
        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new JsonException($"{path}: {error.Message}", error);
        }
    }
}
