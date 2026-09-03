using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Weather;
using Xunit;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Tests.Weather;

/// <summary>
/// The seeded keyframe scheduler + weather planner (backgrounds track Phase 3,
/// 2026-09-02 — brief §Phase 3): schedule determinism across builds, curve
/// continuity at keyframe and episode boundaries, envelope containment over many
/// seeds, the static budget including stacked instances, the clear-stage fraction,
/// per-layer speed inheritance from parallax factors, and the blood-rain gate.
/// </summary>
public class WeatherScheduleTests
{
    private static readonly Lazy<WeatherConfig> ConfigLazy = new(() =>
        WeatherConfig.LoadFile(FindRepoFile(
            Path.Combine("godot", "assets", "weather_presets.json"))));

    private static WeatherConfig Config => ConfigLazy.Value;

    private static WeatherPlanner Planner() => new(Config);

    private static StageGenome Stage(ulong seed) =>
        GameGenome.Generate(GenerationConfig.Default, new Pcg32(seed)).Stage;

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

    // ── the scheduler primitives ───────────────────────────────────────────────

    [Fact]
    public void EveryEasingIsEndpointExactAndMonotone()
    {
        foreach (string easing in EasingLibrary.Names)
        {
            Assert.True(Math.Abs(EasingLibrary.Evaluate(easing, 0f)) < 1e-3f, easing);
            Assert.True(Math.Abs(EasingLibrary.Evaluate(easing, 1f) - 1f) < 1e-3f, easing);
            float prev = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float v = EasingLibrary.Evaluate(easing, i / 100f);
                Assert.True(v >= prev - 1e-4f, $"{easing} not monotone at {i / 100f}");
                prev = v;
            }
        }
    }

    [Fact]
    public void SameSeedRegeneratesAnIdenticalSchedule()
    {
        var range = new Envelope(0.3f, 1f);
        var every = new Envelope(4f, 20f);
        var tween = new Envelope(1.5f, 6f);
        var easings = new[] { "cubicInOut", "sineIn" };
        KeyframeSchedule a = KeyframeSchedule.Channel(
            range, every, tween, easings, new NgPcg(99, 7), 600f);
        KeyframeSchedule b = KeyframeSchedule.Channel(
            range, every, tween, easings, new NgPcg(99, 7), 600f);
        Assert.Equal(a.Segments, b.Segments);
        for (float t = 0; t < 650f; t += 0.37f)
        {
            Assert.Equal(a.Evaluate(t), b.Evaluate(t));
        }
    }

    [Fact]
    public void EvaluatedCurvesAreContinuousAtEverySegmentBoundary()
    {
        // Holds start where the previous tween ended; tweens start where the hold
        // sat — the value never pops at a keyframe or an episode boundary.
        for (ulong seed = 1; seed <= 50; seed++)
        {
            KeyframeSchedule channel = KeyframeSchedule.Channel(
                new Envelope(0f, 1f), new Envelope(2f, 10f), new Envelope(1f, 5f),
                new[] { "sineInOut", "cubicOut", "expoOut", "linear" },
                new NgPcg(seed, 3), 300f);
            KeyframeSchedule episodes = KeyframeSchedule.Episodes(
                persistent: false, new Envelope(10f, 40f), new Envelope(5f, 20f),
                3f, 4f, "cubicOut", new NgPcg(seed, 4), 300f);
            foreach (KeyframeSchedule schedule in new[] { channel, episodes })
            {
                foreach (ScheduleSegment s in schedule.Segments)
                {
                    const float eps = 1e-3f;
                    Assert.True(Math.Abs(schedule.Evaluate(s.T0 - eps)
                        - schedule.Evaluate(s.T0 + eps)) < 0.05f, "pop at segment start");
                    Assert.True(Math.Abs(s.End - schedule.Evaluate(s.T1 + eps)) < 1e-3f,
                        "easing endpoint missed");
                }
            }
        }
    }

    [Fact]
    public void EpisodesStartActiveAndDurationsAndGapsStayInsideTheirEnvelopes()
    {
        var dur = new Envelope(20f, 70f);
        var gap = new Envelope(8f, 40f);
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            KeyframeSchedule gate = KeyframeSchedule.Episodes(
                persistent: false, dur, gap, 4f, 6f, "cubicOut", new NgPcg(seed, 11), 600f);
            // Weather is PRESENT at t = 0 (designer 2026-09-02): the first episode
            // is already running and its ramp-down ends within the dur envelope.
            Assert.Equal(1f, gate.Evaluate(0f));
            var segments = gate.Segments;
            Assert.True(segments.Count >= 3 && segments.Count % 2 == 1);
            ScheduleSegment firstDown = segments[0];
            Assert.Equal(1f, firstDown.Start);
            Assert.True(dur.Contains(firstDown.T1, 0.51f),
                $"first episode ends at {firstDown.T1}, outside the dur envelope");
            float prevEnd = firstDown.T1;
            for (int i = 1; i < segments.Count; i += 2)
            {
                ScheduleSegment up = segments[i];
                ScheduleSegment down = segments[i + 1];
                float gapLen = up.T0 - prevEnd;
                float durLen = down.T1 - up.T0;
                Assert.True(gap.Contains(gapLen, 0.51f), $"gap {gapLen} outside envelope");
                Assert.True(dur.Contains(durLen, 10.51f), $"dur {durLen} outside envelope");
                prevEnd = down.T1;
            }
        }
    }

    // ── the planner over the shipped presets ───────────────────────────────────

    [Fact]
    public void ClearFractionLandsInsideTheDesignBand()
    {
        WeatherPlanner planner = Planner();
        int clear = 0;
        const int n = 3000;
        for (ulong seed = 1; seed <= n; seed++)
        {
            StageGenome stage = Stage(seed);
            WeatherPlan plan = planner.Plan(
                stage, BrawlerSim.Serialization.BuiltGameNaming.NamingSeed(stage), 0.1f, 0.4f);
            if (plan.Instances.Count == 0)
            {
                clear++;
            }
        }
        Assert.InRange(clear / (double)n, 0.60, 0.75);
    }

    [Fact]
    public void PlansAreDeterministicAndBudgetedIncludingStackedInstances()
    {
        WeatherPlanner planner = Planner();
        int stacked = 0, planned = 0;
        for (ulong seed = 1; seed <= 800; seed++)
        {
            StageGenome stage = Stage(seed);
            ulong s = BrawlerSim.Serialization.BuiltGameNaming.NamingSeed(stage);
            WeatherPlan plan = planner.Plan(stage, s, 0.1f, 0.4f);
            WeatherPlan again = planner.Plan(stage, s, 0.1f, 0.4f);
            Assert.Equal(plan.Instances.Count, again.Instances.Count);
            for (int i = 0; i < plan.Instances.Count; i++)
            {
                Assert.Equal(plan.Instances[i].Preset.Name, again.Instances[i].Preset.Name);
                Assert.Equal(plan.Instances[i].Direction, again.Instances[i].Direction);
                Assert.Equal(plan.Instances[i].Speed.Segments, again.Instances[i].Speed.Segments);
            }
            if (plan.Instances.Count == 0)
            {
                continue;
            }
            planned++;
            if (plan.Instances.Count == 2)
            {
                stacked++;
            }
            // The budget holds at the schedules' STATIC maxima — the worst case any
            // frame can render, two instances included.
            float total = plan.Instances.Sum(i => i.StaticMax());
            float action = plan.Instances.Sum(i => i.StaticMax(l => l.Row.Layer == "action"));
            Assert.True(total <= Config.GlobalBudget + 1e-3f, $"seed {seed}: total {total}");
            Assert.True(action <= Config.ActionBandBudget + 1e-3f, $"seed {seed}: action {action}");

            // Per-layer speed inherits the parallax factor (depth consistency).
            foreach (WeatherInstancePlan instance in plan.Instances)
            {
                foreach (WeatherLayerPlan layer in instance.Layers)
                {
                    float expected = layer.Row.Layer switch
                    {
                        "far" => 0.1f,
                        "mid" => 0.4f,
                        "near" => Config.NearFactor,
                        _ => 1f,
                    };
                    Assert.True(Math.Abs(expected * layer.Row.SpeedScale - layer.EffSpeedScale) < 1e-4f);
                }
            }
        }
        Assert.True(planned > 150, "too few weathered stages sampled");
        Assert.True(stacked > 5, "stacked instances never happened");
    }

    [Fact]
    public void BloodRainOnlyFallsOnDeadlyHorrorStages()
    {
        WeatherPlanner planner = Planner();
        int bloodSeen = 0;
        for (ulong seed = 1; seed <= 4000; seed++)
        {
            StageGenome stage = Stage(seed);
            ulong s = BrawlerSim.Serialization.BuiltGameNaming.NamingSeed(stage);
            WeatherPlan plan = planner.Plan(stage, s, 0.1f, 0.4f);
            foreach (WeatherInstancePlan instance in plan.Instances)
            {
                if (instance.Preset.Name != "bloodrain")
                {
                    continue;
                }
                bloodSeen++;
                // The gate's register comes from the SAME shared derivation every
                // selector replays; the background selector exposes it publicly.
                var themeLibrary = BrawlerSim.Sprites.StageThemeLibrary.LoadFile(FindRepoFile(
                    Path.Combine("godot", "assets", "tiles_v2_slices.json")));
                Assert.Equal("horror", new BrawlerSim.Sprites.StageThemeSelector(themeLibrary)
                    .PickRegister(s));
            }
        }
        // The gate is restrictive by design; the corpus of generated stages still
        // produces SOME deadly horror stages, or the preset is dead data.
        Assert.True(bloodSeen > 0, "blood rain never fell — gate or data broken");
    }

    [Fact]
    public void LightningStaysFlagDisabled()
    {
        Assert.False(Config.LightningEnabled); // the M5 gate ships CLOSED
        WeatherPlanner planner = Planner();
        for (ulong seed = 1; seed <= 1500; seed++)
        {
            StageGenome stage = Stage(seed);
            WeatherPlan plan = planner.Plan(
                stage, BrawlerSim.Serialization.BuiltGameNaming.NamingSeed(stage), 0.1f, 0.4f);
            Assert.DoesNotContain(plan.Instances, i => i.Preset.Type == "lightning");
        }
    }
}
