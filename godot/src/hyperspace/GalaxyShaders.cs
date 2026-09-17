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
    /// Quad half-extent as a multiple of the star's world radius.
    ///
    /// This was 20, which made a max-fitness star's sprite 48 world units across: at
    /// the warp standoff the camera sat INSIDE a glow quad wider than the viewport,
    /// and every approach washed the screen white (designer, 2026-09-16 — "light
    /// sources turn each galaxy into a massive bloom"). At 6 the quad is 3x the star's
    /// radius, with <see cref="CoreFraction"/> of it the bright core and the falloff
    /// done by the edge, so a star reads as a disc with a halo instead of a floodlight.
    /// </summary>
    public const float SpriteScale = 6f;

    /// <summary>Fraction of the sprite's half-extent that is full-alpha core. The
    /// shader needs it to size the screen clamps, and HaloTexture bakes it — they must
    /// agree, so both read it from here.</summary>
    public const float CoreFraction = 0.33f;

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
        render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

        uniform sampler2D halo : source_color, filter_linear;
        uniform float fade_numerator = 968.0;
        uniform float sprite_scale = 6.0;
        uniform float core_fraction = 0.33;
        uniform float min_pixels = 2.4;
        uniform float max_pixels = 120.0;
        // World units per screen pixel at one unit of depth: 2*tan(fov/2)/viewport_h.
        uniform float world_per_pixel = 0.002;

        varying vec3 star_rgb;
        varying float star_alpha;

        void vertex() {
            vec3 center = MODEL_MATRIX[3].xyz;
            float radius = length(MODEL_MATRIX[0].xyz);
            vec3 eye = INV_VIEW_MATRIX[3].xyz;
            float dist = max(distance(center, eye), 0.001);

            // Sprite size in world units, then clamped in SCREEN units: floored so a
            // distant star stays visible, capped so a near one cannot swallow the
            // view. Both bounds are expressed against the bright CORE, which is what
            // the eye actually measures the star by.
            float world_per_px = world_per_pixel * dist;
            float sprite = radius * sprite_scale;
            float min_sprite = 2.0 * min_pixels * world_per_px / core_fraction;
            float max_sprite = 2.0 * max_pixels * world_per_px / core_fraction;
            sprite = clamp(sprite, min_sprite, max_sprite);

            vec3 cam_right = INV_VIEW_MATRIX[0].xyz;
            vec3 cam_up = INV_VIEW_MATRIX[1].xyz;
            vec3 world = center + cam_right * VERTEX.x * sprite + cam_up * VERTEX.y * sprite;

            // GalaxyLayout.StarFade is the tested C# twin of this line; keep them
            // together (the LightRig.TintTile precedent). INSTANCE_CUSTOM.x is the
            // normalized fitness, .y the 0->1 ignition ramp for a newly filled cell.
            float fitness = INSTANCE_CUSTOM.x;
            float fade = clamp(fade_numerator / dist, 0.10, 1.0) * (0.35 + 0.62 * fitness);
            star_alpha = fade * INSTANCE_CUSTOM.y;
            star_rgb = COLOR.rgb;

            POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
        }

        void fragment() {
            float profile = texture(halo, UV).a;
            ALBEDO = star_rgb;
            ALPHA = profile * star_alpha;
        }
        """,
    };

    /// <summary>
    /// The radial falloff, generated rather than shipped as an asset so it stays in
    /// step with the numbers it encodes: full core out to 10% of the sprite radius,
    /// ~10% alpha by 30%, zero at the edge (galaxy-view.md §3).
    /// </summary>
    public static Texture2D HaloTexture()
    {
        if (_halo is not null)
        {
            return _halo;
        }
        const int size = 128;
        const float core = CoreFraction;
        // Exponent solved so alpha(0.6) == 0.1 given the core radius: the glow is
        // gone well inside the quad, which is what keeps neighbouring systems from
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
