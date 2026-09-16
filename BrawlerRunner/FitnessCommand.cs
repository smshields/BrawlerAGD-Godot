using System.Globalization;
using BrawlerSim.Fitness;
using BrawlerSim.Fitness.Terms;
using BrawlerSim.Serialization;

namespace BrawlerRunner;

/// <summary>
/// The `fitness` verb (2026-09-16, fitness builder phase 3): inspect the term catalog
/// and scaffold an editable recipe, so a designer-built instrument can be produced and
/// checked without a UI.
///
///   fitness terms                      the catalog: every term, its constants, defaults, ranges
///   fitness show --fitness standard-v7 print a shipped version as a recipe document
///   fitness show --fitness-recipe f.json  re-print a recipe (validates it, shows its hash)
///   fitness new --out my.json          scaffold a recipe from the current default version
/// </summary>
internal static class FitnessCommand
{
    public static int Run(string[] args)
    {
        string action = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1] : "terms";
        var opts = Commands.ParseOptions(args);
        return action switch
        {
            "terms" => Terms(),
            "show" => Show(opts),
            "new" => New(opts),
            _ => Unknown(action),
        };
    }

    private static int Unknown(string action)
    {
        Console.Error.WriteLine($"Unknown `fitness` action '{action}' (terms|show|new).");
        return 1;
    }

    private static int Terms()
    {
        Console.WriteLine($"{FitnessTermRegistry.All.Count} fitness terms:");
        Console.WriteLine();
        foreach (FitnessTermRegistry.Entry entry in FitnessTermRegistry.All)
        {
            Console.WriteLine($"  {entry.Id}  ({entry.Label})");
            Console.WriteLine($"    {entry.Doc}");
            foreach (FitnessTermParam spec in entry.Parameters)
            {
                string range = spec.IsFlag
                    ? "flag"
                    : FormattableString.Invariant($"{spec.Min:0.####} .. {spec.Max:0.####}");
                Console.WriteLine(FormattableString.Invariant(
                    $"      {spec.Key,-18} default {spec.Default,-10:0.####} [{range}]  {spec.Doc}"));
            }
            Console.WriteLine();
        }
        Console.WriteLine("Assemble these into a recipe with `fitness new`, then score with");
        Console.WriteLine("`evaluate --fitness-recipe <file>` or run one with `evolve --fitness-recipe <file>`.");
        return 0;
    }

    private static int Show(Dictionary<string, string> opts)
    {
        FitnessRecipe recipe = opts.TryGetValue("fitness-recipe", out string? path)
            ? FitnessRecipeJson.Load(path)
            : FromVersion(opts, opts.GetValueOrDefault("name", "custom/preview"));

        Console.WriteLine(FitnessRecipeJson.Serialize(recipe));
        Console.WriteLine($"// {recipe.Terms.Count} terms, content hash {recipe.ContentHash}");
        return 0;
    }

    private static int New(Dictionary<string, string> opts)
    {
        string outPath = Commands.Require(opts, "out");
        string name = opts.GetValueOrDefault("name", DefaultName(outPath));
        FitnessRecipe recipe = FromVersion(opts, name);

        FitnessRecipeJson.Save(recipe, outPath);
        Console.WriteLine($"Wrote {outPath}: '{recipe.Name}', {recipe.Terms.Count} terms, hash {recipe.ContentHash}.");
        Console.WriteLine("Edit the constants, then:");
        Console.WriteLine($"  evaluate --game <game.json> --fitness-recipe {outPath} --breakdown");
        Console.WriteLine();
        Console.WriteLine("A run scored by a recipe records the whole document in run.json, so the run");
        Console.WriteLine("stays reproducible even if this file is edited or deleted afterwards.");
        return 0;
    }

    /// <summary>A shipped version captured as an editable recipe. --fitness picks the
    /// version (default: the current default for --players); --target-seconds /
    /// --max-seconds / --collision-scalar seed the constants it is built with, since a
    /// recipe carries them inside its own terms from then on.</summary>
    private static FitnessRecipe FromVersion(Dictionary<string, string> opts, string name)
    {
        int players = Commands.GetInt(opts, "players", 2);
        string version = opts.GetValueOrDefault("fitness", FitnessRegistry.DefaultNameFor(players));
        return FitnessRecipe.FromShippedVersion(
            version,
            name,
            Commands.GetFloat(opts, "target-seconds", 45f),
            Commands.GetFloat(opts, "max-seconds", BrawlerSim.Sim.MatchConfig.Default.MaxMatchSeconds),
            opts.ContainsKey("collision-scalar") ? Commands.GetFloat(opts, "collision-scalar", 0f) : null,
            players);
    }

    /// <summary>custom/&lt;file stem&gt; — namespaced so a recipe is never mistaken for
    /// a shipped version at a glance (FitnessRecipe.Validate refuses an actual
    /// collision outright).</summary>
    private static string DefaultName(string outPath) =>
        "custom/" + Path.GetFileNameWithoutExtension(outPath).ToLowerInvariant();
}
