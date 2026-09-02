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
            return moves is null ? presented : presented.WithMoves(moves);
        }
    }
}

/// <summary>One stage entry. ThemeId + Register (2026-09-01, M4d —
/// stage-tile-selection.md) are the NEGOTIATED presentation, settled with the stage's
/// generated name by the game-open pass and persisted once, like BuiltCharacter's
/// sprite fields; the stored genome stays untouched. BackgroundId + BackgroundRemap
/// (2026-09-02, backgrounds track) join the same pass: the settled bg-v1 entry and
/// its palette remap target (null remap = the entry's native palette).</summary>
public sealed record BuiltStage(string DisplayName, string? Origin, StageGenome Stage,
    string? ThemeId = null, string? Register = null,
    string? BackgroundId = null, string? BackgroundRemap = null)
{
    /// <summary>The stage as this game presents it: the negotiated theme + background
    /// injected over the inherited genes — views and match launches read this.</summary>
    public StageGenome Presented
    {
        get
        {
            StageGenome presented = ThemeId is null ? Stage : Stage.WithThemeId(ThemeId);
            return BackgroundId is null ? presented : presented.WithBackgroundId(BackgroundId);
        }
    }
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
        doc.BackgroundId = null; // background is presentation too (2026-09-02) —
                                 // same rule, same reason
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
