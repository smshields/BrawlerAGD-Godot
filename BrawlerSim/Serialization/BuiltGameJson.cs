using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;

namespace BrawlerSim.Serialization;

/// <summary>One roster entry: a display name the builder can edit, the provenance of
/// the element (source game.json's origin/path), and the compiled genome. SpriteId +
/// Register (2026-08-22, sprite-selection.md) are the NEGOTIATED presentation — the
/// game-open pass may settle on a different sprite than the genome's inherited gene
/// (name compatibility, per-roster overuse), and what it settles on persists here,
/// leaving the genome untouched.</summary>
public sealed record BuiltCharacter(string DisplayName, string? Origin, CharacterGenome Character,
    string? SpriteId = null, string? Register = null, IReadOnlyList<string?>? MoveSpriteIds = null)
{
    /// <summary>The genome as this roster presents it: the negotiated character sprite
    /// and per-move attack sprites (M4b) injected over the inherited genes — views and
    /// match launches read this; the stored genome stays untouched.</summary>
    public CharacterGenome Presented
    {
        get
        {
            CharacterGenome presented = SpriteId is null ? Character : Character.WithSpriteId(SpriteId);
            if (MoveSpriteIds is null || MoveSpriteIds.Count != presented.Moves.Count)
            {
                return presented;
            }
            List<MoveGenome>? moves = null;
            for (int m = 0; m < presented.Moves.Count; m++)
            {
                if (MoveSpriteIds[m] is { } id && id != presented.Moves[m].SpriteId)
                {
                    moves ??= presented.Moves.ToList();
                    moves[m] = presented.Moves[m].WithSpriteId(id);
                }
            }
            return moves is null
                ? presented
                : new CharacterGenome(presented.Name, presented.Stocks, presented.SpriteIndex,
                    presented.Params, moves, presented.ButtonMoves, presented.SpriteId);
        }
    }
}

/// <summary>One stage entry. ThemeId + Register (2026-09-01, M4d —
/// stage-tile-selection.md) are the NEGOTIATED presentation, settled with the stage's
/// generated name by the game-open pass and persisted once, like BuiltCharacter's
/// sprite fields; the stored genome stays untouched.</summary>
public sealed record BuiltStage(string DisplayName, string? Origin, StageGenome Stage,
    string? ThemeId = null, string? Register = null)
{
    /// <summary>The stage as this game presents it: the negotiated theme injected
    /// over the inherited gene — views and match launches read this.</summary>
    public StageGenome Presented => ThemeId is null ? Stage : Stage.WithThemeId(ThemeId);
}

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
    /// matter which file it came from. The semantic sprite gene is excluded too
    /// (2026-08-22, sprite-selection.md): it is presentation, not identity, and the
    /// naming/sprite seed derives from this key — the shared seed must not shift when
    /// selection or repair changes the look. Byte-identical to the pre-sprite key for
    /// every existing (null-gene) character.</summary>
    public static string ContentKey(CharacterGenome character)
    {
        GameGenomeJson.CharacterDoc doc = GameGenomeJson.ToCharacterDoc(character);
        doc.SpriteId = null;
        if (doc.Moves is not null)
        {
            foreach (GameGenomeJson.MoveDoc move in doc.Moves)
            {
                move.SpriteId = null; // move sprites are presentation too (M4b) — and
                                      // the shared seed derives from this key, so it
                                      // must not shift when move genes get assigned
            }
        }
        return JsonSerializer.Serialize(doc, ContentKeyOptions);
    }

    public static string ContentKey(StageGenome stage)
    {
        GameGenomeJson.StageDoc doc = GameGenomeJson.ToStageDoc(stage);
        doc.ThemeId = null; // theme is presentation, not identity (2026-09-01, M4d) —
                            // and the stage naming seed derives from this key, so it
                            // must not shift when selection or repair changes the look
        return JsonSerializer.Serialize(doc, ContentKeyOptions);
    }

    /// <summary>Null suppression keeps ContentKey (and therefore the naming/sprite
    /// seeds) byte-identical to the pre-spriteId era: no doc field other than the new
    /// SpriteId was ever null, so omitting nulls changes nothing for legacy content
    /// while keeping the new gene out of the bytes.</summary>
    private static readonly JsonSerializerOptions ContentKeyOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
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
/// </summary>
public static class BuiltGameJson
{
    public const int CurrentFormatVersion = 5;

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
                s.Register));
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
        public GameGenomeJson.StageDoc? Stage { get; set; }
    }
}
