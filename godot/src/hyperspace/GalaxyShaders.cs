using Godot;
using BrawlerSim.Hyperspace;

namespace BrawlerGodot.Hyperspace;

/// <summary>
/// The galaxy view's materials and generated textures.
///
/// HALOS ARE ART, NOT BLOOM (designer, 2026-09-16). The spec leaned on
/// WorldEnvironment glow for the star halo, but this project renders under
/// gl_compatibility, where glow behaves differently from Forward+ and a hard bloom
/// threshold is itself a pop-in source (§8.1.5) — a star crossing the threshold as
/// you approach visibly snaps. An authored radial falloff baked into the sprite
/// grows continuously at every distance and looks identical on both renderers.
/// </summary>
public static class GalaxyShaders
{
    /// <summary>
    /// Quad half-extent as a multiple of the body's world radius. The procedural
    /// sphere fills the quad exactly, so 2.0 means the disc IS the body. (History:
    /// 20 was a floodlight you could sit inside, 6 a halo that fogged dense fields,
    /// 2.4 a translucent disc with a soft rim — each a designer play-test round.)
    /// </summary>
    public const float SpriteScale = 2f;

    /// <summary>A star's core never shrinks below this on screen, so the far end of
    /// the lane stays populated (§8.1: no far cull for stars). Same quad, same
    /// texture — a size clamp, never a representation swap.</summary>
    /// 2.4 made every distant star clamp to the same size, so a galaxy seen from the
    /// lane was a cluster of identical soft balls with the fitness-size encoding gone.
    public const float MinStarPixels = 1.2f;

    /// <summary>...and never grows past this, so flying up to a star gives you a
    /// bright disc rather than a white screen. The cap is on SIZE, not brightness:
    /// dimming a star as you approach would read as the star going out.</summary>
    public const float MaxStarPixels = 90f;

    private static Shader? _star;
    private static Texture2D? _halo;

    public static Shader Star() => _star ??= new Shader
    {
        Code = """
        shader_type spatial;
        // OPAQUE since 2026-09-17 (designer: "solid, not transparent"): no blend
        // mode, no ALPHA — bodies write depth and genuinely occlude what is behind
        // them. Dimming (distance fade, fitness, ignition) goes through ALBEDO
        // instead, which on black space looks identical and costs less: opaque quads
        // skip blending and get early-Z.
        render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;

        uniform float fade_numerator = 968.0;
        uniform float sprite_scale = 2.0;
        uniform float min_pixels = 1.2;
        uniform float max_pixels = 90.0;
        // 0 = self-luminous limb darkening (stars); 1 = a fixed key light with a
        // terminator (planets). Both make the disc read as a BALL, not a dot.
        uniform float shade_directional = 0.0;
        // World units per screen pixel at one unit of depth: 2*tan(fov/2)/viewport_h.
        uniform float world_per_pixel = 0.002;

        varying vec3 body_rgb;
        varying float brightness;

        void vertex() {
            vec3 center = MODEL_MATRIX[3].xyz;
            float radius = length(MODEL_MATRIX[0].xyz);
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            float dist = max(distance(center, eye), 0.001);

            // Sprite size in world units, then clamped in SCREEN units: floored so a
            // distant body stays visible, capped so a near one cannot swallow the
            // view. The disc fills the quad, so the bounds ARE the body's radius.
            float world_per_px = world_per_pixel * dist;
            float sprite = clamp(radius * sprite_scale,
                2.0 * min_pixels * world_per_px, 2.0 * max_pixels * world_per_px);

            vec3 cam_right = INV_VIEW_MATRIX[0].xyz;
            vec3 cam_up = INV_VIEW_MATRIX[1].xyz;
            vec3 world = center + cam_right * VERTEX.x * sprite + cam_up * VERTEX.y * sprite;

            // GalaxyLayout.StarFade is the tested C# twin of this line; keep them
            // together (the LightRig.TintTile precedent). INSTANCE_CUSTOM.x is the
            // normalized fitness, .y the 0->1 ignition ramp for a newly filled cell
            // (fading up from black on black space needs no transparency).
            float fade = clamp(fade_numerator / dist, 0.10, 1.0)
                * (0.35 + 0.62 * INSTANCE_CUSTOM.x);
            brightness = fade * INSTANCE_CUSTOM.y;
            body_rgb = COLOR.rgb;

            POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
        }

        void fragment() {
            // Procedural sphere on the billboard: circular cut, then a normal from
            // the disc so the body shades as a ball instead of reading flat.
            vec2 p = UV * 2.0 - 1.0;
            float r2 = dot(p, p);
            if (r2 > 1.0) {
                discard;
            }
            float nz = sqrt(1.0 - r2);
            vec3 n = normalize(vec3(p.x, -p.y, nz));
            // Stars: limb darkening, bright centre to darker rim, like a sun.
            float limb = mix(0.42, 1.0, pow(nz, 0.6));
            // Planets: a fixed upper-left key light with a soft terminator.
            float lit = mix(0.10, 1.0,
                clamp(dot(n, normalize(vec3(-0.5, 0.55, 0.7))), 0.0, 1.0));
            ALBEDO = body_rgb * brightness * mix(limb, lit, shade_directional);
        }
        """,
    };

    /// <summary>
    /// The radial falloff for things that ARE glows — the galaxy markers and the
    /// ambient sky. Bodies stopped using it in the 2026-09-17 solid pass: full core
    /// out to a third of the sprite, ~10% alpha by 60%, zero at the edge.
    /// </summary>
    public static Texture2D HaloTexture()
    {
        if (_halo is not null)
        {
            return _halo;
        }
        const int size = 128;
        const float core = 0.33f;
        // Exponent solved so alpha(0.6) == 0.1 given the core radius: the glow is
        // gone well inside the quad, which is what keeps neighbouring galaxies from
        // merging into haze.
        const float exponent = 4.46f;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha;
                if (r <= core)
                {
                    alpha = 1f;
                }
                else if (r >= 1f)
                {
                    alpha = 0f;
                }
                else
                {
                    alpha = Mathf.Pow((1f - r) / (1f - core), exponent);
                }
                image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        _halo = ImageTexture.CreateFromImage(image);
        return _halo;
    }

    /// <summary>World units per screen pixel at unit depth — the star shader's
    /// minimum-size clamp needs it, and it changes with the viewport.</summary>
    public static float WorldPerPixel(float fovDegrees, float viewportHeight) =>
        viewportHeight <= 0f
            ? 0.002f
            : 2f * Mathf.Tan(Mathf.DegToRad(fovDegrees) * 0.5f) / viewportHeight;
}
