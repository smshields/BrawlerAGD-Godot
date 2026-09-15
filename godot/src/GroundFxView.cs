using Godot;
using BrawlerSim.Genome;
using BrawlerSim.Vfx;
using BrawlerSim.Weather;

namespace BrawlerGodot;

/// <summary>
/// Footstep + landing dust (particle prototype, 2026-09-14; visibility pass
/// 2026-09-15 — render-chain entry 3b, added BEHIND the fighters so dust never
/// obscures a body). A pooled ring of one-shot CpuParticles2D anchored to the
/// world where they were kicked up (they do not follow the fighter). Every
/// particle is born small and GROWS into its final size while its alpha holds
/// then fades — final size and fade duration both scale with impact force /
/// movement speed. Landings fire TWO opposed fans so the cloud spreads in every
/// direction away from the platform; footsteps kick BACKWARD against the
/// movement, scattered by a spread cone. Magnitudes come from the pure
/// GroundFxConfig math (mass/size/impact speed); color matches the scene — the
/// tile theme's palette group, blended toward the active weather's particle
/// color by the live episode gate, under the light rig's fighter tint. The event
/// schedule is a pure function of sim state; per-particle jitter is cosmetic
/// (the weather exemption).
///
/// CPU particles, not GPU: bursts are tiny (tens of quads) and GpuParticles2D
/// silently rendered NOTHING inside the character-select pane's nested
/// SubViewport on the gl_compatibility renderer (diagnosed 2026-09-15 — the
/// emitters were visible in tree, correctly positioned and sized, and still drew
/// nothing, while the identical code drew fine in the arena and the evolve
/// preview). CpuParticles2D renders in every host, which a readability effect
/// has to.
/// </summary>
public partial class GroundFxView : Node2D
{
    // Landings consume two emitters (one fan each), and Restart() clears an
    // emitter's live particles — so the ring is sized to cycle slowly enough that
    // reuse rarely truncates a burst that is still on screen.
    private const int PoolSize = 24;
    private const float RefPpu = 72f;

    /// <summary>Small-host legibility (the character-select pane runs at ppu 16).
    /// Strict proportionality puts its puffs at ~3 px and ~0.2 alpha — correct in
    /// ratio, below the perceptual threshold in practice — so a tiny host floors
    /// the particle SIZE and lifts the alpha. Distances (launch speed, gravity,
    /// spawn band) stay strictly proportional, or the cloud would sprawl wider
    /// than the fighter. Only hosts under HostPpuFloor are affected; ArenaView
    /// and the evolve preview (both ppu 72) are untouched.</summary>
    private const float HostPpuFloor = 30f;
    private const float MinSizeScale = 0.8f;
    private const float SmallHostOpacityBoost = 1.6f;

    /// <summary>Screen pixels across at scale 1.0 and the reference framing —
    /// what the tuning file's scale numbers mean, independent of the puff
    /// texture's source resolution.</summary>
    private const float PuffUnitPx = 10f;

    private CpuParticles2D[] _pool = System.Array.Empty<CpuParticles2D>();
    private int _next;
    private float _ppu = RefPpu;
    private float _pxScale = 1f;     // ppu / 72 — distances, strictly proportional
    private float _sizeScale = 1f;   // ppu / 72, floored — particle size only
    private float _opacityBoost = 1f;
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
        _sizeScale = Mathf.Max(_pxScale, MinSizeScale);
        _opacityBoost = ppu < HostPpuFloor ? SmallHostOpacityBoost : 1f;
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
        // Born small, grows into the final size: fast early expansion easing off,
        // so a puff "blooms" the instant it appears and then drifts open.
        var growth = new Curve();
        growth.AddPoint(new Vector2(0f, config.ScaleBirthFraction));
        growth.AddPoint(new Vector2(0.35f, Mathf.Lerp(config.ScaleBirthFraction, 1f, 0.72f)));
        growth.AddPoint(new Vector2(1f, 1f));
        // Alpha holds, then fades across the tail — the fade window inherits the
        // force/velocity-driven lifetime.
        var fade = new Gradient();
        fade.SetOffset(0, 0f);
        fade.SetColor(0, new Color(1f, 1f, 1f, 1f));
        fade.SetOffset(1, 1f);
        fade.SetColor(1, new Color(1f, 1f, 1f, 0f));
        fade.AddPoint(config.FadeStartFraction, new Color(1f, 1f, 1f, 1f));

        _pool = new CpuParticles2D[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            _pool[i] = new CpuParticles2D
            {
                Texture = GroundFxBank.PuffTexture,
                OneShot = true,
                Explosiveness = 1f,
                Emitting = false,
                // Particles ride the emitter's own space. The emitter is only
                // repositioned by Restart(), which clears it, so a live puff never
                // teleports — and dust stays glued to the WORLD in every host,
                // including MovesetPreview, which pans by moving its world root
                // (global-space particles slid out from under it there).
                LocalCoords = true,
                // Dust is a procedural soft blob, not pixel art — the project's
                // nearest filtering turned scaled-up puffs into chunky octagons.
                TextureFilter = TextureFilterEnum.Linear,
                EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
                Gravity = new Vector2(0f, 70f * _pxScale * _sizeFactor),
                ScaleAmountCurve = growth,
                ColorRamp = fade,
            };
            AddChild(_pool[i]);
        }
    }

    /// <summary>weatherGate = the live EpisodeGate x Intensity of the stage's first
    /// weather instance (0 on clear stages and in previews).</summary>
    public void TriggerLanding(Vector2 feetWorld, float mass, float impactSpeed,
        float bodyHalfX, float bodyHalfY, float weatherGate)
    {
        GroundFxConfig config = GroundFxBank.Config;
        LandingBurst burst = config.ComputeLanding(mass, impactSpeed, bodyHalfX, bodyHalfY);
        if (burst.Count <= 0)
        {
            return;
        }
        // Two opposed fans tilted just above the platform surface: together they
        // cover every counter-platform direction, biased sideways so the cloud
        // spreads ALONG the ground instead of puffing straight up.
        float tilt = Mathf.DegToRad(config.LandingFanTiltDeg);
        var right = new Vector2(Mathf.Cos(tilt), -Mathf.Sin(tilt));
        var left = new Vector2(-right.X, right.Y);
        int perFan = Mathf.Max(1, Mathf.RoundToInt(burst.Count / 2f));
        // Spawn across the foot width so the fans read as one connected cloud.
        float spawnHalfWidth = bodyHalfX * 0.55f * _ppu;
        foreach (Vector2 dir in new[] { left, right })
        {
            Emit(feetWorld, dir, config.LandingFanSpreadDeg, perFan, burst.Scale,
                burst.SpreadSpeed, burst.Opacity, burst.Lifetime, weatherGate,
                spawnHalfWidth);
        }
    }

    /// <summary>velX is SIGNED — dust kicks back against the movement.</summary>
    public void TriggerFootstep(Vector2 feetWorld, float mass, float velX,
        float maxGroundSpeed, float bodyHalfX, float bodyHalfY, float weatherGate)
    {
        GroundFxConfig config = GroundFxBank.Config;
        FootstepPuff puff = config.ComputeFootstep(
            mass, Mathf.Abs(velX), maxGroundSpeed, bodyHalfY);
        if (puff.Count <= 0)
        {
            return;
        }
        float back = velX >= 0f ? -1f : 1f; // opposite the direction of travel
        float tilt = Mathf.DegToRad(config.FootstepBackTiltDeg);
        var dir = new Vector2(back * Mathf.Cos(tilt), -Mathf.Sin(tilt));
        Emit(feetWorld, dir, config.FootstepSpreadDeg, puff.Count, puff.Scale,
            puff.SpreadSpeed, puff.Opacity, puff.Lifetime, weatherGate,
            spawnHalfWidth: bodyHalfX * 0.35f * _ppu);
    }

    private void Emit(Vector2 feetWorld, Vector2 direction, float spreadDeg, int count,
        float scale, float spreadSpeed, float opacity, float lifetime, float weatherGate,
        float spawnHalfWidth)
    {
        if (_pool.Length == 0)
        {
            return;
        }
        CpuParticles2D particles = _pool[_next];
        _next = (_next + 1) % PoolSize; // oldest stolen when saturated

        float k = _pxScale * _sizeFactor;
        particles.Position = new Vector2(feetWorld.X * _ppu, -feetWorld.Y * _ppu);
        particles.Lifetime = Mathf.Max(0.05f, lifetime);
        particles.Amount = Mathf.Max(1, count);
        particles.Direction = direction;
        particles.Spread = spreadDeg;
        // A flat spawn band across the contact point: neighboring particles
        // overlap into a continuous sheet instead of separable dots.
        particles.EmissionRectExtents = new Vector2(
            Mathf.Max(1f, spawnHalfWidth), Mathf.Max(1f, 2f * k));
        particles.InitialVelocityMin = spreadSpeed * 0.45f * k;
        particles.InitialVelocityMax = spreadSpeed * 1.25f * k;
        // Damping scales with the launch speed: the burst punches out, then the
        // cloud settles and hangs while it grows and fades.
        particles.DampingMin = spreadSpeed * 0.8f * k;
        particles.DampingMax = spreadSpeed * 1.8f * k;
        float sizeK = _sizeScale * _sizeFactor * (PuffUnitPx / GroundFxBank.PuffTexturePx);
        particles.ScaleAmountMin = scale * sizeK * 0.75f;
        particles.ScaleAmountMax = scale * sizeK * 1.35f;

        Color color = _baseColor;
        if (_hasWeather && weatherGate > 0.01f)
        {
            color = _baseColor.Lerp(_weatherColor,
                Mathf.Min(GroundFxBank.Config.WeatherBlendMax, weatherGate));
        }
        color *= LightTint;
        particles.Modulate = color with
        {
            A = Mathf.Min(GroundFxConfig.OpacityCeiling, opacity * _opacityBoost),
        };
        particles.Restart();
    }
}
