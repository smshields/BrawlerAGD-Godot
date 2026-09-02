using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Serialization;

/// <summary>
/// Reads and writes the single-file game.json format (current version:
/// CurrentFormatVersion below — the history list is the authority). Params are
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
///   5 — 2026-07-14 projectiles: "projectile" joins the type values (projectile schema).
///   6 — 2026-07-20 five buttons: buttonMoves grew 4 → 5 (single jump button freed
///       pad Y). ≤5 files migrate: old[0..2] stay, NEW index 3 duplicates button 0's
///       move (the new physical button, never pressed by old traces), old[3] → 4 (the
///       R1/L dash-pin button keeps its move at the new last index).
///   7 — 2026-07-21 map size: stage gained "params" (the stage schema — map dimensions,
///       KO margin, symmetry, spawn genes; docs/features/map-size.md). ≤6 files load
///       with StageRules.LegacyParams: the pre-feature dimensions plus spawns derived
///       by the old ComputeSpawn rule, so old games and traces replay bit-identically.
///   8 — 2026-07-22 spawning behaviors: stage gained platformSpawnDuration +
///       spawnInvulnDuration (docs/features/spawn-and-polish.md). ≤7 files default both
///       to 0 (spawning feature OFF = instant vulnerable spawn), so they replay
///       bit-identically.
///   9 — 2026-08-12 four-player support: stage gained spawn3X/Y + spawn4X/Y (every
///       stage carries four spawn points; docs/features/four-player.md), and the
///       characters list may hold 2–4 entries. ≤8 files derive spawns 3/4
///       deterministically (StageRules.DeriveExtraSpawns); 2P matches never read
///       them, so old games and traces replay bit-identically.
///  10 — 2026-08-22 sprite selection: characters gained "spriteId" (the semantic
///       sprite gene into players_v2 — docs/features/sprite-selection.md), omitted
///       when null. ≤9 files load with spriteId = null (per designer, no v1→v2 look
///       mapping: old games are archived; a null gene renders from the legacy v1
///       sheet and is resolved fresh if it enters a sprite-enabled pipeline).
///       spriteIndex is retained for round-trip. Purely cosmetic — replays and
///       goldens are untouched.
///  11 — 2026-08-23 melee attack sprites (M4b, attack-sprite-selection.md): moves
///       gained "spriteId" (moves_v2 library; assigned on Attack moves only),
///       omitted when null. ≤10 files load with null move genes — same legacy
///       stance as v10. Purely cosmetic.
///  12 — 2026-09-01 thin platforms (FEATURES.md §Thin Platforms;
///       docs/features/thin-platforms.md): platforms gained "thin" (drop-through —
///       a structural gene), the stage gained thinPlatformFraction, and characters
///       gained dropThroughDelay. ≤11 files load all-solid with both new params 0,
///       so old games and traces replay bit-identically (every thin path is gated
///       on a thin platform existing).
///  13 — 2026-09-01 stage tile themes (M4d, stage-tile-selection.md): the stage
///       gained "themeId" (the tiles_v2 theme gene), omitted when null. ≤12 files
///       load with themeId = null — the M4 legacy stance: null renders the v1
///       Kenney tiles and is resolved fresh only in a theme-enabled pipeline.
///       Purely cosmetic — replays and match goldens untouched.
///  14 — 2026-09-02 backgrounds (docs/background-implementation-brief.md): the stage
///       gained "backgroundId" (the bg-v1 background gene), omitted when null. ≤13
///       files load with backgroundId = null — the M4/M4d legacy stance: null
///       renders the blank pre-feature backdrop and is resolved fresh only in a
///       background-enabled pipeline. Purely cosmetic — replays and match goldens
///       untouched.
/// </summary>
public static class GameGenomeJson
{
    public const int CurrentFormatVersion = 14; // 2026-09-02 backgrounds (see header)
    private const int MinSupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = JsonOptions.Document;

    public static string Serialize(GameRecord record)
    {
        var doc = new GameDoc
        {
            FormatVersion = CurrentFormatVersion,
            Name = record.Name,
            Origin = record.Origin,
            Characters = record.Genome.Characters.Select(ToCharacterDoc).ToList(),
            Stage = ToStageDoc(record.Genome.Stage),
        };
        return JsonSerializer.Serialize(doc, Options);
    }

    /// <summary>Element-level DTO conversions, shared with BuiltGameJson (2026-08-13,
    /// Game Builder) so a built game's characters/stages are byte-compatible with the
    /// game.json shapes.</summary>
    internal static CharacterDoc ToCharacterDoc(CharacterGenome c) => new()
    {
        Name = c.Name,
        Stocks = c.Stocks,
        SpriteIndex = c.SpriteIndex,
        SpriteId = c.SpriteId,
        Params = c.Params.ToDictionary(),
        ButtonMoves = c.ButtonMoves.ToList(),
        Moves = c.Moves.Select(m => new MoveDoc
        {
            Type = m.Type switch
            {
                MoveType.Shield => "shield",
                MoveType.Dash => "dash",
                MoveType.Projectile => "projectile",
                _ => "attack",
            },
            SpriteIndex = m.SpriteIndex,
            SpriteId = m.SpriteId,
            Params = m.Params.ToDictionary(),
        }).ToList(),
    };

    internal static StageDoc ToStageDoc(StageGenome stage) => new()
    {
        ThemeId = stage.ThemeId,
        BackgroundId = stage.BackgroundId,
        Params = stage.Params.ToDictionary(),
        Platforms = stage.Platforms
            // Thin is written only when TRUE (null suppression): solid platforms
            // keep their pre-v12 bytes, which keeps BuiltGameJson.ContentKey — and
            // therefore the naming/sprite seeds of existing content — byte-identical.
            .Select(p => new PlatformDoc
            {
                X = p.X, Y = p.Y, XSize = p.XSize, YSize = p.YSize,
                Thin = p.Thin ? true : null,
            })
            .ToList(),
    };

    /// <summary>Four-button-era (v2–v5) buttonMoves → five slots: new index 3 (pad Y,
    /// previously a jump button — no legacy trace ever presses it) duplicates button
    /// 0's move; old index 3 (R1/L) keeps its move at the new LAST index, preserving
    /// the dash pin's physical home. Mirrored by InputTraceJson's 7-value upgrade.</summary>
    private static List<int>? MigrateButtonMoves(List<int>? buttonMoves)
    {
        if (buttonMoves is null || buttonMoves.Count != 4)
        {
            return buttonMoves;
        }
        return new List<int> { buttonMoves[0], buttonMoves[1], buttonMoves[2], buttonMoves[0], buttonMoves[3] };
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

        var characters = doc.Characters.Select(c => CharacterFromDoc(c, config));
        var stage = StageFromDoc(doc.Stage, config);
        return new GameRecord(doc.Name ?? "Unnamed", doc.Origin, new GameGenome(characters, stage));
    }

    internal static CharacterGenome CharacterFromDoc(CharacterDoc c, GenerationConfig config) =>
        new(
            c.Name ?? "Unnamed",
            c.Stocks,
            c.SpriteIndex,
            ParamSet.FromDictionary(config.CharacterSchema,
                WithCharacterDefaults(Require(c.Params, "character params"))),
            (c.Moves ?? new List<MoveDoc>()).Select(m => m.Type switch
            {
                "shield" => new MoveGenome(
                    ParamSet.FromDictionary(config.ShieldSchema,
                        WithReflectDefault(Require(m.Params, "shield params"))),
                    m.SpriteIndex, MoveType.Shield, m.SpriteId),
                "dash" => new MoveGenome(
                    ParamSet.FromDictionary(config.DashSchema,
                        WithReflectDefault(Require(m.Params, "dash params"))),
                    m.SpriteIndex, MoveType.Dash, m.SpriteId),
                "projectile" => new MoveGenome(
                    ParamSet.FromDictionary(config.ProjectileSchema, Require(m.Params, "projectile params")),
                    m.SpriteIndex, MoveType.Projectile, m.SpriteId),
                _ => new MoveGenome(
                    ParamSet.FromDictionary(config.MoveSchema, Require(m.Params, "move params")),
                    m.SpriteIndex, MoveType.Attack, m.SpriteId), // absent (≤v10) → null
            }),
            MigrateButtonMoves(c.ButtonMoves), // null (v1 files) → all-zeros default in the ctor
            c.SpriteId); // absent (≤v9 files) → null: legacy v1-sheet rendering

    internal static StageGenome StageFromDoc(StageDoc doc, GenerationConfig config)
    {
        var platforms = (doc.Platforms ?? throw new JsonException("stage is missing platforms."))
            .Select(p => new PlatformGene(p.X, p.Y, p.XSize, p.YSize, p.Thin == true)).ToList(); // absent (≤v11) → solid
        // ≤ v6: no stage params — the legacy dimensions + old derived spawns
        // (bit-identical playback). v7+: read them (missing keys throw, as everywhere).
        return new StageGenome(platforms, doc.Params is null
            ? StageRules.LegacyParams(platforms, config.StageSchema)
            : ParamSet.FromDictionary(config.StageSchema, WithStageDefaults(doc.Params, platforms)),
            doc.ThemeId,        // absent (≤v12) → null: legacy v1-tile rendering
            doc.BackgroundId);  // absent (≤v13) → null: legacy blank backdrop
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
        // 2026-09-01 thin platforms: pre-v12 characters drop instantly after the
        // crouch sink (0 s delay) — only observable on stages that have thin platforms.
        (CharacterParams.DropThroughDelay, 0f),
    };

    internal static Dictionary<string, float> WithCharacterDefaults(Dictionary<string, float> dict)
    {
        foreach ((string key, float value) in CharacterParamDefaults)
        {
            dict.TryAdd(key, value);
        }
        return dict;
    }

    /// <summary>2026-07-20 shield/dash schema append: pre-reflect files read as
    /// reflect OFF (0) — same neutral-default pattern as the character appends.
    /// ShieldParams.Reflect and DashParams.Reflect share the key string.</summary>
    private static Dictionary<string, float> WithReflectDefault(Dictionary<string, float> dict)
    {
        dict.TryAdd(ShieldParams.Reflect, 0f);
        return dict;
    }

    /// <summary>2026-07-22 spawning-behaviors stage append: ≤ v7 files (stage params
    /// present but predating the spawn genes) read both durations as 0 = feature OFF,
    /// so they replay as the instant vulnerable spawn they were recorded under.
    /// 2026-08-12 four-player append: ≤ v8 files derive spawns 3/4 deterministically
    /// from the layout + stored spawns 1/2 (never read by 2P matches — bit-exact).</summary>
    private static Dictionary<string, float> WithStageDefaults(
        Dictionary<string, float> dict, IReadOnlyList<PlatformGene> platforms)
    {
        dict.TryAdd(StageParams.PlatformSpawnDuration, 0f);
        dict.TryAdd(StageParams.SpawnInvulnDuration, 0f);
        dict.TryAdd(StageParams.ThinPlatformFraction, 0f); // ≤ v11: all-solid (2026-09-01)
        if (!dict.ContainsKey(StageParams.Spawn3X))
        {
            (Vec2 s3, Vec2 s4) = StageRules.DeriveExtraSpawns(
                platforms,
                new Vec2(dict[StageParams.Spawn1X], dict[StageParams.Spawn1Y]),
                new Vec2(dict[StageParams.Spawn2X], dict[StageParams.Spawn2Y]),
                dict[StageParams.VisibleHalfWidth], dict[StageParams.VisibleHalfHeight]);
            dict[StageParams.Spawn3X] = s3.X;
            dict[StageParams.Spawn3Y] = s3.Y;
            dict[StageParams.Spawn4X] = s4.X;
            dict[StageParams.Spawn4Y] = s4.Y;
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

    internal sealed class CharacterDoc
    {
        public string? Name { get; set; }
        public int Stocks { get; set; }
        public int SpriteIndex { get; set; }
        public string? SpriteId { get; set; } // v10+; omitted when null
        public Dictionary<string, float>? Params { get; set; }
        public List<int>? ButtonMoves { get; set; }
        public List<MoveDoc>? Moves { get; set; }
    }

    internal sealed class MoveDoc
    {
        public string? Type { get; set; } // null/absent (v1/v2) → attack
        public int SpriteIndex { get; set; }
        public string? SpriteId { get; set; } // v11+; omitted when null
        public Dictionary<string, float>? Params { get; set; }
    }

    internal sealed class StageDoc
    {
        public string? ThemeId { get; set; } // v13+; omitted when null
        public string? BackgroundId { get; set; } // v14+; omitted when null
        public Dictionary<string, float>? Params { get; set; } // absent in ≤ v6 files
        public List<PlatformDoc>? Platforms { get; set; }
    }

    internal sealed class PlatformDoc
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int XSize { get; set; }
        public int YSize { get; set; }
        public bool? Thin { get; set; } // v12+; written only when true, absent → solid
    }
}
