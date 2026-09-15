using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Vfx;
using BrawlerSim.Weather;

namespace BrawlerGodot;

/// <summary>
/// Footstep + landing dust (particle prototype, 2026-09-14; render-chain entry 3b —
/// added BEHIND the fighters so dust never obscures a body). A pooled ring of
/// one-shot GPUParticles2D in WORLD coordinates (puffs stay where they were kicked
/// up, the opposite of weather's parallax-plane model). Magnitudes come from the
/// pure GroundFxConfig math (mass/size/impact speed); color matches the scene —
/// the tile theme's palette group, blended toward the active weather's particle
/// color by the live episode gate, under the light rig's fighter-tint band. The
/// event schedule is a pure function of sim state; per-particle jitter is GPU-side
/// and cosmetic (the weather exemption).
/// </summary>
public partial class GroundFxView : Node2D
{
    private const int PoolSize = 12;
    private const float RefPpu = 72f;

    private GpuParticles2D[] _pool = System.Array.Empty<GpuParticles2D>();
    private ParticleProcessMaterial[] _materials = System.Array.Empty<ParticleProcessMaterial>();
    private int _next;
    private float _ppu = RefPpu;
    private float _pxScale = 1f;     // ppu / 72 — previews render proportionally
    private float _sizeFactor = 1f;  // big-map legibility (the WeatherSystem trick)
    private Color _baseColor = new(0.85f, 0.84f, 0.81f);
    private Color _weatherColor;
    private bool _hasWeather;

    /// <summary>The light rig's fighter-tint band (set by ArenaView when a rig is
    /// live) so dust sits in scene lighting exactly like the fighters.</summary>
    public Color LightTint { get; set; } = Colors.White;

    /// <summary>weatherPlan null = host without weather (previews): theme-neutral
    /// dust only.</summary>
    public void Setup(float ppu, StageGenome stage, WeatherPlan? weatherPlan)
    {
        _ppu = ppu;
        _pxScale = ppu / RefPpu;
        BrawlerSim.Determinism.Vec2 blast = StageRules.BlastHalfExtents(stage.Params);
        _sizeFactor = Mathf.Clamp(blast.Y * ppu / 360f, 1f, 3f);

        // Stage-neutral dust: a grey-white base re-anchored onto the tile theme's
        // palette group (the WeatherBank.ParticleColor mechanism); legacy
        // null-theme stages keep the plain grey-white.
        BrawlerSim.Sprites.ThemeDef? theme = ThemeBank.Library.ByName(stage.ThemeId);
        if (theme is not null && theme.PaletteGroup.Length > 0 && theme.PaletteGroup != "grey")
        {
            (byte r, byte g, byte b) = BackgroundBank.Palette.RemapColor(
                (214, 210, 200), "grey", theme.PaletteGroup);
            _baseColor = new Color(r / 255f, g / 255f, b / 255f);
        }
        if (weatherPlan is { Instances.Count: > 0 })
        {
            WeatherPreset preset = weatherPlan.Instances[0].Preset;
            _weatherColor = WeatherBank.ParticleColor(preset.Type, preset.PaletteRamp);
            _hasWeather = true;
        }

        GroundFxConfig config = GroundFxBank.Config;
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var fadeRamp = new GradientTexture1D { Gradient = fade };
        _pool = new GpuParticles2D[PoolSize];
        _materials = new ParticleProcessMaterial[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            _materials[i] = new ParticleProcessMaterial
            {
                Direction = new Vector3(0f, -1f, 0f), // screen-up out of the ground
                Spread = 80f,
                Gravity = new Vector3(0f, 90f * _pxScale * _sizeFactor, 0f),
                DampingMin = 20f * _pxScale,
                DampingMax = 60f * _pxScale,
                ColorRamp = fadeRamp, // soft fade-out, no end-of-life pop
            };
            _pool[i] = new GpuParticles2D
            {
                Texture = GroundFxBank.PuffTexture,
                ProcessMaterial = _materials[i],
                // Fixed allocation (resizing Amount reallocates GPU buffers);
                // per-trigger count rides AmountRatio instead.
                Amount = config.MaxBurstCount,
                OneShot = true,
                Explosiveness = 1f,
                Emitting = false,
                LocalCoords = false,
                TextureFilter = TextureFilterEnum.Nearest,
            };
            AddChild(_pool[i]);
        }
    }

    /// <summary>weatherGate = the live EpisodeGate x Intensity of the stage's first
    /// weather instance (0 on clear stages and in previews).</summary>
    public void TriggerLanding(Vector2 feetWorld, float mass, float impactSpeed,
        float bodyHalfX, float bodyHalfY, float weatherGate)
    {
        LandingBurst burst = GroundFxBank.Config.ComputeLanding(
            mass, impactSpeed, bodyHalfX, bodyHalfY);
        if (burst.Count > 0)
        {
            Emit(feetWorld, burst.Count, burst.Scale, burst.SpreadSpeed,
                burst.Opacity, burst.Lifetime, weatherGate, spreadDeg: 85f);
        }
    }

    public void TriggerFootstep(Vector2 feetWorld, float mass, float absVelX,
        float maxGroundSpeed, float bodyHalfY, float weatherGate)
    {
        FootstepPuff puff = GroundFxBank.Config.ComputeFootstep(
            mass, absVelX, maxGroundSpeed, bodyHalfY);
        if (puff.Count > 0)
        {
            // Footsteps rise gently — a fraction of the landing spread band.
            Emit(feetWorld, puff.Count, puff.Scale, 30f,
                puff.Opacity, puff.Lifetime, weatherGate, spreadDeg: 35f);
        }
    }

    private void Emit(Vector2 feetWorld, int count, float scale, float spreadSpeed,
        float opacity, float lifetime, float weatherGate, float spreadDeg)
    {
        if (_pool.Length == 0)
        {
            return;
        }
        GpuParticles2D particles = _pool[_next];
        ParticleProcessMaterial material = _materials[_next];
        _next = (_next + 1) % PoolSize; // oldest stolen when saturated (life <= 0.6 s)

        float k = _pxScale * _sizeFactor;
        particles.Position = new Vector2(feetWorld.X * _ppu, -feetWorld.Y * _ppu);
        particles.Lifetime = Mathf.Max(0.05f, lifetime);
        particles.AmountRatio = count / (float)GroundFxBank.Config.MaxBurstCount;
        material.Spread = spreadDeg;
        material.InitialVelocityMin = spreadSpeed * 0.6f * k;
        material.InitialVelocityMax = spreadSpeed * 1.2f * k;
        material.ScaleMin = scale * k * 0.8f;
        material.ScaleMax = scale * k * 1.3f;

        Color color = _baseColor;
        if (_hasWeather && weatherGate > 0.01f)
        {
            color = _baseColor.Lerp(_weatherColor,
                Mathf.Min(GroundFxBank.Config.WeatherBlendMax, weatherGate));
        }
        color *= LightTint;
        particles.Modulate = color with { A = opacity };
        particles.Restart();
    }
}
