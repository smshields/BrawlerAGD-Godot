using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>
/// Reads and writes the built-game document (2026-08-13, Game Builder): a
/// PARAMETERIZED, SELF-CONTAINED file — every character and stage is compiled in
/// using the game.json element shapes, so a built game survives deleting the runs it
/// was assembled from.
///
/// Format history:
///   1 — original: { formatVersion, name, characters: [{ displayName, origin,
///       character }], stages: [{ displayName, origin, stage }] }.
///   2 — 2026-08-22 sprite selection: character entries gained "spriteId" +
///       "register" (the negotiated presentation, persisted once by the game-open
///       pass like names are; omitted when null). v1 files load with nulls and get
///       both on their next open.
///   3 — 2026-08-23 melee attack sprites (M4b): character entries gained
///       "moveSpriteIds" — one entry per move, null on non-attack slots; resolved
///       around the NEGOTIATED character sprite (whose wields may differ from the
///       genome gene's) and persisted once. ≤2 files gain them on next open.
///   4 — 2026-09-01 thin platforms: platform docs may carry "thin" (written only
///       when true — solid platforms keep their pre-v12 bytes, so ContentKey and
///       the naming/sprite seeds of legacy content are unchanged). ≤3 files load
///       all-solid via the shared game.json element shapes.
///   5 — 2026-09-01 stage tile themes (M4d): stage entries gained "themeId" +
///       "register" (the negotiated presentation, persisted once with the stage's
///       name by the game-open pass; omitted when null). ≤4 files load with nulls
///       and get both on their next open. ContentKey excludes the theme gene.
///   6 — 2026-09-02 backgrounds: stage entries gained "backgroundId" +
///       "backgroundRemap" (the negotiated bg-v1 entry + palette remap target,
///       settled by the same pass after the tile theme; omitted when null). ≤5
///       files load with nulls and get both on their next open. ContentKey excludes
///       the background gene.
/// </summary>
public static class BuiltGameJson
{
    public const int CurrentFormatVersion = 6;
    private const int MinSupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = JsonOptions.Document;

    public static string Serialize(BuiltGame game)
    {
        var doc = new BuiltGameDoc
        {
            FormatVersion = CurrentFormatVersion,
            Name = game.Name,
            Characters = game.Characters.Select(c => new BuiltCharacterDoc
            {
                DisplayName = c.DisplayName,
                Origin = c.Origin,
                SpriteId = c.SpriteId,
                Register = c.Register,
                MoveSpriteIds = c.MoveSpriteIds?.ToList(),
                Character = GameGenomeJson.ToCharacterDoc(c.Character),
            }).ToList(),
            Stages = game.Stages.Select(s => new BuiltStageDoc
            {
                DisplayName = s.DisplayName,
                Origin = s.Origin,
                ThemeId = s.ThemeId,
                Register = s.Register,
                BackgroundId = s.BackgroundId,
                BackgroundRemap = s.BackgroundRemap,
                Stage = GameGenomeJson.ToStageDoc(s.Stage),
            }).ToList(),
        };
        return JsonSerializer.Serialize(doc, Options);
    }

    public static BuiltGame Deserialize(string json, GenerationConfig? config = null)
    {
        config ??= GenerationConfig.Default;
        BuiltGameDoc doc = JsonSerializer.Deserialize<BuiltGameDoc>(json, Options)
            ?? throw new JsonException("built game parsed to null.");
        if (doc.FormatVersion is < MinSupportedFormatVersion or > CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"built-game formatVersion {doc.FormatVersion} is not supported "
                + $"(expected {MinSupportedFormatVersion}..{CurrentFormatVersion}).");
        }
        var game = new BuiltGame { Name = doc.Name ?? "UNTITLED" };
        foreach (BuiltCharacterDoc c in doc.Characters ?? new List<BuiltCharacterDoc>())
        {
            game.Characters.Add(new BuiltCharacter(
                c.DisplayName ?? "UNNAMED",
                c.Origin,
                GameGenomeJson.CharacterFromDoc(
                    c.Character ?? throw new JsonException("built game entry is missing its character."),
                    config),
                c.SpriteId,
                c.Register,
                c.MoveSpriteIds));
        }
        foreach (BuiltStageDoc s in doc.Stages ?? new List<BuiltStageDoc>())
        {
            game.Stages.Add(new BuiltStage(
                s.DisplayName ?? "UNNAMED",
                s.Origin,
                GameGenomeJson.StageFromDoc(
                    s.Stage ?? throw new JsonException("built game entry is missing its stage."),
                    config),
                s.ThemeId,
                s.Register,
                s.BackgroundId,
                s.BackgroundRemap));
        }
        return game;
    }

    public static void Save(BuiltGame game, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Serialize(game));
    }

    public static BuiltGame Load(string path, GenerationConfig? config = null) =>
        Deserialize(File.ReadAllText(path), config);

    private sealed class BuiltGameDoc
    {
        public int FormatVersion { get; set; }
        public string? Name { get; set; }
        public List<BuiltCharacterDoc>? Characters { get; set; }
        public List<BuiltStageDoc>? Stages { get; set; }
    }

    private sealed class BuiltCharacterDoc
    {
        public string? DisplayName { get; set; }
        public string? Origin { get; set; }
        public string? SpriteId { get; set; } // v2+; the negotiated presentation
        public string? Register { get; set; } // v2+
        public List<string?>? MoveSpriteIds { get; set; } // v3+; null on non-attack slots
        public GameGenomeJson.CharacterDoc? Character { get; set; }
    }

    private sealed class BuiltStageDoc
    {
        public string? DisplayName { get; set; }
        public string? Origin { get; set; }
        public string? ThemeId { get; set; }  // v5+; omitted when null
        public string? Register { get; set; } // v5+; omitted when null
        public string? BackgroundId { get; set; }    // v6+; omitted when null
        public string? BackgroundRemap { get; set; } // v6+; omitted when null
        public GameGenomeJson.StageDoc? Stage { get; set; }
    }
}
