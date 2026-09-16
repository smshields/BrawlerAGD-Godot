using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// The fitness REGRESSION HARNESS (2026-09-16, fitness term refactor). Pins every
/// shipped version's score AND its full per-term breakdown, BIT-EXACTLY, over the
/// FitnessFixtures battery.
///
/// Why bit-exact and not a tolerance: fitness drives selection, so a last-bit change
/// in any term rewrites every evolved population downstream of it. A tolerance-based
/// assertion would wave through exactly the drift this harness exists to catch.
/// Values are hashed per version (FNV-1a over the canonical table) rather than
/// transcribed — nine constants instead of two thousand lines — and a failing version
/// dumps its actual table to a temp file named in the assertion message, so a real
/// diff is one `diff` away.
///
/// RE-PIN RULE (same as the sim goldens): a hash here moves ONLY with a dated comment
/// stating which term changed and why. A refactor that is meant to preserve behavior
/// must move NOTHING.
/// </summary>
public class FitnessCharacterizationTests
{
    // Construction mirrors production: the registry's factory signature, the
    // historical 45 s target inside a 60 s cap (so the overtime fixture crosses the
    // cliff), and the default collision scalar.
    private const float Target = 45f;
    private const float Max = 60f;
    private const float CollisionScalar = StandardFitnessV3.DefaultCollisionScalar;

    /// <summary>Every shipped version, with the player counts it is allowed to score.
    /// Mirrors FitnessRegistry's SupportsNPlayers column.</summary>
    public static TheoryData<string, bool> Versions() => new()
    {
        { "standard-v2", false },
        { "standard-v3", false },
        { "standard-v4", false },
        { "standard-v5", false },
        { "standard-v6", false },
        { "standard-v7", false },
        { "ffa-v1", true },
        { "ffa-v2", true },
        { "ffa-v3", true },
    };

    /// <summary>version → FNV-1a of its canonical score table. Pinned 2026-09-16
    /// against the code as merged at 578b1d3 (directional projectiles + v7/ffa-v3),
    /// BEFORE the term extraction.</summary>
    private static readonly Dictionary<string, ulong> Baseline = new()
    {
        ["standard-v2"] = 44100183755262564UL,
        ["standard-v3"] = 17024880742285903365UL,
        ["standard-v4"] = 4908690327243585108UL,
        ["standard-v5"] = 72877831302095510UL,
        ["standard-v6"] = 1120200112591057067UL,
        ["standard-v7"] = 18179954067288389178UL,
        ["ffa-v1"] = 8451137360704105658UL,
        ["ffa-v2"] = 4490690082906436525UL,
        ["ffa-v3"] = 4875647087022155920UL,
    };

    [Theory]
    [MemberData(nameof(Versions))]
    public void ShippedVersionScoresAreUnchanged(string name, bool supportsNPlayers)
    {
        string table = Table(name, supportsNPlayers);
        ulong actual = Fnv1a.Hash(Encoding.UTF8.GetBytes(table), Fnv1a.OffsetBasis);

        if (actual != Baseline[name])
        {
            string dump = Path.Combine(Path.GetTempPath(), $"fitness-baseline-{name}.txt");
            File.WriteAllText(dump, table);
            Assert.Fail(
                $"{name} scores moved: expected hash {Baseline[name]}, got {actual}. " +
                $"Actual table written to {dump}. If this change is intended, re-pin the " +
                $"hash with a dated comment stating which term changed and why.");
        }
    }

    /// <summary>The canonical table: one line per case, the total then every
    /// breakdown entry, each as its raw IEEE bit pattern.</summary>
    private static string Table(string name, bool supportsNPlayers)
    {
        IFitnessFunction fitness = FitnessRegistry.Create(name, Target, Max, CollisionScalar);
        var sb = new StringBuilder();
        sb.Append(name).Append('\n');

        foreach ((string label, MatchResult result) in FitnessFixtures.TwoPlayer)
        {
            AppendCase(sb, fitness, label, result);
        }

        if (supportsNPlayers)
        {
            foreach ((string label, MatchResult result) in FitnessFixtures.MultiPlayer)
            {
                AppendCase(sb, fitness, label, result);
            }
        }

        return sb.ToString();
    }

    private static void AppendCase(StringBuilder sb, IFitnessFunction fitness, string label, MatchResult result)
    {
        sb.Append(label).Append('|').Append(Bits(fitness.Evaluate(result)));
        if (fitness is IFitnessBreakdown itemized)
        {
            foreach ((string term, float value) in itemized.Breakdown(result))
            {
                sb.Append('|').Append(term).Append('=').Append(Bits(value));
            }
        }
        sb.Append('\n');
    }

    private static string Bits(float value) =>
        BitConverter.SingleToInt32Bits(value).ToString("X8");

    /// <summary>The breakdown must itemize to the total exactly — a pair of
    /// compensating term errors cannot hide behind a matching sum.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void BreakdownSumsToTheScoreExactly(string name, bool supportsNPlayers)
    {
        IFitnessFunction fitness = FitnessRegistry.Create(name, Target, Max, CollisionScalar);
        if (fitness is not IFitnessBreakdown itemized)
        {
            return; // standard-v2 predates term composition by design.
        }

        var cases = new List<(string, MatchResult)>(FitnessFixtures.TwoPlayer);
        if (supportsNPlayers)
        {
            cases.AddRange(FitnessFixtures.MultiPlayer);
        }

        foreach ((string label, MatchResult result) in cases)
        {
            float sum = 0f;
            foreach ((_, float value) in itemized.Breakdown(result))
            {
                sum += value;
            }
            Assert.True(
                BitConverter.SingleToInt32Bits(sum) == BitConverter.SingleToInt32Bits(fitness.Evaluate(result)),
                $"{name}/{label}: breakdown sums to {sum}, Evaluate returned {fitness.Evaluate(result)}");
        }
    }
}
