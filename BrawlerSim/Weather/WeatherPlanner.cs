using NameGen.Data;
using NameGen.Features;
using NameGen.Traits;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Weather;

/// <summary>One planned emitter row: the preset row, the parallax factor its depth
/// inherited (speed on screen scales with it — the brief's depth-consistency rule),
/// and the density AFTER budget clamping.</summary>
public sealed record WeatherLayerPlan(WeatherLayerRow Row, float ParallaxFactor, float EffDensity)
{
    /// <summary>The on-screen speed multiplier: depth times the authored scale.</summary>
    public float EffSpeedScale => ParallaxFactor * Row.SpeedScale;
}

/// <summary>One weather instance, fully expanded: schedules for every dynamics
/// channel plus the per-layer plans. Evaluation is pure lookups on the match clock.</summary>
public sealed record WeatherInstancePlan(
    WeatherPreset Preset,
    DirectionPlan Direction,
    KeyframeSchedule Speed,
    KeyframeSchedule Intensity,
    KeyframeSchedule EpisodeGate,
    IReadOnlyList<WeatherLayerPlan> Layers)
{
    /// <summary>The instance's static worst-case opacity x density mass.</summary>
    public float StaticMax(Func<WeatherLayerPlan, bool>? filter = null) =>
        Layers.Where(l => filter?.Invoke(l) ?? true)
            .Sum(l => l.EffDensity * l.Row.OpacityCap)
        * Intensity.StaticMax() * EpisodeGate.StaticMax();
}

/// <summary>A stage's whole weather: zero (clear — most stages), one, or two
/// instances, plus whether the budget clamp fired (a debug note, never an error).</summary>
public sealed record WeatherPlan(IReadOnlyList<WeatherInstancePlan> Instances, bool BudgetClamped)
{
    public static readonly WeatherPlan Clear = new(Array.Empty<WeatherInstancePlan>(), false);
}

/// <summary>
/// Weather selection + schedule expansion (backgrounds track Phase 3, 2026-09-02 —
/// brief §Phase 3). One more seeded pick in the stage pass: presets are gated by
/// register and required traits (blood rain: horror + deadly), weighted by salient
/// -trait matches, and none-weighted so 60-75% of stages stay clear. Every dynamics
/// channel expands into a keyframe schedule at plan time; the global readability
/// budget is enforced HERE from static maxima (density clamp), so the renderer can
/// never overdraw the action band. View-only — nothing in the sim reads weather.
/// </summary>
public sealed class WeatherPlanner
{
    /// <summary>Private Pcg32 sequence for weather draws.</summary>
    private const ulong WeatherSequence = 0x4247574541545252UL; // "BGWEATRR"

    /// <summary>Schedules cover any legal match (MaxMatchSeconds default 300; the
    /// last value holds beyond the horizon).</summary>
    public const float HorizonS = 600f;

    private readonly NameGenData _data;
    private readonly FeatureExtractor _extractor;

    public WeatherConfig Config { get; }

    public WeatherPlanner(WeatherConfig config, NameGenData? data = null)
    {
        Config = config;
        _data = data ?? NameGenData.LoadEmbedded();
        _extractor = new FeatureExtractor(_data.Ranges);
    }

    /// <summary>Builds the stage's weather plan. farFactor/midFactor are the stage's
    /// background parallax factors (the emitters live IN those depths); action = 1.</summary>
    public WeatherPlan Plan(Genome.StageGenome stage, ulong seed,
        float farFactor, float midFactor)
    {
        var rng = new NgPcg(seed, WeatherSequence);
        if (rng.NextDouble() < Config.ClearProbability)
        {
            return WeatherPlan.Clear;
        }

        FeatureVector features = _extractor.ExtractStage(
            new NameGen.StageGenome(stage.Params.ToDictionary()));
        IReadOnlyList<SalientTrait> salient = TraitScorer.SelectSalient(
            features, _data.Traits.Stage, _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
        string register = Sprites.StageThemeSelector.PickRegister(
            new NgPcg(seed, Sprites.StageThemeSelector.ThemeSequence), _data);

        List<WeatherPreset> pool = Config.Presets
            .Where(p => (Config.LightningEnabled || p.Type != "lightning")
                && (p.Registers.Count == 0 || p.Registers.Contains(register))
                && p.RequiresTraits.All(t => salient.Any(s => s.Name == t)))
            .ToList();
        if (pool.Count == 0)
        {
            return WeatherPlan.Clear;
        }

        var instances = new List<WeatherInstancePlan>();
        WeatherPreset first = SamplePreset(pool, salient, rng);
        instances.Add(Expand(first, rng, farFactor, midFactor));
        bool second = rng.NextDouble() < Config.SecondInstanceProbability; // always drawn: stable stream
        if (second && pool.Count > 1)
        {
            List<WeatherPreset> rest = pool.Where(p => p.Name != first.Name).ToList();
            instances.Add(Expand(SamplePreset(rest, salient, rng), rng, farFactor, midFactor));
        }

        return ApplyBudget(instances);
    }

    /// <summary>Trait-weighted preset pick: 1 + 2 x (salient traits the preset names).</summary>
    private static WeatherPreset SamplePreset(
        List<WeatherPreset> pool, IReadOnlyList<SalientTrait> salient, NgPcg rng)
    {
        var weights = new double[pool.Count];
        double total = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            weights[i] = 1 + 2 * pool[i].Traits.Count(t => salient.Any(s => s.Name == t));
            total += weights[i];
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += weights[i];
            if (roll < acc)
            {
                return pool[i];
            }
        }
        return pool[^1];
    }

    private WeatherInstancePlan Expand(WeatherPreset preset, NgPcg rng,
        float farFactor, float midFactor)
    {
        DirectionPlan direction = DirectionPlan.Sample(
            preset.BaseDeg, preset.DriftDegPerMin, preset.OscAmpDeg, preset.OscPeriodS, rng);
        KeyframeSchedule speed = KeyframeSchedule.Channel(
            preset.Speed, preset.SpeedChangeEveryS, preset.SpeedTweenDurS,
            preset.SpeedEasing, rng, HorizonS);
        KeyframeSchedule intensity = KeyframeSchedule.Channel(
            preset.Intensity, preset.IntensityChangeEveryS, preset.IntensityTweenDurS,
            preset.IntensityEasing, rng, HorizonS);
        KeyframeSchedule gate = KeyframeSchedule.Episodes(
            preset.Persistent, preset.EpisodeDurS, preset.EpisodeGapS,
            preset.RampInS, preset.RampOutS, preset.EpisodeEasing, rng, HorizonS);
        var layers = preset.Layers.Select(row => new WeatherLayerPlan(row, row.Layer switch
        {
            "far" => Math.Max(0.05f, farFactor),
            "mid" => Math.Max(0.1f, midFactor),
            "near" => Config.NearFactor,
            _ => 1f, // action
        }, row.Density)).ToList();
        return new WeatherInstancePlan(preset, direction, speed, intensity, gate, layers);
    }

    /// <summary>The global + action-band budgets, enforced from static maxima by
    /// proportional density clamps (never rejection — the designer tunes budgets in
    /// data, the planner guarantees them).</summary>
    private WeatherPlan ApplyBudget(List<WeatherInstancePlan> instances)
    {
        bool clamped = false;
        float action = instances.Sum(i => i.StaticMax(l => l.Row.Layer == "action"));
        if (action > Config.ActionBandBudget && action > 0)
        {
            float scale = Config.ActionBandBudget / action;
            instances = instances.Select(i => i with
            {
                Layers = i.Layers.Select(l => l.Row.Layer == "action"
                    ? l with { EffDensity = l.EffDensity * scale } : l).ToList(),
            }).ToList();
            clamped = true;
        }
        float total = instances.Sum(i => i.StaticMax());
        if (total > Config.GlobalBudget && total > 0)
        {
            float scale = Config.GlobalBudget / total;
            instances = instances.Select(i => i with
            {
                Layers = i.Layers.Select(l => l with { EffDensity = l.EffDensity * scale }).ToList(),
            }).ToList();
            clamped = true;
        }
        return new WeatherPlan(instances, clamped);
    }
}
