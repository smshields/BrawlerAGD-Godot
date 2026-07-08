using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Serialization;

/// <summary>
/// Reads and writes the single-file game.json format (formatVersion 1). Params are
/// serialized by name in schema order, so files stay human-readable and diff-able and
/// survive schema extension (unknown keys in a file are ignored; missing keys throw).
/// </summary>
public static class GameGenomeJson
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(GameRecord record)
    {
        var doc = new GameDoc
        {
            FormatVersion = CurrentFormatVersion,
            Name = record.Name,
            Origin = record.Origin,
            Characters = record.Genome.Characters.Select(c => new CharacterDoc
            {
                Name = c.Name,
                Stocks = c.Stocks,
                SpriteIndex = c.SpriteIndex,
                Params = c.Params.ToDictionary(),
                Moves = c.Moves.Select(m => new MoveDoc
                {
                    SpriteIndex = m.SpriteIndex,
                    Params = m.Params.ToDictionary(),
                }).ToList(),
            }).ToList(),
            Stage = new StageDoc
            {
                Platforms = record.Genome.Stage.Platforms
                    .Select(p => new PlatformDoc { X = p.X, Y = p.Y, XSize = p.XSize, YSize = p.YSize })
                    .ToList(),
            },
        };
        return JsonSerializer.Serialize(doc, Options);
    }

    public static GameRecord Deserialize(string json, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        GameDoc doc = JsonSerializer.Deserialize<GameDoc>(json, Options)
            ?? throw new JsonException("game.json parsed to null.");
        if (doc.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"game.json formatVersion {doc.FormatVersion} is not supported (expected {CurrentFormatVersion}).");
        }
        if (doc.Characters is null || doc.Stage?.Platforms is null)
        {
            throw new JsonException("game.json is missing characters or stage.");
        }

        var characters = doc.Characters.Select(c => new CharacterGenome(
            c.Name ?? "Unnamed",
            c.Stocks,
            c.SpriteIndex,
            ParamSet.FromDictionary(config.CharacterSchema, Require(c.Params, "character params")),
            (c.Moves ?? new List<MoveDoc>()).Select(m => new MoveGenome(
                ParamSet.FromDictionary(config.MoveSchema, Require(m.Params, "move params")),
                m.SpriteIndex))));

        var stage = new StageGenome(doc.Stage.Platforms.Select(p => new PlatformGene(p.X, p.Y, p.XSize, p.YSize)));
        return new GameRecord(doc.Name ?? "Unnamed", doc.Origin, new GameGenome(characters, stage));
    }

    public static void Save(GameRecord record, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Serialize(record));
    }

    public static GameRecord Load(string path, GenerationConfig? config = null) =>
        Deserialize(File.ReadAllText(path), config);

    private static Dictionary<string, float> Require(Dictionary<string, float>? dict, string what) =>
        dict ?? throw new JsonException($"game.json is missing {what}.");

    // DTOs — the on-disk shape. Do not reuse genome types here: the file format must be
    // able to evolve independently of the in-memory model.
    private sealed class GameDoc
    {
        public int FormatVersion { get; set; }
        public string? Name { get; set; }
        public string? Origin { get; set; }
        public List<CharacterDoc>? Characters { get; set; }
        public StageDoc? Stage { get; set; }
    }

    private sealed class CharacterDoc
    {
        public string? Name { get; set; }
        public int Stocks { get; set; }
        public int SpriteIndex { get; set; }
        public Dictionary<string, float>? Params { get; set; }
        public List<MoveDoc>? Moves { get; set; }
    }

    private sealed class MoveDoc
    {
        public int SpriteIndex { get; set; }
        public Dictionary<string, float>? Params { get; set; }
    }

    private sealed class StageDoc
    {
        public List<PlatformDoc>? Platforms { get; set; }
    }

    private sealed class PlatformDoc
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int XSize { get; set; }
        public int YSize { get; set; }
    }
}
