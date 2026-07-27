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
/// </summary>
public static class GameGenomeJson
{
    public const int CurrentFormatVersion = 8; // 2026-07-22 spawning behaviors (see header)
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
                        MoveType.Projectile => "projectile",
                        _ => "attack",
                    },
                    SpriteIndex = m.SpriteIndex,
                    Params = m.Params.ToDictionary(),
                }).ToList(),
            }).ToList(),
            Stage = new StageDoc
            {
                Params = record.Genome.Stage.Params.ToDictionary(),
                Platforms = record.Genome.Stage.Platforms
                    .Select(p => new PlatformDoc { X = p.X, Y = p.Y, XSize = p.XSize, YSize = p.YSize })
                    .ToList(),
            },
        };
        return JsonSerializer.Serialize(doc, Options);
    }

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

        var characters = doc.Characters.Select(c => new CharacterGenome(
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
                    m.SpriteIndex, MoveType.Shield),
                "dash" => new MoveGenome(
                    ParamSet.FromDictionary(config.DashSchema,
                        WithReflectDefault(Require(m.Params, "dash params"))),
                    m.SpriteIndex, MoveType.Dash),
                "projectile" => new MoveGenome(
                    ParamSet.FromDictionary(config.ProjectileSchema, Require(m.Params, "projectile params")),
                    m.SpriteIndex, MoveType.Projectile),
                _ => new MoveGenome(
                    ParamSet.FromDictionary(config.MoveSchema, Require(m.Params, "move params")),
                    m.SpriteIndex),
            }),
            MigrateButtonMoves(c.ButtonMoves))); // null (v1 files) → all-zeros default in the ctor

        var platforms = doc.Stage.Platforms
            .Select(p => new PlatformGene(p.X, p.Y, p.XSize, p.YSize)).ToList();
        // ≤ v6: no stage params — the legacy dimensions + old derived spawns
        // (bit-identical playback). v7+: read them (missing keys throw, as everywhere).
        var stage = new StageGenome(platforms, doc.Stage.Params is null
            ? StageRules.LegacyParams(platforms, config.StageSchema)
            : ParamSet.FromDictionary(config.StageSchema, WithStageDefaults(doc.Stage.Params)));
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
    /// so they replay as the instant vulnerable spawn they were recorded under.</summary>
    private static Dictionary<string, float> WithStageDefaults(Dictionary<string, float> dict)
    {
        dict.TryAdd(StageParams.PlatformSpawnDuration, 0f);
        dict.TryAdd(StageParams.SpawnInvulnDuration, 0f);
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
        public Dictionary<string, float>? Params { get; set; } // absent in ≤ v6 files
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
