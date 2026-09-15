using BrawlerSim.Determinism;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Fitness;

/// <summary>
/// standard-v6 (2026-09-09, the scaled-time stage-diversity experiment): v5 with the
/// time target re-anchored to sqrt(mapArea / legacyMapArea), clamped to [0.5, 5].
/// Legacy-size maps and null StageMetrics must score EXACTLY as v5 — the version
/// exists to stop the flat 45 s target from pricing big/small maps out of the
/// population, not to change anything else.
/// </summary>
public class ScaledTimeFitnessTests
{
    private static MatchResult Result(float lengthSeconds, StageMetrics? stage) =>
        new(
            new[]
            {
                new PlayerStats(150f, 10, 2, 0, new[] { 100f, 50f }),
                new PlayerStats(400f, 5, 3, 0, new[] { 400f }),
            },
            LoserIndex: 0,
            Ticks: (int)(lengthSeconds * 60),
            LengthSeconds: lengthSeconds,
            FinalHash: 0,
            Trace: null,
            Placements: null,
            Stage: stage);

    private static StageMetrics LegacyStage => new(
        StageRules.LegacyVisibleHalfWidth, StageRules.LegacyVisibleHalfHeight);

    /// <summary>Extents scaled linearly from legacy by a factor per axis.</summary>
    private static StageMetrics ScaledStage(float wFactor, float hFactor) => new(
        StageRules.LegacyVisibleHalfWidth * wFactor,
        StageRules.LegacyVisibleHalfHeight * hFactor);

    [Fact]
    public void V6EqualsV5OnLegacySizeAndOnNullMetrics()
    {
        var v5 = new StandardFitnessV5();
        var v6 = new StandardFitnessV6();
        foreach (float length in new[] { 20f, 45f, 90f })
        {
            Assert.Equal(v5.Evaluate(Result(length, null)), v6.Evaluate(Result(length, null)));
            Assert.Equal(
                v5.Evaluate(Result(length, LegacyStage)),
                v6.Evaluate(Result(length, LegacyStage)));
        }
    }

    [Fact]
    public void TimeScaleIsTheLinearMapSizeRatio()
    {
        // Geometric mean of the per-axis ratios: 2× both axes → 2; 4× width alone → 2.
        Assert.Equal(1f, StandardFitnessV6.TimeScale(LegacyStage), 0.0001f);
        Assert.Equal(2f, StandardFitnessV6.TimeScale(ScaledStage(2f, 2f)), 0.0001f);
        Assert.Equal(2f, StandardFitnessV6.TimeScale(ScaledStage(4f, 1f)), 0.0001f);
        Assert.Equal(1f, StandardFitnessV6.TimeScale(null), 0.0001f);
        // Clamped to the generation envelope: 0.5×–5× per axis.
        Assert.Equal(5f, StandardFitnessV6.TimeScale(ScaledStage(10f, 10f)), 0.0001f);
        Assert.Equal(0.5f, StandardFitnessV6.TimeScale(ScaledStage(0.1f, 0.1f)), 0.0001f);
    }

    [Fact]
    public void TimeTargetMovesToTheScaledAnchor()
    {
        var v5 = new StandardFitnessV5();
        var v6 = new StandardFitnessV6();
        StageMetrics doubled = ScaledStage(2f, 2f); // s = 2 → scaled target 90 s

        // A 90 s match on a 2× map: v5 charges −45 for time, v6 charges 0 → +45.
        Assert.Equal(
            v5.Evaluate(Result(90f, doubled)) + 45f,
            v6.Evaluate(Result(90f, doubled)), 0.001f);
        // A 45 s match on the same map is now 45 s SHORT of its anchor → −45 vs v5.
        Assert.Equal(
            v5.Evaluate(Result(45f, doubled)) - 45f,
            v6.Evaluate(Result(45f, doubled)), 0.001f);
    }

    [Fact]
    public void BreakdownAppendsTheTimeRescaleTerm()
    {
        var terms = new StandardFitnessV6().Breakdown(Result(90f, ScaledStage(2f, 2f)));
        Assert.Equal("timeRescale", terms[^1].Name);
        Assert.Equal(45f, terms[^1].Value, 0.001f);
        // The flat v3 time term is still listed — the rescale is a visible correction.
        Assert.Contains(terms, t => t.Name == "time");
    }

    [Fact]
    public void RegistryConstructsV6AndTheDefaultIsUnchanged()
    {
        // 2026-09-14: the default graduated to standard-v7 (self-hit-blind + the
        // scaled time target, designer-directed) — v6 itself stays opt-in.
        Assert.Equal("standard-v7", FitnessRegistry.DefaultName);
        Assert.Equal("standard-v6", FitnessRegistry.Create("standard-v6", 45f, 300f).Name);
        // 2P-only, like the rest of the standard family.
        Assert.Throws<ArgumentException>(
            () => FitnessRegistry.Create("standard-v6", 45f, 300f, playerCount: 3));
    }

    [Fact]
    public void SimWorldRecordsStageMetricsFromTheGenome()
    {
        var genome = GameGenome.Generate(GenerationConfig.Default, new Pcg32(11));
        MatchResult result = new SimWorld(genome).BuildResult();
        Assert.NotNull(result.Stage);
        Assert.Equal(
            genome.Stage.Params.Get(StageParams.VisibleHalfWidth),
            result.Stage!.VisibleHalfWidth);
        Assert.Equal(
            genome.Stage.Params.Get(StageParams.VisibleHalfHeight),
            result.Stage.VisibleHalfHeight);
    }
}
