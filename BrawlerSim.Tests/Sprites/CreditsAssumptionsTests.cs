using System.Text.Json;
using Xunit;

namespace BrawlerSim.Tests.Sprites;

/// <summary>
/// The credits screens state STATIC facts about the sprite/tile libraries
/// (CreditsUi.CourtesyBlock, corrected 2026-09-10: character, attack, AND stage-tile
/// sprites are DCSS-derived CC0; Kenney covers only the legacy v1 sheets). Backgrounds
/// are data-driven and separately tested; these three libraries are homogeneous, so
/// the text is static — this test pins the homogeneity so the words cannot silently
/// drift when a library is rebuilt. A failure here means: update the credits text AND
/// this test together.
/// </summary>
public class CreditsAssumptionsTests
{
    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {relative} above the test directory");
    }

    [Theory]
    [InlineData("players_v2_slices.json", "sprites")]
    [InlineData("moves_v2_slices.json", "sprites")]
    [InlineData("tiles_v2_slices.json", "themes")]
    public void EveryV2LibraryEntryIsCc0AndDcssDerived(string file, string listKey)
    {
        using JsonDocument doc = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile(Path.Combine("godot", "assets", file))));
        int count = 0;
        foreach (JsonElement entry in doc.RootElement.GetProperty(listKey).EnumerateArray())
        {
            count++;
            string id = entry.TryGetProperty("id", out JsonElement idEl)
                ? idEl.GetString() ?? "?"
                : entry.GetProperty("name").GetString() ?? "?";
            Assert.True(
                entry.TryGetProperty("license", out JsonElement lic)
                    && lic.GetString() == "CC0",
                $"{file}: {id} is not CC0 — the credits text must change with it");
            if (entry.TryGetProperty("source", out JsonElement src))
            {
                string source = src.GetString() ?? "";
                Assert.True(source.StartsWith("dcss", StringComparison.Ordinal)
                    || source.StartsWith("dngn/", StringComparison.Ordinal),
                    $"{file}: {id} source '{source}' is not DCSS-derived — update the credits");
            }
        }
        Assert.True(count > 50, $"{file}: only {count} entries parsed");
    }
}
