using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Serialization;

/// <summary>
/// Reads and writes the single-file game.json format (formatVersion 4). Params are
/// serialized by name in schema order, so files stay human-readable and diff-able and
/// survive schema extension (unknown keys in a file are ignored; missing keys throw).
///
/// Format history:
///   1 — original (Phase 1).
///   2 — 2026-07-08 multi-move controls: characters gained "buttonMoves" (the button→
///       move mapping gene, 4 ints). v1 files load with all-zeros (every button = move
///       0), which reproduces pre-feature behavior exactly; files are written as v2.
///   3 — 2026-07-12 shields: moves gained "type" ("attack" | "shield"); shield moves'
///       params use the shield schema. v1/v2 moves load as attacks.
///   4 — 2026-07-13 dashes: "dash" joins the type values (dash params schema).
/// </summary>
public static class GameGenomeJson
{
    public const int CurrentFormatVersion = 4;
    private const int MinSupportedFormatVersion = 1;

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
                ButtonMoves = c.ButtonMoves.ToList(),
                Moves = c.Moves.Select(m => new MoveDoc
                {
                    Type = m.Type switch
                    {
                        MoveType.Shield => "shield",
                        MoveType.Dash => "dash",
                        _ => "attack",
                    },
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
        if (doc.FormatVersion is < MinSupportedFormatVersion or > CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"game.json formatVersion {doc.FormatVersion} is not supported " +
                $"(expected {MinSupportedFormatVersion}..{CurrentFormatVersion}).");
        }
        if (doc.Characters is null || doc.Stage?.Platforms is null)
        {
            throw new JsonException("game.json is missing characters or stage.");
        }

        var characters = doc.Characters.Select(c => new CharacterGenome(
            c.Name ?? "Unnamed",
            c.Stocks,
            c.SpriteIndex,
            ParamSet.FromDictionary(config.CharacterSchema,
                WithCharacterDefaults(Require(c.Params, "character params"))),
            (c.Moves ?? new List<MoveDoc>()).Select(m => m.Type switch
            {
                "shield" => new MoveGenome(
                    ParamSet.FromDictionary(config.ShieldSchema, Require(m.Params, "shield params")),
                    m.SpriteIndex, MoveType.Shield),
                "dash" => new MoveGenome(
                    ParamSet.FromDictionary(config.DashSchema, Require(m.Params, "dash params")),
                    m.SpriteIndex, MoveType.Dash),
                _ => new MoveGenome(
                    ParamSet.FromDictionary(config.MoveSchema, Require(m.Params, "move params")),
                    m.SpriteIndex),
            }),
            c.ButtonMoves)); // null (v1 files) → all-zeros default in the ctor

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

    /// <summary>Neutral defaults for the 2026-07-13 character-schema appends: every
    /// value switches its mechanic OFF (crouchMoveSpeed 1.0 = unchanged speed), so
    /// pre-feature genomes play exactly as they always did.</summary>
    internal static readonly (string Key, float Value)[] CharacterParamDefaults =
    {
        (CharacterParams.FastFallAcceleration, 0f),
        (CharacterParams.CrouchAccelerationChange, 0f),
        (CharacterParams.CrouchSpeed, 0.1f),
        (CharacterParams.CrouchMoveSpeed, 1f),
        (CharacterParams.CrouchHeightRatio, 0.9f),
        (CharacterParams.DirectionalInfluence, 0f),
        (CharacterParams.DiKnockbackReduction, 0f),
    };

    internal static Dictionary<string, float> WithCharacterDefaults(Dictionary<string, float> dict)
    {
        foreach ((string key, float value) in CharacterParamDefaults)
        {
            dict.TryAdd(key, value);
        }
        return dict;
    }

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
        public List<int>? ButtonMoves { get; set; }
        public List<MoveDoc>? Moves { get; set; }
    }

    private sealed class MoveDoc
    {
        public string? Type { get; set; } // null/absent (v1/v2) → attack
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
