using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Serialization;

/// <summary>
/// Import fidelity against the actual AIIDE '22 study games (fixtures copied verbatim
/// from the Unity repo, Assets/Research/Game). If these fail, published research
/// artifacts no longer load correctly.
/// </summary>
public class LegacyImporterTests
{
    private static string GameDir(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", name);

    public static TheoryData<string> AllStudyGames => new() { "GameA", "GameB", "GameC", "GameD", "GameE", "GameF" };

    [Theory]
    [MemberData(nameof(AllStudyGames))]
    public void EveryStudyGameImportsAndValidates(string game)
    {
        GameRecord record = LegacyImporter.ImportGameFolder(GameDir(game));

        Assert.Equal(game, record.Name);
        Assert.Equal($"unity-import:{game}", record.Origin);
        Assert.Equal(2, record.Genome.Characters.Count);
        Assert.All(record.Genome.Characters, c => Assert.Single(c.Moves));
        Assert.True(record.Genome.Stage.Platforms.Count >= 2);
        // Every value the study shipped with must sit inside the code's design space.
        Assert.Empty(record.Genome.Validate());
    }

    [Fact]
    public void GameCValuesMatchTheSourceFilesExactly()
    {
        GameRecord record = LegacyImporter.ImportGameFolder(GameDir("GameC"));
        CharacterGenome p1 = record.Genome.Characters[0];

        Assert.Equal("Player 1", p1.Name);
        Assert.Equal(3, p1.Stocks);
        Assert.Equal(83, p1.SpriteIndex);
        Assert.Equal(5.850123405456543f, p1.Params.Get(CharacterParams.MaxGroundSpeed));
        Assert.Equal(12.086400985717773f, p1.Params.Get(CharacterParams.GroundJumpForce));
        Assert.Equal(2.2961859703063965f, p1.Params.Get(CharacterParams.Mass));
        Assert.Equal(1.1991796493530273f, p1.Params.Get(CharacterParams.GravityScalar));
        Assert.Equal(0.22958076000213623f, p1.Params.Get(CharacterParams.HitstunDamageScalar));

        MoveGenome move = p1.Moves[0];
        Assert.Equal(255, move.SpriteIndex);
        Assert.Equal(4.808187484741211f, move.Params.Get(MoveParams.MoveAngle));
        Assert.Equal(0.4819362163543701f, move.Params.Get(MoveParams.CoolDownDuration));
        Assert.Equal(0.38225558400154114f, move.Params.Get(MoveParams.KnockbackModX));

        Assert.Equal(8, record.Genome.Stage.Platforms.Count);
        Assert.Equal(new PlatformGene(-6, -3, 5, 1), record.Genome.Stage.Platforms[0]);
    }

    [Fact]
    public void DerivedDamageMatchesTheValueUnityStored()
    {
        // The legacy files carry Unity's derived damageGiven; our MoveRules must
        // reproduce it from the raw params. Direct parity check against real data.
        GameRecord record = LegacyImporter.ImportGameFolder(GameDir("GameC"));
        float derived = MoveRules.DamageGiven(record.Genome.Characters[0].Moves[0].Params);
        Assert.Equal(9.00617504119873f, derived, 0.001f);
    }

    [Theory]
    [MemberData(nameof(AllStudyGames))]
    public void ImportedGamesRoundTripThroughGameJson(string game)
    {
        GameRecord imported = LegacyImporter.ImportGameFolder(GameDir(game));
        string json = GameGenomeJson.Serialize(imported);
        GameRecord reloaded = GameGenomeJson.Deserialize(json);
        Assert.Equal(json, GameGenomeJson.Serialize(reloaded));
    }

    [Fact]
    public void MissingFileFailsLoudly()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"brawler-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Throws<FileNotFoundException>(() => LegacyImporter.ImportGameFolder(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
