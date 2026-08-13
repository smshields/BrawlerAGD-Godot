using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>One roster entry: a display name the builder can edit, the provenance of
/// the element (source game.json's origin/path), and the compiled genome.</summary>
public sealed record BuiltCharacter(string DisplayName, string? Origin, CharacterGenome Character);

public sealed record BuiltStage(string DisplayName, string? Origin, StageGenome Stage);

/// <summary>
/// A curated, self-contained game assembled from evolved outputs (2026-08-13,
/// FEATURES.md §Game Menu / Game Builder; docs/features/game-builder.md). COMPLETE at
/// exactly 8 characters and 4 stages (strict Smash-style slots, designer); incomplete
/// games save and edit fine — completeness is what the future Game Player requires.
/// Duplicates are rejected by CONTENT (the same evolved character added from two
/// different files is still the same fighter). Entirely outside the evolution loop —
/// no schema, genome, or sim impact.
/// </summary>
public sealed class BuiltGame
{
    public const int RequiredCharacters = 8;
    public const int RequiredStages = 4;

    public string Name { get; set; } = "UNTITLED";
    public List<BuiltCharacter> Characters { get; } = new();
    public List<BuiltStage> Stages { get; } = new();

    public bool IsComplete =>
        Characters.Count == RequiredCharacters && Stages.Count == RequiredStages;

    /// <summary>Adds a character unless the roster is full or already contains the
    /// same fighter (content identity). False with a human-readable reason.</summary>
    public bool TryAddCharacter(BuiltCharacter entry, out string reason)
    {
        if (Characters.Count >= RequiredCharacters)
        {
            reason = $"roster full ({RequiredCharacters} characters)";
            return false;
        }
        string key = ContentKey(entry.Character);
        if (Characters.Any(c => ContentKey(c.Character) == key))
        {
            reason = "already in this game";
            return false;
        }
        Characters.Add(entry);
        reason = "";
        return true;
    }

    public bool TryAddStage(BuiltStage entry, out string reason)
    {
        if (Stages.Count >= RequiredStages)
        {
            reason = $"stage list full ({RequiredStages} stages)";
            return false;
        }
        string key = ContentKey(entry.Stage);
        if (Stages.Any(s => ContentKey(s.Stage) == key))
        {
            reason = "already in this game";
            return false;
        }
        Stages.Add(entry);
        reason = "";
        return true;
    }

    /// <summary>Content identity — the serialized element bytes (display names and
    /// provenance excluded), so the same evolved fighter/stage is a duplicate no
    /// matter which file it came from.</summary>
    public static string ContentKey(CharacterGenome character) =>
        JsonSerializer.Serialize(GameGenomeJson.ToCharacterDoc(character));

    public static string ContentKey(StageGenome stage) =>
        JsonSerializer.Serialize(GameGenomeJson.ToStageDoc(stage));
}

/// <summary>
/// Reads and writes the built-game document (2026-08-13, Game Builder): a
/// PARAMETERIZED, SELF-CONTAINED file — every character and stage is compiled in
/// using the game.json element shapes, so a built game survives deleting the runs it
/// was assembled from.
///
/// Format history:
///   1 — original: { formatVersion, name, characters: [{ displayName, origin,
///       character }], stages: [{ displayName, origin, stage }] }.
/// </summary>
public static class BuiltGameJson
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
                Character = GameGenomeJson.ToCharacterDoc(c.Character),
            }).ToList(),
            Stages = game.Stages.Select(s => new BuiltStageDoc
            {
                DisplayName = s.DisplayName,
                Origin = s.Origin,
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
        if (doc.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"built-game formatVersion {doc.FormatVersion} is not supported (expected 1..{CurrentFormatVersion}).");
        }
        var game = new BuiltGame { Name = doc.Name ?? "UNTITLED" };
        foreach (BuiltCharacterDoc c in doc.Characters ?? new List<BuiltCharacterDoc>())
        {
            game.Characters.Add(new BuiltCharacter(
                c.DisplayName ?? "UNNAMED",
                c.Origin,
                GameGenomeJson.CharacterFromDoc(
                    c.Character ?? throw new JsonException("built game entry is missing its character."),
                    config)));
        }
        foreach (BuiltStageDoc s in doc.Stages ?? new List<BuiltStageDoc>())
        {
            game.Stages.Add(new BuiltStage(
                s.DisplayName ?? "UNNAMED",
                s.Origin,
                GameGenomeJson.StageFromDoc(
                    s.Stage ?? throw new JsonException("built game entry is missing its stage."),
                    config)));
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
        public GameGenomeJson.CharacterDoc? Character { get; set; }
    }

    private sealed class BuiltStageDoc
    {
        public string? DisplayName { get; set; }
        public string? Origin { get; set; }
        public GameGenomeJson.StageDoc? Stage { get; set; }
    }
}
