using BrawlerSim.Fitness;
using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// The term catalog (2026-09-16): what a builder UI enumerates, and the round trip
/// between a term's declared constants, a parameter map, and a live term.
/// </summary>
public class FitnessTermRegistryTests
{
    private static MatchResult Healthy => FitnessFixtures.TwoPlayer[0].Result;

    [Fact]
    public void EveryTermIdIsUniqueAndEveryTermDeclaresAtLeastOneConstant()
    {
        var ids = FitnessTermRegistry.All.Select(e => e.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(FitnessTermRegistry.All, entry =>
        {
            Assert.NotEmpty(entry.Parameters);
            Assert.NotEmpty(entry.Label);
            Assert.NotEmpty(entry.Doc);
        });
    }

    [Fact]
    public void EveryTermParameterKeyIsUniqueWithinItsTerm()
    {
        Assert.All(FitnessTermRegistry.All, entry =>
        {
            var keys = entry.Parameters.Select(p => p.Key).ToArray();
            Assert.Equal(keys.Length, keys.Distinct().Count());
        });
    }

    [Fact]
    public void CreatingWithNoValuesUsesEveryDeclaredDefault()
    {
        // The contract that lets a UI ship a partial map: what a term reports back
        // must be exactly what the catalog said it would default to.
        foreach (FitnessTermRegistry.Entry entry in FitnessTermRegistry.All)
        {
            IFitnessTerm term = FitnessTermRegistry.Create(entry.Id);
            Assert.Equal(entry.Id, term.Id);
            foreach (FitnessTermParam spec in entry.Parameters)
            {
                Assert.True(term.Values.ContainsKey(spec.Key),
                    $"{entry.Id} does not report '{spec.Key}'");
                Assert.Equal(spec.Default, term.Values[spec.Key]);
            }
        }
    }

    [Fact]
    public void APartialParameterMapFillsTheRestFromTheDefaults()
    {
        IFitnessTerm term = FitnessTermRegistry.Create(
            StunLockTerm.TermId, new Dictionary<string, float> { ["tolerance"] = 0.4f });

        Assert.Equal(0.4f, term.Values["tolerance"]);
        Assert.Equal(StandardFitnessV3.DefaultStunLockWeight, term.Values["weight"]);
        Assert.Equal(100f, term.Values["percentScale"]);
    }

    [Fact]
    public void ValuesRoundTripThroughTheRegistryForEveryTerm()
    {
        // Rebuild each term from what it reported and it must score identically —
        // the property a saved recipe will depend on.
        var input = new FitnessInput(Healthy);
        foreach (FitnessTermRegistry.Entry entry in FitnessTermRegistry.All)
        {
            IFitnessTerm original = FitnessTermRegistry.Create(entry.Id);
            IFitnessTerm rebuilt = FitnessTermRegistry.Create(entry.Id, original.Values);

            Assert.Equal(original.Values, rebuilt.Values);
            Assert.True(
                BitConverter.SingleToInt32Bits(original.Evaluate(input))
                    == BitConverter.SingleToInt32Bits(rebuilt.Evaluate(input)),
                $"{entry.Id} did not round-trip");
        }
    }

    [Fact]
    public void EditedValuesRoundTripToo()
    {
        foreach (FitnessTermRegistry.Entry entry in FitnessTermRegistry.All)
        {
            // Nudge every constant off its default, within the declared control range.
            // Flags flip; everything else halves (clamped into its control range).
            var edited = entry.Parameters.ToDictionary(
                p => p.Key,
                p => p.IsFlag
                    ? (p.Default == 0f ? 1f : 0f)
                    : Math.Clamp(p.Default == 0f ? 1f : p.Default * 0.5f, p.Min, p.Max));

            IFitnessTerm term = FitnessTermRegistry.Create(entry.Id, edited);
            foreach ((string key, float value) in edited)
            {
                Assert.Equal(value, term.Values[key]);
            }
            Assert.Equal(term.Values, FitnessTermRegistry.Create(entry.Id, term.Values).Values);
        }
    }

    [Fact]
    public void UnknownTermIdsAreRejectedWithTheCatalogInTheMessage()
    {
        var error = Assert.Throws<ArgumentException>(() => FitnessTermRegistry.Create("nonsense"));
        Assert.Contains("nonsense", error.Message);
        Assert.Contains(TimeTerm.TermId, error.Message);
    }

    [Fact]
    public void UnknownParameterKeysOnATermAreRejected()
    {
        // A typo'd key must not silently do nothing — it would look like a knob that
        // does not work rather than a mistake.
        Assert.Throws<ArgumentException>(() => new StockFairnessTerm(
            new Dictionary<string, float> { ["baselines"] = 5f }).Evaluate(new FitnessInput(Healthy)));
    }

    [Fact]
    public void ComposeBuildsAnAssemblyInTheOrderGiven()
    {
        FitnessComposer composed = FitnessTermRegistry.Compose("test", new (string, IReadOnlyDictionary<string, float>?)[]
        {
            (DropThroughsTerm.TermId, null),
            (StockFairnessTerm.TermId, new Dictionary<string, float> { ["baseline"] = 10f }),
        });

        Assert.Equal(new[] { "dropThroughs", "stockFairness" }, composed.Terms.Select(t => t.Id));
        Assert.Equal(10f, composed.Terms[1].Values["baseline"]);
    }

    [Fact]
    public void EveryShippedVersionIsBuiltOnlyFromCatalogTerms()
    {
        // No term may exist that a designer cannot also reach — if a shipped version
        // uses it, the catalog lists it.
        var catalog = FitnessTermRegistry.All.Select(e => e.Id).ToHashSet();
        foreach (string name in new[]
                 {
                     "standard-v3", "standard-v4", "standard-v5", "standard-v6",
                     "standard-v7", "ffa-v1", "ffa-v2", "ffa-v3",
                 })
        {
            var fitness = (IFitnessBreakdown)FitnessRegistry.Create(name, 45f, 60f);
            foreach ((string term, _) in fitness.Breakdown(Healthy))
            {
                Assert.True(catalog.Contains(term), $"{name} uses uncatalogued term '{term}'");
            }
        }
    }
}
