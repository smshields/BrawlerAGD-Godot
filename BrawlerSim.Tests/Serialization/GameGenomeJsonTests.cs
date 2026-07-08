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
        json = json.Replace("\"formatVersion\": 1", "\"formatVersion\": 999");
        Assert.Throws<NotSupportedException>(() => GameGenomeJson.Deserialize(json));
    }
}
