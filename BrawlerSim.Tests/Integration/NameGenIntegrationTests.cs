using System.Text.Json;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using Xunit;
using NG = NameGen;

namespace BrawlerSim.Tests.Integration;

/// <summary>
/// Test integration of the namegen library (designer, 2026-08-14) — proving it works
/// against REAL evolved-game data before the naming feature is built. The mapping
/// below (ParamSet.ToDictionary + MoveType→MoveKind) is exactly what the future
/// feature will lift. Test-only: no shipping project references NameGen yet.
/// </summary>
public class NameGenIntegrationTests
{
    // ── The genome mapping the naming feature will use ─────────────────────────

    private static NG.CharacterGenome Map(CharacterGenome character) => new(
        character.Params.ToDictionary(),
        character.Moves.Select(m => new NG.MoveGenome(Map(m.Type), m.Params.ToDictionary())).ToList());

    private static NG.MoveKind Map(MoveType type) => type switch
    {
        MoveType.Shield => NG.MoveKind.Shield,
        MoveType.Dash => NG.MoveKind.Dash,
        MoveType.Projectile => NG.MoveKind.Projectile,
        _ => NG.MoveKind.Melee,
    };

    private static NG.StageGenome Map(StageGenome stage) => new(stage.Params.ToDictionary());

    private static GameGenome Game(ulong seed, int players = 4) =>
        GameGenome.Generate(
            GenerationConfig.Default with { CharacterCount = players }, new Pcg32(seed));

    // ── Behavior against real genomes ──────────────────────────────────────────

    [Fact]
    public void EmbeddedDataLoadsAndNamesRealGenomes()
    {
        var generator = NG.NameGenerator.CreateDefault(); // loads + validates embedded data
        GameGenome game = Game(11);

        foreach (CharacterGenome character in game.Characters)
        {
            NG.NameResult name = generator.GenerateCharacterName(
                Map(character), new NG.NameOptions { Seed = 7 });
            Assert.False(string.IsNullOrWhiteSpace(name.Display));
            Assert.False(string.IsNullOrWhiteSpace(name.Register));
            Assert.NotEmpty(name.Features); // feature extraction saw our params
        }
        NG.NameResult stage = generator.GenerateStageName(
            Map(game.Stage), new NG.NameOptions { Seed = 7 });
        Assert.False(string.IsNullOrWhiteSpace(stage.Display));
    }

    [Fact]
    public void SeededNamingIsReproducibleAndSeedsMatter()
    {
        var generator = NG.NameGenerator.CreateDefault();
        NG.CharacterGenome genome = Map(Game(11).Characters[0]);

        string a = generator.GenerateCharacterName(genome, new NG.NameOptions { Seed = 42 }).Display;
        string b = generator.GenerateCharacterName(genome, new NG.NameOptions { Seed = 42 }).Display;
        Assert.Equal(a, b); // same seed, same name — safe for (genome, seed) reproducibility

        var names = new HashSet<string>();
        for (ulong seed = 1; seed <= 5; seed++)
        {
            names.Add(generator.GenerateCharacterName(genome, new NG.NameOptions { Seed = seed }).Display);
        }
        Assert.True(names.Count >= 2, "five seeds produced one single name");
    }

    [Fact]
    public void ARosterSessionNamesABuiltGameUniquely()
    {
        // The intended use: naming a full built game — 8 characters + 4 stages.
        var session = new NG.UniqueNameSession(NG.NameGenerator.CreateDefault());
        var names = new List<string>();
        for (ulong seed = 0; seed < 2; seed++)
        {
            GameGenome game = Game(20 + seed);
            names.AddRange(game.Characters.Select((c, i) =>
                session.GenerateCharacterName(Map(c), new NG.NameOptions { Seed = 100 + (ulong)i }).Display));
        }
        for (ulong seed = 0; seed < 4; seed++)
        {
            names.Add(session.GenerateStageName(
                Map(Game(30 + seed).Stage), new NG.NameOptions { Seed = 200 + seed }).Display);
        }
        Assert.Equal(12, names.Count); // 8 characters + 4 stages
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SweepsManyEvolvedGenomesWithoutFailures()
    {
        // Robustness in context: 50 generated games (200 characters, 50 stages, the
        // full move-type pool) must name without exceptions, empties, or absurd lengths.
        var generator = NG.NameGenerator.CreateDefault();
        for (ulong seed = 1; seed <= 50; seed++)
        {
            GameGenome game = Game(seed);
            foreach (CharacterGenome character in game.Characters)
            {
                string display = generator.GenerateCharacterName(
                    Map(character), new NG.NameOptions { Seed = seed }).Display;
                Assert.InRange(display.Trim().Length, 2, 48);
            }
            string stageName = generator.GenerateStageName(
                Map(game.Stage), new NG.NameOptions { Seed = seed }).Display;
            Assert.InRange(stageName.Trim().Length, 2, 48);
        }
    }

    // ── The real integration risk: namegen's copy of the schema ranges ─────────

    /// <summary>
    /// namegen normalizes params against its own transcription of DefaultSchemas
    /// (Data/schema-ranges.json). Its contract tolerates MISSING keys (schema appends
    /// read as neutral), but every range it DOES declare must match the live
    /// generation range, and every declared key must still exist — this is the drift
    /// guard that caught the visibleHalfHeight transcription bug (5.5 blast half
    /// height instead of the 5.0 visible half height) on integration day.
    /// </summary>
    [Fact]
    public void DeclaredSchemaRangesMatchTheLiveSchema()
    {
        string path = FindSchemaRangesJson();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var sections = new (string Section, ParamSchema Schema)[]
        {
            ("character", DefaultSchemas.Character),
            ("move", DefaultSchemas.Move),
            ("shield", DefaultSchemas.Shield),
            ("dash", DefaultSchemas.Dash),
            ("projectile", DefaultSchemas.Projectile),
            ("stage", DefaultSchemas.Stage),
        };
        foreach ((string section, ParamSchema schema) in sections)
        {
            Assert.True(doc.RootElement.TryGetProperty(section, out JsonElement element),
                $"schema-ranges.json is missing the '{section}' section");
            foreach (JsonProperty declared in element.EnumerateObject())
            {
                int index = schema.IndexOf(declared.Name);
                Assert.True(index >= 0,
                    $"{section}.{declared.Name} is declared by namegen but no longer in the live schema");
                ParamSpec spec = schema[index];
                float min = declared.Value.GetProperty("min").GetSingle();
                float max = declared.Value.GetProperty("max").GetSingle();
                Assert.True(Close(min, spec.Min) && Close(max, spec.Max),
                    $"{section}.{declared.Name}: namegen declares [{min}, {max}] "
                    + $"but the live schema generates [{spec.Min}, {spec.Max}]");
            }
        }
    }

    private static bool Close(float a, float b) =>
        MathF.Abs(a - b) <= 0.001f * MathF.Max(1f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));

    private static string FindSchemaRangesJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "namegen", "src", "NameGen", "Data", "schema-ranges.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("could not locate namegen/src/NameGen/Data/schema-ranges.json above the test directory");
    }
}
