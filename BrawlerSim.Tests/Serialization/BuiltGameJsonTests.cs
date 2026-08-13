using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Serialization;

/// <summary>
/// Game Builder document (2026-08-13, FEATURES.md §Game Menu / Game Builder):
/// a self-contained compiled game — exactly 8 characters + 4 stages = COMPLETE,
/// strict slots, content-identity duplicate rejection.
/// </summary>
public class BuiltGameJsonTests
{
    private static GameGenome Source(ulong seed, int players = 2) =>
        GameGenome.Generate(
            GenerationConfig.Default with { CharacterCount = players }, new Pcg32(seed));

    private static BuiltCharacter Char(GameGenome source, int index, string name) =>
        new(name, $"test:seed/char{index}", source.Characters[index]);

    private static BuiltStage Stage(GameGenome source, string name) =>
        new(name, "test:seed/stage", source.Stage);

    private static BuiltGame CompleteGame()
    {
        var game = new BuiltGame { Name = "TEST ROSTER" };
        for (int s = 0; s < 4; s++)
        {
            GameGenome source = Source((ulong)(10 + s));
            Assert.True(game.TryAddCharacter(Char(source, 0, $"FIGHTER {2 * s + 1}"), out _));
            Assert.True(game.TryAddCharacter(Char(source, 1, $"FIGHTER {2 * s + 2}"), out _));
            Assert.True(game.TryAddStage(Stage(source, $"STAGE {s + 1}"), out _));
        }
        return game;
    }

    [Fact]
    public void CompleteAtExactlyEightCharactersAndFourStages()
    {
        var game = new BuiltGame();
        Assert.False(game.IsComplete);
        game = CompleteGame();
        Assert.True(game.IsComplete);
        Assert.Equal(8, game.Characters.Count);
        Assert.Equal(4, game.Stages.Count);
    }

    [Fact]
    public void SlotsAreStrict()
    {
        BuiltGame game = CompleteGame();
        GameGenome extra = Source(99);
        Assert.False(game.TryAddCharacter(Char(extra, 0, "NINTH"), out string reason));
        Assert.Contains("full", reason);
        Assert.False(game.TryAddStage(Stage(extra, "FIFTH"), out reason));
        Assert.Contains("full", reason);
    }

    [Fact]
    public void DuplicatesAreRejectedByContentNotByNameOrOrigin()
    {
        var game = new BuiltGame();
        GameGenome source = Source(5);
        Assert.True(game.TryAddCharacter(Char(source, 0, "ALPHA"), out _));
        // Same genome content, different display name and origin: still a duplicate.
        Assert.False(game.TryAddCharacter(
            new BuiltCharacter("BETA", "elsewhere", source.Characters[0]), out string reason));
        Assert.Contains("already", reason);
        // The sibling character IS different content.
        Assert.True(game.TryAddCharacter(Char(source, 1, "GAMMA"), out _));

        Assert.True(game.TryAddStage(Stage(source, "ARENA"), out _));
        Assert.False(game.TryAddStage(new BuiltStage("OTHER", null, source.Stage), out reason));
        Assert.Contains("already", reason);
    }

    [Fact]
    public void RoundTripPreservesEverything()
    {
        BuiltGame original = CompleteGame();
        original.Name = "ROUND TRIP";
        string json = BuiltGameJson.Serialize(original);
        BuiltGame loaded = BuiltGameJson.Deserialize(json);

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(
            original.Characters.Select(c => (c.DisplayName, c.Origin)),
            loaded.Characters.Select(c => (c.DisplayName, c.Origin)));
        Assert.Equal(
            original.Stages.Select(s => (s.DisplayName, s.Origin)),
            loaded.Stages.Select(s => (s.DisplayName, s.Origin)));
        // Bitwise element equality via a second serialization pass.
        Assert.Equal(json, BuiltGameJson.Serialize(loaded));
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void SaveAndLoadRoundTripsThroughDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"brawler-built-{Guid.NewGuid():N}", "game.json");
        try
        {
            BuiltGame original = CompleteGame();
            BuiltGameJson.Save(original, path);
            Assert.Equal(BuiltGameJson.Serialize(original), BuiltGameJson.Serialize(BuiltGameJson.Load(path)));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void UnsupportedFormatVersionThrows()
    {
        string json = BuiltGameJson.Serialize(CompleteGame())
            .Replace($"\"formatVersion\": {BuiltGameJson.CurrentFormatVersion}", "\"formatVersion\": 99");
        Assert.Throws<NotSupportedException>(() => BuiltGameJson.Deserialize(json));
    }

    /// <summary>The whole point of compiling: a loaded built game must feed the
    /// EXISTING sim path — pick fighters + a stage, assemble a GameGenome, and a full
    /// AI match runs deterministically (self-contained, no source files needed).</summary>
    [Fact]
    public void LoadedElementsAssembleIntoAPlayableMatch()
    {
        BuiltGame loaded = BuiltGameJson.Deserialize(BuiltGameJson.Serialize(CompleteGame()));
        var genome = new GameGenome(
            new[] { loaded.Characters[0].Character, loaded.Characters[5].Character },
            loaded.Stages[2].Stage);
        Assert.Empty(genome.Validate());

        var sources = new IInputSource[]
        {
            BrawlerSim.Agents.AgentConfig.Default.CreateSource(new Pcg32(7, 0)),
            BrawlerSim.Agents.AgentConfig.Default.CreateSource(new Pcg32(7, 1)),
        };
        var config = MatchConfig.Default with { MaxMatchSeconds = 30f };
        MatchResult a = MatchRunner.Run(genome, sources, config);
        sources = new IInputSource[]
        {
            BrawlerSim.Agents.AgentConfig.Default.CreateSource(new Pcg32(7, 0)),
            BrawlerSim.Agents.AgentConfig.Default.CreateSource(new Pcg32(7, 1)),
        };
        MatchResult b = MatchRunner.Run(genome, sources, config);
        Assert.Equal(a.FinalHash, b.FinalHash); // deterministic like any other match
    }
}
