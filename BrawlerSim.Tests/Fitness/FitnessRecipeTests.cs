using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Fitness.Terms;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// A fitness as data (2026-09-16, phase 3): capture, validate, serialize, and rebuild
/// — with the float exactness that makes a recorded instrument reproducible.
/// </summary>
public class FitnessRecipeTests
{
    /// <summary>Every version built from terms. standard-v2 is excluded on purpose —
    /// it predates term composition and cannot be captured.</summary>
    public static TheoryData<string> ComposedVersions() => new()
    {
        "standard-v3", "standard-v4", "standard-v5", "standard-v6", "standard-v7",
        "ffa-v1", "ffa-v2", "ffa-v3",
    };

    private static IReadOnlyList<(string Name, MatchResult Result)> AllCases()
    {
        var cases = new List<(string, MatchResult)>(FitnessFixtures.TwoPlayer);
        cases.AddRange(FitnessFixtures.MultiPlayer);
        return cases;
    }

    [Theory]
    [MemberData(nameof(ComposedVersions))]
    public void AShippedVersionCapturedAsARecipeScoresIdenticallyBitForBit(string version)
    {
        // THE equivalence the whole recipe layer rests on: the data form of a fitness
        // is the same instrument as the code form, to the last bit, over the whole
        // battery — including the 3/4-player cases for the ffa names.
        IFitnessFunction shipped = FitnessRegistry.Create(version, 45f, 60f);
        IFitnessFunction rebuilt = FitnessRecipe
            .FromShippedVersion(version, $"custom/{version}", 45f, 60f)
            .ToFitness();

        foreach ((string label, MatchResult result) in AllCases())
        {
            if (!version.StartsWith("ffa", StringComparison.Ordinal) && result.Players.Count != 2)
            {
                continue; // 2P versions are registry-guarded against N-player matches.
            }
            Assert.True(
                BitConverter.SingleToInt32Bits(shipped.Evaluate(result))
                    == BitConverter.SingleToInt32Bits(rebuilt.Evaluate(result)),
                $"{version}/{label}: {shipped.Evaluate(result)} vs recipe {rebuilt.Evaluate(result)}");
        }
    }

    [Theory]
    [MemberData(nameof(ComposedVersions))]
    public void ARecipeSurvivesAJsonRoundTripBitForBit(string version)
    {
        // The property phase 3 depends on: a recipe written to disk and read back must
        // be the SAME instrument. If a constant loses its last bit in JSON, a resumed
        // run scores differently from the run it is continuing.
        FitnessRecipe original = FitnessRecipe.FromShippedVersion(version, $"custom/{version}", 45f, 60f);
        FitnessRecipe reloaded = FitnessRecipeJson.Deserialize(FitnessRecipeJson.Serialize(original));

        Assert.Equal(original.ContentHash, reloaded.ContentHash);
        IFitnessFunction a = original.ToFitness(), b = reloaded.ToFitness();
        foreach ((string label, MatchResult result) in AllCases())
        {
            if (!version.StartsWith("ffa", StringComparison.Ordinal) && result.Players.Count != 2)
            {
                continue;
            }
            Assert.True(
                BitConverter.SingleToInt32Bits(a.Evaluate(result))
                    == BitConverter.SingleToInt32Bits(b.Evaluate(result)),
                $"{version}/{label} did not survive the round trip");
        }
    }

    [Fact]
    public void ArbitraryFloatConstantsSurviveTheJsonRoundTripExactly()
    {
        // Not trusting "System.Text.Json round-trips Single" — pinning it, over
        // pseudorandom bit patterns plus the awkward literals by hand.
        var rng = new Pcg32(20260916);
        var values = new List<float>
        {
            0f, -0f, 1f, -1f, 0.1f, 0.15f, 0.25f, 1f / 3f, 45f, 600f,
            float.Epsilon, float.MaxValue, float.MinValue, 1e-30f, 1e30f,
        };
        for (int i = 0; i < 500; i++)
        {
            float candidate = BitConverter.Int32BitsToSingle((int)rng.NextUInt());
            if (float.IsFinite(candidate))
            {
                values.Add(candidate);
            }
        }

        foreach (float value in values)
        {
            var recipe = new FitnessRecipe("custom/round-trip", new[]
            {
                new FitnessRecipe.TermEntry(
                    StockFairnessTerm.TermId, new Dictionary<string, float> { ["baseline"] = value }),
            });
            FitnessRecipe reloaded = FitnessRecipeJson.Deserialize(FitnessRecipeJson.Serialize(recipe));

            Assert.True(
                BitConverter.SingleToInt32Bits(value)
                    == BitConverter.SingleToInt32Bits(reloaded.Terms[0].Params["baseline"]),
                $"{value} did not round-trip exactly");
        }
    }

    [Fact]
    public void ContentHashTracksTheValuesNotTheFormatting()
    {
        FitnessRecipe recipe = FitnessRecipe.FromDefault("custom/a");
        Assert.Equal(recipe.ContentHash, FitnessRecipe.FromDefault("custom/a").ContentHash);

        // Key order is not content: a re-ordered parameter map hashes the same.
        var shuffled = new FitnessRecipe(recipe.Name, recipe.Terms
            .Select(t => new FitnessRecipe.TermEntry(
                t.Id, t.Params.Reverse().ToDictionary(p => p.Key, p => p.Value)))
            .ToArray());
        Assert.Equal(recipe.ContentHash, shuffled.ContentHash);

        // A different NAME, a different TERM ORDER, or one changed constant all are.
        Assert.NotEqual(recipe.ContentHash, FitnessRecipe.FromDefault("custom/b").ContentHash);
        Assert.NotEqual(recipe.ContentHash,
            new FitnessRecipe(recipe.Name, recipe.Terms.Reverse().ToArray()).ContentHash);
        Assert.NotEqual(recipe.ContentHash, Edited(recipe, BlocksTerm.TermId, "reward", 2.0000002f).ContentHash);
    }

    [Fact]
    public void TheDefaultSeedIsTheCurrentDefaultVersionUnderANewName()
    {
        FitnessRecipe seed = FitnessRecipe.FromDefault();
        IFitnessFunction shipped = FitnessRegistry.Create(FitnessRegistry.DefaultName, 45f, 60f);

        Assert.NotEqual(FitnessRegistry.DefaultName, seed.Name);
        foreach ((string label, MatchResult result) in FitnessFixtures.TwoPlayer)
        {
            Assert.True(
                BitConverter.SingleToInt32Bits(shipped.Evaluate(result))
                    == BitConverter.SingleToInt32Bits(seed.ToFitness().Evaluate(result)),
                label);
        }
        // And at 3/4 players the seed follows the registry's own default.
        Assert.Equal(
            FitnessRegistry.Create("ffa-v3", 45f, 60f).Evaluate(FitnessFixtures.MultiPlayer[1].Result),
            FitnessRecipe.FromDefault(playerCount: 4).ToFitness().Evaluate(FitnessFixtures.MultiPlayer[1].Result));
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    public void ARecipeMayNotTakeAShippedVersionsName()
    {
        // The rule that keeps a custom instrument from ever being mistaken for a
        // shipped one in a manifest, a chart, or a filename.
        foreach (string shipped in FitnessRegistry.Names)
        {
            var clash = new FitnessRecipe(shipped, FitnessRecipe.FromDefault().Terms);
            var error = Assert.Throws<ArgumentException>(() => clash.Validate());
            Assert.Contains("shipped fitness version", error.Message);
        }
    }

    [Fact]
    public void UnknownTermIdsAreRejected()
    {
        var recipe = new FitnessRecipe("custom/bad", new[]
        {
            new FitnessRecipe.TermEntry("nonsense", new Dictionary<string, float>()),
        });
        Assert.Contains("nonsense", Assert.Throws<ArgumentException>(() => recipe.ToFitness()).Message);
    }

    [Fact]
    public void UndeclaredParameterKeysAreRejected()
    {
        var recipe = new FitnessRecipe("custom/typo", new[]
        {
            new FitnessRecipe.TermEntry(
                BlocksTerm.TermId, new Dictionary<string, float> { ["rewrad"] = 5f }),
        });
        Assert.Contains("rewrad", Assert.Throws<ArgumentException>(() => recipe.ToFitness()).Message);
    }

    [Fact]
    public void ATermListedTwiceIsRejected()
    {
        // It would silently count twice — a very quiet way to build a wrong instrument.
        var recipe = new FitnessRecipe("custom/double", new[]
        {
            new FitnessRecipe.TermEntry(BlocksTerm.TermId, new Dictionary<string, float>()),
            new FitnessRecipe.TermEntry(BlocksTerm.TermId, new Dictionary<string, float>()),
        });
        Assert.Contains("twice", Assert.Throws<ArgumentException>(() => recipe.ToFitness()).Message);
    }

    [Fact]
    public void AnEmptyOrUnnamedRecipeIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new FitnessRecipe("custom/empty", Array.Empty<FitnessRecipe.TermEntry>()).Validate());
        Assert.Throws<ArgumentException>(() =>
            new FitnessRecipe("  ", FitnessRecipe.FromDefault().Terms).Validate());
    }

    [Fact]
    public void AFutureFormatVersionIsRefusedRatherThanMisread()
    {
        var recipe = FitnessRecipe.FromDefault() with { FormatVersion = FitnessRecipe.CurrentFormatVersion + 1 };
        Assert.Throws<ArgumentException>(() => recipe.Validate());
    }

    [Fact]
    public void AHandWrittenRecipeMayOmitFormatVersionAndUnchangedConstants()
    {
        // The minimum a document has to carry: a name, term ids, and only the
        // constants it actually changed.
        FitnessRecipe recipe = FitnessRecipeJson.Deserialize("""
        {
          "name": "custom/hand-written",
          "terms": [
            { "id": "collisions", "params": { "scalar": 2.0 } },
            { "id": "stockFairness", "params": {} }
          ]
        }
        """);

        Assert.Equal(FitnessRecipe.CurrentFormatVersion, recipe.FormatVersion);
        FitnessComposer fitness = recipe.ToFitness();
        Assert.Equal(new[] { "collisions", "stockFairness" }, fitness.Terms.Select(t => t.Id));
        Assert.Equal(2f, fitness.Terms[0].Values["scalar"]);
        Assert.Equal(0f, fitness.Terms[0].Values["opponentOnly"]);   // defaulted
        Assert.Equal(3f, fitness.Terms[1].Values["baseline"]);       // defaulted
    }

    [Fact]
    public void StandardV2CannotBeCapturedBecauseItPredatesTermComposition()
    {
        Assert.Throws<ArgumentException>(() =>
            FitnessRecipe.FromShippedVersion("standard-v2", "custom/v2"));
    }

    [Fact]
    public void ARecipeCanDropATermEntirely()
    {
        // Omitting a term is how you turn it off — and the breakdown then reports what
        // the instrument actually measures rather than a row of zeros.
        FitnessRecipe seed = FitnessRecipe.FromDefault();
        var withoutJumps = new FitnessRecipe(seed.Name,
            seed.Terms.Where(t => t.Id != JumpsTerm.TermId).ToArray());

        FitnessComposer fitness = withoutJumps.ToFitness();
        Assert.DoesNotContain(JumpsTerm.TermId, fitness.Terms.Select(t => t.Id));
        MatchResult healthy = FitnessFixtures.TwoPlayer[0].Result;
        Assert.DoesNotContain("jumps", fitness.Breakdown(healthy).Select(r => r.Name));
        // 25 jumps against a 40 saturation is +6.25 on this fixture, and that is
        // exactly what the score loses.
        Assert.Equal(seed.ToFitness().Evaluate(healthy) - 6.25f, fitness.Evaluate(healthy), 0.001f);
    }

    internal static FitnessRecipe Edited(FitnessRecipe recipe, string termId, string key, float value) =>
        new(recipe.Name, recipe.Terms.Select(term => term.Id == termId
            ? new FitnessRecipe.TermEntry(
                term.Id, new Dictionary<string, float>(term.Params) { [key] = value })
            : term).ToArray())
        { FormatVersion = recipe.FormatVersion };
}
