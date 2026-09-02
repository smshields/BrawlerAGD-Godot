using System.Linq;
using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Weather;

namespace BrawlerGodot;

/// <summary>
/// The layered weather renderer (backgrounds track Phase 3, 2026-09-02 — brief
/// §Phase 3). Up to two seeded instances per stage; each preset layer row becomes
/// one GPUParticles2D emitter whose depth places it in the parallax stack (far/mid
/// rows draw with the backdrop, the action row in front of the arena at hard-capped
/// opacity, the near row as the bokeh plane) and whose on-screen SPEED inherits the
/// layer's parallax factor. Per-frame behavior is PURE LOOKUPS on the match clock
/// against the planner's keyframe schedules — direction, speed, intensity, episode
/// gate — with layerPhaseOffsetS delaying nearer rows so a gust visibly sweeps
/// back-to-front. Per-particle jitter is GPU-side and cosmetic (exempt from the
/// determinism guarantee by design; the schedules are the reproducible layer).
/// Pauses freeze it for free: the clock is the sim tick.
/// </summary>
public partial class WeatherSystem : Node2D
{
    private sealed record Emitter(
        Node2D Holder, GpuParticles2D Particles, ParticleProcessMaterial Material,
        WeatherInstancePlan Instance, WeatherLayerPlan Layer, int LayerIndex,
        float BaseOpacity);

    private readonly System.Collections.Generic.List<Emitter> _emitters = new();
    private ArenaCamera? _camera;

    /// <summary>The active plan (empty = clear skies), exposed for debugging.</summary>
    public WeatherPlan Plan { get; private set; } = WeatherPlan.Clear;

    public void Setup(float ppu, StageGenome stage, ArenaCamera? camera,
        float farFactor, float midFactor)
    {
        _camera = camera;
        Plan = WeatherBank.Planner.Plan(
            stage, BuiltGameNaming.NamingSeed(stage), farFactor, midFactor);
        GD.Print(Plan.Instances.Count == 0
            ? "weather: clear"
            : "weather: " + string.Join(" + ", Plan.Instances.Select(i => i.Preset.Name))
                + (Plan.BudgetClamped ? " (budget-clamped)" : ""));
        BrawlerSim.Determinism.Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
        float halfW = blast.X * ppu;
        float halfH = blast.Y * ppu;
        float areaScale = Mathf.Clamp(halfW * halfH * 4f / (1280f * 720f), 0.4f, 4f);

        foreach (WeatherInstancePlan instance in Plan.Instances)
        {
            for (int i = 0; i < instance.Layers.Count; i++)
            {
                WeatherLayerPlan layer = instance.Layers[i];
                var material = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                    EmissionBoxExtents = new Vector3(halfW * 1.2f, halfH * 1.2f, 0f),
                    Gravity = Vector3.Zero,
                    Spread = 12f,
                    ScaleMin = layer.Row.SizeScale * 0.8f,
                    ScaleMax = layer.Row.SizeScale * 1.25f,
                };
                var particles = new GpuParticles2D
                {
                    Texture = WeatherBank.TextureFor(instance.Preset.Type),
                    ProcessMaterial = material,
                    Amount = Mathf.Clamp(
                        (int)(layer.EffDensity * 320f * areaScale), 2, 800),
                    Lifetime = instance.Preset.Type is "fog" or "dust" ? 10f : 5f,
                    Preprocess = 4f,
                    LocalCoords = true, // the whole depth plane shifts with its holder
                    TextureFilter = TextureFilterEnum.Nearest,
                    ZIndex = layer.Row.Layer switch
                    {
                        "action" => 1, // above stage + players (opacity hard-capped)
                        "near" => 2,   // the bokeh plane
                        _ => 0,        // far/mid ride the backdrop depth
                    },
                    Modulate = WeatherBank.ParticleColor(
                        instance.Preset.Type, instance.Preset.PaletteRamp)
                        with { A = 0f },
                };
                var holder = new Node2D();
                AddChild(holder);
                holder.AddChild(particles);
                _emitters.Add(new Emitter(holder, particles, material, instance, layer, i,
                    layer.Row.OpacityCap));
            }
        }
    }

    /// <summary>Advance to the match clock (sim ticks — pause freezes weather).</summary>
    public void Sync(int tick)
    {
        if (_emitters.Count == 0)
        {
            return;
        }
        float t = tick / 60f;
        foreach (Emitter e in _emitters)
        {
            // Nearer rows run slightly BEHIND the schedule — the gust sweeps
            // back-to-front through the stack (the cheapest depth sell there is).
            float tLayer = Mathf.Max(0f, t - e.LayerIndex * e.Instance.Preset.LayerPhaseOffsetS);
            float gate = e.Instance.EpisodeGate.Evaluate(tLayer);
            float intensity = e.Instance.Intensity.Evaluate(tLayer);
            bool visible = gate > 0.01f;
            e.Particles.Emitting = visible;
            if (!visible)
            {
                e.Particles.Modulate = e.Particles.Modulate with { A = 0f };
                continue;
            }
            float speed = e.Instance.Speed.Evaluate(tLayer) * e.Layer.EffSpeedScale;
            float directionDeg = e.Instance.Direction.Evaluate(tLayer);
            float rad = Mathf.DegToRad(directionDeg);
            var dir = new Vector3(Mathf.Cos(rad), -Mathf.Sin(rad), 0f); // world y-up → screen y-down
            e.Material.Direction = dir;
            e.Material.InitialVelocityMin = speed * 0.8f;
            e.Material.InitialVelocityMax = speed * 1.2f;
            if (e.Instance.Preset.Type is "rain" or "sand")
            {
                // Streak textures align with their motion (texture drawn along +Y).
                float angle = -directionDeg - 90f;
                e.Material.AngleMin = angle;
                e.Material.AngleMax = angle;
            }
            e.Particles.AmountRatio = Mathf.Clamp(0.25f + 0.75f * intensity, 0f, 1f) * gate;
            e.Particles.Modulate = e.Particles.Modulate with
            {
                A = e.BaseOpacity * gate * (0.45f + 0.55f * intensity),
            };
            // Parallax: the row trails the camera by (1 − factor) of its motion.
            if (_camera is not null)
            {
                e.Holder.Position = _camera.Position * (1f - e.Layer.ParallaxFactor);
            }
        }
    }
}
