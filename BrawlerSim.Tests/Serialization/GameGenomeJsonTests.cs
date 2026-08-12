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
    public void PreV7FilesLoadWithLegacyStageParams()
    {
        // 2026-07-21 Map Size: a ≤ v6 file has no stage params — it must load with the
        // legacy dimensions and the OLD derived spawns so replays stay bit-identical.
        string json = GameGenomeJson.Serialize(NewRecord());
        json = json.Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 6");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"stage\": \\{\\s*\"params\": \\{[^}]*\\},", "\"stage\": {");
        Assert.DoesNotContain("visibleHalfWidth", json);

        GameRecord loaded = GameGenomeJson.Deserialize(json);
        var expected = StageRules.LegacyParams(loaded.Genome.Stage.Platforms);
        Assert.Equal(expected.ToArray(), loaded.Genome.Stage.Params.ToArray());
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void PreV9FilesDeriveSpawns3And4()
    {
        // 2026-08-12 Four Player Support: a ≤ v8 file has no spawn3/4 genes — they
        // must derive deterministically while spawns 1/2 load exactly as stored
        // (2P matches never read 3/4, so old replays stay bit-identical).
        GameRecord original = NewRecord();
        string json = GameGenomeJson.Serialize(original);
        json = json.Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 8");
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"spawn[34][XY]\": [^,\\n}]*", "");
        Assert.DoesNotContain("spawn3X", json);

        GameRecord loaded = GameGenomeJson.Deserialize(json);
        var p = loaded.Genome.Stage.Params;
        var stored = original.Genome.Stage.Params;
        Assert.Equal(stored.Get(StageParams.Spawn1X), p.Get(StageParams.Spawn1X));
        Assert.Equal(stored.Get(StageParams.Spawn1Y), p.Get(StageParams.Spawn1Y));
        Assert.Equal(stored.Get(StageParams.Spawn2X), p.Get(StageParams.Spawn2X));
        Assert.Equal(stored.Get(StageParams.Spawn2Y), p.Get(StageParams.Spawn2Y));

        (Vec2 s3, Vec2 s4) = StageRules.DeriveExtraSpawns(
            loaded.Genome.Stage.Platforms,
            new Vec2(stored.Get(StageParams.Spawn1X), stored.Get(StageParams.Spawn1Y)),
            new Vec2(stored.Get(StageParams.Spawn2X), stored.Get(StageParams.Spawn2Y)),
            stored.Get(StageParams.VisibleHalfWidth), stored.Get(StageParams.VisibleHalfHeight));
        Assert.Equal(s3.X, p.Get(StageParams.Spawn3X));
        Assert.Equal(s3.Y, p.Get(StageParams.Spawn3Y));
        Assert.Equal(s4.X, p.Get(StageParams.Spawn4X));
        Assert.Equal(s4.Y, p.Get(StageParams.Spawn4Y));
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void FourCharacterGamesRoundTripThroughJson()
    {
        // 2026-08-12: the characters list may hold 2–4 entries (game.json v9).
        var config = GenerationConfig.Default with { CharacterCount = 4 };
        var record = new GameRecord("FourUp", "test:4p",
            GameGenome.Generate(config, new Pcg32(7)));
        Assert.Equal(4, record.Genome.Characters.Count);
        string json = GameGenomeJson.Serialize(record);
        GameRecord loaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(4, loaded.Genome.Characters.Count);
        Assert.Equal(json, GameGenomeJson.Serialize(loaded));
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void StageParamsRoundTripThroughJson()
    {
        GameRecord original = NewRecord();
        string json = GameGenomeJson.Serialize(original);
        Assert.Contains("\"visibleHalfWidth\"", json);
        GameRecord loaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(original.Genome.Stage.Params.ToArray(), loaded.Genome.Stage.Params.ToArray());
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
