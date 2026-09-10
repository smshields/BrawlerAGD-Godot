using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sprites;
using Xunit;

namespace BrawlerSim.Tests.Sprites;

/// <summary>
/// The move-sprite no-monopoly caps (designer 2026-09-10: blade dominated fresh
/// rosters — first in 56% of wields lists and 35% of the library — while evolution
/// drifted repair re-picks to burst at 48% with one sprite at 10% of all picks).
/// Move selection now carries the same water-fill rule as character sprites, themes,
/// and backgrounds: no class above MaxClassShare per pick, no sprite above
/// MaxSpriteShare within its class; infeasible caps no-op, so one-sprite classes
/// (whip) still function.
/// </summary>
public class MoveSpriteSpreadTests
{
    private static readonly Lazy<SpriteSelector> SelectorLazy = new(() =>
    {
        string players = FindRepoFile(Path.Combine("godot", "assets", "players_v2_slices.json"));
        string dir = Path.GetDirectoryName(players)!;
        return new SpriteSelector(
            SpriteLibrary.LoadFile(players),
            SpriteSelectionConfig.LoadFile(Path.Combine(dir, "sprite_selection.json")),
            moveLibrary: MoveSpriteLibrary.LoadFile(Path.Combine(dir, "moves_v2_slices.json")));
    });

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

    [Fact]
    public void FreshGenerationSpreadsAttackSpritesAcrossClassesAndSprites()
    {
        GenerationConfig config = GenerationConfig.Default with
        {
            SpriteSelector = SelectorLazy.Value,
        };
        var byClass = new Dictionary<string, int>(StringComparer.Ordinal);
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        int picks = 0;
        MoveSpriteLibrary moves = SelectorLazy.Value.MoveLibrary!;
        for (ulong seed = 1; seed <= 600; seed++)
        {
            GameGenome game = GameGenome.Generate(config, new Pcg32(seed));
            foreach (CharacterGenome c in game.Characters)
            {
                foreach (MoveGenome m in c.Moves)
                {
                    if (m.SpriteId is not { } id || moves.ById(id) is not { } def)
                    {
                        continue;
                    }
                    picks++;
                    byClass[def.AttackClass] = byClass.GetValueOrDefault(def.AttackClass) + 1;
                    byId[id] = byId.GetValueOrDefault(id) + 1;
                }
            }
        }
        Assert.True(picks > 2000, $"only {picks} attack picks sampled");
        // Per-pick cap 0.30 bounds any class's aggregate share (small sampling slack);
        // pre-fix, blade took the majority of fresh first slots.
        (string cls, int n) = byClass.MaxBy(kv => kv.Value) is var kv2 ? (kv2.Key, kv2.Value) : ("", 0);
        Assert.True(n <= picks * 0.35, $"class monopoly: {cls} {n}/{picks}");
        // No single sprite dominates overall (class cap x within cap ≈ 7.5% ceiling).
        (string sid, int sn) = byId.MaxBy(kv => kv.Value) is var kv3 ? (kv3.Key, kv3.Value) : ("", 0);
        Assert.True(sn <= picks * 0.10, $"sprite monopoly: {sid} {sn}/{picks}");
        // Breadth: most of the library sees use, and every populated class appears.
        Assert.True(byId.Count >= 90, $"only {byId.Count} distinct sprites picked");
        foreach (string cls2 in new[]
            { "blade", "burst", "blunt", "object", "staff", "polearm", "axe", "impact", "natural", "whip" })
        {
            Assert.True(byClass.ContainsKey(cls2), $"class {cls2} never picked");
        }
    }

    [Fact]
    public void OneSpriteClassesStillFunctionUnderTheCaps()
    {
        // The whip class holds exactly one sprite: the within-class cap must no-op
        // (cap x 1 <= 1) rather than starve it — selection still returns it when the
        // class stage lands there. Prove via the water-fill directly.
        var probs = new double[] { 1.0 };
        BrawlerSim.Backgrounds.SelectionMath.CapShare(probs, 0.25f);
        Assert.Equal(1.0, probs[0], 6);
    }
}
