using System.Text.Json;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Serialization;

/// <summary>
/// Imports a Unity BrawlerAGD game folder (level.json, player1.json, player2.json,
/// p1move1.json, p2move1.json) into a GameRecord. Values are taken verbatim — no
/// re-derivation, no constraints re-applied — so the AIIDE '22 study games (A–F) and any
/// archived population load exactly as evolved. Move 2 (the unfinished shield) is
/// intentionally not imported, matching the decision to drop it from the port.
/// </summary>
public static class LegacyImporter
{
    public static GameRecord ImportGameFolder(string directory, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        string name = new DirectoryInfo(directory).Name;

        CharacterGenome player1 = ImportCharacter(
            Path.Combine(directory, "player1.json"), Path.Combine(directory, "p1move1.json"), config);
        CharacterGenome player2 = ImportCharacter(
            Path.Combine(directory, "player2.json"), Path.Combine(directory, "p2move1.json"), config);
        StageGenome stage = ImportStage(Path.Combine(directory, "level.json"));

        var genome = new GameGenome(new[] { player1, player2 }, stage);
        return new GameRecord(name, $"unity-import:{name}", genome);
    }

    private static CharacterGenome ImportCharacter(string playerPath, string movePath, GenerationConfig config)
    {
        using JsonDocument playerDoc = ParseFile(playerPath);
        JsonElement playerRoot = playerDoc.RootElement;

        ParamSet playerParams = ParamSet.FromDictionary(config.CharacterSchema, NumericFields(playerRoot));
        string name = playerRoot.TryGetProperty("playerName", out JsonElement nameEl)
            ? nameEl.GetString() ?? "Unnamed"
            : "Unnamed";
        int stocks = playerRoot.TryGetProperty("stocks", out JsonElement stocksEl) ? stocksEl.GetInt32() : 3;
        int spriteIndex = playerRoot.GetProperty("spriteIndex").GetInt32();

        using JsonDocument moveDoc = ParseFile(movePath);
        JsonElement moveRoot = moveDoc.RootElement;
        ParamSet moveParams = ParamSet.FromDictionary(config.MoveSchema, NumericFields(moveRoot));
        int moveSpriteIndex = moveRoot.GetProperty("spriteIndex").GetInt32();

        return new CharacterGenome(name, stocks, spriteIndex, playerParams,
            new[] { new MoveGenome(moveParams, moveSpriteIndex) });
    }

    private static StageGenome ImportStage(string levelPath)
    {
        using JsonDocument doc = ParseFile(levelPath);
        var platforms = new List<PlatformGene>();
        foreach (JsonElement p in doc.RootElement.GetProperty("platformList").EnumerateArray())
        {
            platforms.Add(new PlatformGene(
                p.GetProperty("x").GetInt32(),
                p.GetProperty("y").GetInt32(),
                p.GetProperty("xSize").GetInt32(),
                p.GetProperty("ySize").GetInt32()));
        }
        return new StageGenome(platforms);
    }

    /// <summary>
    /// All numeric fields of a legacy object as a name→float dictionary. The schemas pick
    /// the params they own; legacy-only fields (derived values, dead shield fields, key
    /// bindings) fall away here by design.
    /// </summary>
    private static Dictionary<string, float> NumericFields(JsonElement root)
    {
        var fields = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number)
            {
                fields[prop.Name] = prop.Value.GetSingle();
            }
        }
        return fields;
    }

    private static JsonDocument ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Legacy game file not found: {path}", path);
        }
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
