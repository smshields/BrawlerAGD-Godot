using System.Diagnostics;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Full AI-vs-AI matches on the real study games: determinism, replay verification,
/// the cross-platform golden hash, and an evaluation-throughput smoke test.
/// </summary>
public class MatchTests
{
    private static GameGenome StudyGame(string name) =>
        LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", name)).Genome;

    private static MatchResult RunAiMatch(GameGenome genome, ulong seed, bool recordTrace = false)
    {
        var sources = new IInputSource[]
        {
            new DecisionTreeAgent(new Pcg32(seed, 0)),
            new DecisionTreeAgent(new Pcg32(seed, 1)),
        };
        return MatchRunner.Run(genome, sources, recordTrace: recordTrace);
    }

    [Fact]
    public void MatchesAlwaysTerminate()
    {
        foreach (string game in new[] { "GameA", "GameB", "GameC", "GameD", "GameE", "GameF" })
        {
            MatchResult result = RunAiMatch(StudyGame(game), seed: 7);
            Assert.True(result.Ticks <= MatchConfig.Default.MaxTicks);
            Assert.InRange(result.LoserIndex, -1, 1);
            Assert.All(result.Players, p => Assert.InRange(p.RemainingStocks, 0, 3));
        }
    }

    [Fact]
    public void SameSeedIsBitIdenticalDifferentSeedIsNot()
    {
        GameGenome genome = StudyGame("GameC");
        MatchResult a = RunAiMatch(genome, seed: 42);
        MatchResult b = RunAiMatch(genome, seed: 42);
        MatchResult c = RunAiMatch(genome, seed: 43);

        Assert.Equal(a.FinalHash, b.FinalHash);
        Assert.Equal(a.Ticks, b.Ticks);
        Assert.NotEqual(a.FinalHash, c.FinalHash);
    }

    [Fact]
    public void ReplayReproducesTheMatchBitExactly()
    {
        // The tick-equivalence guarantee: a recorded match re-run from its input trace
        // reaches the identical final state. This is the test the rendered path will
        // also have to pass in Phase 4.
        GameGenome genome = StudyGame("GameC");
        MatchResult live = RunAiMatch(genome, seed: 42, recordTrace: true);
        Assert.NotNull(live.Trace);

        MatchResult replayed = MatchRunner.Replay(genome, live.Trace!);
        Assert.Equal(live.FinalHash, replayed.FinalHash);
        Assert.Equal(live.Ticks, replayed.Ticks);
        Assert.Equal(live.LoserIndex, replayed.LoserIndex);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(live.Players[i].TotalDamageTaken, replayed.Players[i].TotalDamageTaken);
            Assert.Equal(live.Players[i].TotalHitsReceived, replayed.Players[i].TotalHitsReceived);
        }
    }

    [Fact]
    public void EvolvedGameProducesInteraction()
    {
        // Game C evolved specifically for hit trading; the ported sim + agent should
        // reproduce at least *some* interaction (exact numbers are calibration, Phase 5).
        MatchResult result = RunAiMatch(StudyGame("GameC"), seed: 11);
        int totalHits = result.Players[0].TotalHitsReceived + result.Players[1].TotalHitsReceived;
        Assert.True(totalHits > 0, "no hits landed in an evolved hit-trading game");
    }

    [Fact]
    public void PassiveInputsTimeOutAsADraw()
    {
        var sources = new IInputSource[] { ScriptedSource.Neutral, ScriptedSource.Neutral };
        MatchResult result = MatchRunner.Run(TestGames.FlatArena(), sources);
        Assert.Equal(-1, result.LoserIndex);
        Assert.Equal(MatchConfig.Default.MaxTicks, result.Ticks);
        Assert.Equal(60f, result.LengthSeconds, 0.001f);
    }

    /// <summary>
    /// Cross-platform / cross-runtime canary #2 (see Phase1PipelineTests for #1): a full
    /// physics + agent match pinned to its exact final hash, produced on macOS ARM64 /
    /// .NET 8. A mismatch on any platform means the sim is not bit-deterministic there —
    /// a determinism-contract release blocker, not a flaky test.
    /// </summary>
    [Fact]
    public void GoldenMatchHashMatches()
    {
        MatchResult result = RunAiMatch(StudyGame("GameC"), seed: 20260707);
        // Re-pinned 2026-07-10 (2nd): MaxStunSeconds 0.75 → 0.25 s — the second
        // stun-cap sweep showed 0.75 s still allowed re-stun chains (97%-stunned
        // round). Prior pins: 5450044395552427516 (0.75 s cap), 8640048477680184839
        // (2026-07-09, hash-format only), 1788087336528951335 (2026-07-08).
        Assert.Equal(13546504710617393521UL, result.FinalHash);
    }

    [Fact]
    public void EvaluationThroughputSupportsEvolutionScale()
    {
        // The whole point of the rewrite: population-scale evaluation must be fast.
        // 20 full matches must finish well under a second each even on CI hardware.
        GameGenome genome = StudyGame("GameC");
        var stopwatch = Stopwatch.StartNew();
        for (ulong seed = 0; seed < 20; seed++)
        {
            RunAiMatch(genome, seed);
        }
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 20_000,
            $"20 matches took {stopwatch.ElapsedMilliseconds} ms — evaluation is too slow");
    }
}
