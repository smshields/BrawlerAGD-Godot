using BrawlerSim.Fitness;
using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// The dynamic stitcher (2026-09-16): a fitness assembled at runtime from term
/// objects, summed in list order, with the current default fitness as its seed.
/// </summary>
public class FitnessComposerTests
{
    private static MatchResult Healthy => FitnessFixtures.TwoPlayer[0].Result;

    [Fact]
    public void SumsItsTermsInListOrder()
    {
        var composer = new FitnessComposer("test", new IFitnessTerm[]
        {
            new StockFairnessTerm(10f),                 // 10 − spread
            new SelfDestructsTerm(1f, 4f),              // −min(1 × ΣSD, 4)
            new DropThroughsTerm(0.25f, 1f),            // +min(0.25 × Σdrops, 1)
        });

        // stocks 3 vs 1 → 10 − 2 = 8; two SDs → −2; four drops → +1.
        var result = FitnessFixtures.Match(new[]
        {
            FitnessFixtures.Player(stocks: 3, selfDestructs: 2, drops: 4),
            FitnessFixtures.Player(stocks: 1),
        });
        Assert.Equal(7f, composer.Evaluate(result), 0.0001f);
    }

    [Fact]
    public void AnEmptyAssemblyScoresZero()
    {
        Assert.Equal(0f, new FitnessComposer("empty", Array.Empty<IFitnessTerm>()).Evaluate(Healthy));
    }

    [Fact]
    public void BreakdownRowsAreTheTermsInOrderWithTheirOwnValues()
    {
        var terms = new IFitnessTerm[]
        {
            new StockFairnessTerm(), new SelfDestructsTerm(), new DropThroughsTerm(),
        };
        var composer = new FitnessComposer("test", terms);
        var rows = composer.Breakdown(Healthy);

        Assert.Equal(terms.Length, rows.Count);
        var input = new FitnessInput(Healthy);
        for (int i = 0; i < terms.Length; i++)
        {
            Assert.Equal(terms[i].Id, rows[i].Name);
            Assert.Equal(terms[i].Evaluate(input), rows[i].Value);
        }
    }

    [Fact]
    public void BreakdownSumsToTheScoreBitExactly()
    {
        var composer = FitnessTermRegistry.SeedFromDefault();
        foreach ((string label, MatchResult result) in FitnessFixtures.TwoPlayer)
        {
            float sum = 0f;
            foreach ((_, float value) in composer.Breakdown(result))
            {
                sum += value;
            }
            Assert.True(
                BitConverter.SingleToInt32Bits(sum)
                    == BitConverter.SingleToInt32Bits(composer.Evaluate(result)),
                $"{label}: breakdown {sum} vs evaluate {composer.Evaluate(result)}");
        }
    }

    [Fact]
    public void TheDefaultSeedScoresIdenticallyToTheShippedDefaultVersion()
    {
        // The contract behind "a designer starts from what we ship": the seed is the
        // current default fitness, term for term, bit for bit — only the name differs,
        // so a custom assembly can never be mistaken for a shipped version.
        IFitnessFunction shipped = FitnessRegistry.Create(
            FitnessRegistry.DefaultName, 45f, 60f, StandardFitnessV3.DefaultCollisionScalar);
        FitnessComposer seed = FitnessTermRegistry.SeedFromDefault();

        Assert.NotEqual(shipped.Name, seed.Name);
        var cases = new List<(string, MatchResult)>(FitnessFixtures.TwoPlayer);
        cases.AddRange(FitnessFixtures.MultiPlayer);
        foreach ((string label, MatchResult result) in cases)
        {
            Assert.True(
                BitConverter.SingleToInt32Bits(shipped.Evaluate(result))
                    == BitConverter.SingleToInt32Bits(seed.Evaluate(result)),
                $"{label}: {shipped.Name} {shipped.Evaluate(result)} vs seed {seed.Evaluate(result)}");
        }
    }

    [Fact]
    public void TheDefaultSeedExposesEveryTermForEditing()
    {
        FitnessComposer seed = FitnessTermRegistry.SeedFromDefault();
        Assert.Equal(
            new[]
            {
                "time", "damage", "farmPenalty", "collisions", "damageFairness",
                "stockFairness", "moveMix", "stunLock", "jumps", "blocks",
                "selfDestructs", "dropThroughs",
            },
            seed.Terms.Select(t => t.Id));

        // And every term reports constants a UI can render and write back.
        foreach (IFitnessTerm term in seed.Terms)
        {
            Assert.NotEmpty(term.Values);
        }
    }

    [Fact]
    public void EditingATermChangesTheScore()
    {
        // End-to-end proof that the knobs are live: rebuild the default seed with a
        // tripled block reward and the shield-heavy fixture scores higher.
        MatchResult blocks = FitnessFixtures.TwoPlayer.First(c => c.Name == "blocks").Result;
        FitnessComposer seed = FitnessTermRegistry.SeedFromDefault();

        var edited = new FitnessComposer("custom", seed.Terms.Select(term =>
            term.Id == BlocksTerm.TermId
                ? FitnessTermRegistry.Create(BlocksTerm.TermId, new Dictionary<string, float>
                {
                    ["reward"] = 6f, ["opponentOnly"] = term.Values["opponentOnly"],
                })
                : term));

        // 39 blocked hits across both players: 2.0 → 6.0 per block is +156.
        Assert.Equal(seed.Evaluate(blocks) + 156f, edited.Evaluate(blocks), 0.001f);
    }
}
