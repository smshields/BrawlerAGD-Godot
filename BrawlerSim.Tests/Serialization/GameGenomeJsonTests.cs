using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Serialization;

public class GameGenomeJsonTests
{
    private static GameRecord NewRecord(ulong seed = 42) =>
        new("TestGame", "test:seed42", GameGenome.Generate(GenerationConfig.Default, new Pcg32(seed)));

    [Fact]
    public void RoundTripPreservesEverything()
    {
        GameRecord original = NewRecord();
        string json = GameGenomeJson.Serialize(original);
        GameRecord loaded = GameGenomeJson.Deserialize(json);

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Origin, loaded.Origin);
        // Bitwise param equality via a second serialization pass.
        Assert.Equal(json, GameGenomeJson.Serialize(loaded));
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void SaveAndLoadRoundTripsThroughDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"brawler-test-{Guid.NewGuid():N}", "game.json");
        try
        {
            GameRecord original = NewRecord();
            GameGenomeJson.Save(original, path);
            GameRecord loaded = GameGenomeJson.Load(path);
            Assert.Equal(GameGenomeJson.Serialize(original), GameGenomeJson.Serialize(loaded));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void UnknownParamsAreIgnoredOnLoad()
    {
        string json = GameGenomeJson.Serialize(NewRecord());
        // Simulate a file written by a future schema with an extra character param.
        json = json.Replace("\"maxGroundSpeed\":", "\"futureParam\": 1.0, \"maxGroundSpeed\":");
        GameRecord loaded = GameGenomeJson.Deserialize(json);
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void MissingParamThrows()
    {
        string json = GameGenomeJson.Serialize(NewRecord());
        json = json.Replace("\"mass\":", "\"massRenamed\":");
        Assert.Throws<KeyNotFoundException>(() => GameGenomeJson.Deserialize(json));
    }

    [Fact]
    public void UnsupportedFormatVersionThrows()
    {
        string json = GameGenomeJson.Serialize(NewRecord());
        json = json.Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 999");
        Assert.Contains("\"formatVersion\": 999", json); // guard: the replace must hit
        Assert.Throws<NotSupportedException>(() => GameGenomeJson.Deserialize(json));
    }

    [Fact]
    public void FormatVersion1FilesLoadWithDefaultButtonMoves()
    {
        // Every pre-feature file (evolved runs, Unity imports) is v1 with no buttonMoves:
        // it must load with the all-zeros mapping, i.e. every button triggers move 0.
        string json = GameGenomeJson.Serialize(NewRecord());
        json = json.Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 1");
        json = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"buttonMoves\": \\[[^\\]]*\\],", "");
        Assert.DoesNotContain("buttonMoves", json);

        GameRecord loaded = GameGenomeJson.Deserialize(json);
        Assert.All(loaded.Genome.Characters, c =>
        {
            Assert.Equal(BrawlerSim.Sim.InputFrame.ActionCount, c.ButtonMoves.Count);
            Assert.All(c.ButtonMoves, m => Assert.Equal(0, m));
        });
    }

    [Fact]
    public void ButtonMovesRoundTripThroughJson()
    {
        GameRecord original = NewRecord();
        string json = GameGenomeJson.Serialize(original);
        Assert.Contains("\"buttonMoves\"", json);
        GameRecord loaded = GameGenomeJson.Deserialize(json);
        for (int c = 0; c < original.Genome.Characters.Count; c++)
        {
            Assert.Equal(original.Genome.Characters[c].ButtonMoves, loaded.Genome.Characters[c].ButtonMoves);
        }
    }
}
