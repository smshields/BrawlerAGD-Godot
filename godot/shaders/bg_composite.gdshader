// Background composite (backgrounds track Phase 1, 2026-09-02 —
// docs/background-implementation-brief.md): tilt-shift blur anchored to the arena's
// platform envelope in SCREEN space (BackgroundView updates band_top/band_bottom
// every frame from the camera transform, so zoom keeps platforms sharpest), plus the
// runtime dim + seeded brightness/contrast jitter. Crop and flip live on the sprite
// (region rect / flip_h); the remap LUT is applied CPU-side once at load
// (BackgroundBank.TextureFor) because the corpus is palette-quantized.
shader_type canvas_item;

uniform float band_top = 0.30;    // screen UV y where the focal band starts
uniform float band_bottom = 0.70; // screen UV y where the focal band ends
uniform float blur_px = 5.5;      // max blur radius in texture pixels
uniform float dim = 0.85;         // runtime dim multiplier (background never wins contrast)
uniform float falloff_dim = 0.18; // extra darkening at full defocus (depth cue, backdrop only)
uniform float brightness = 1.0;   // seeded per-stage jitter
uniform float contrast = 1.0;     // seeded per-stage jitter

void fragment() {
    // Distance from the focal band, 0 inside it, ramping to 1 at the screen edges.
    float d = 0.0;
    if (SCREEN_UV.y < band_top) {
        d = (band_top - SCREEN_UV.y) / max(band_top, 1e-4);
    } else if (SCREEN_UV.y > band_bottom) {
        d = (SCREEN_UV.y - band_bottom) / max(1.0 - band_bottom, 1e-4);
    }
    // Intensified 2026-09-10 (designer: sell the miniature harder during play):
    // full defocus arrives just past the band instead of at the screen edge.
    float s = smoothstep(0.0, 0.55, d);
    float radius = blur_px * s;

    vec4 c = texture(TEXTURE, UV);
    if (radius > 0.01) {
        vec4 acc = vec4(0.0);
        float total = 0.0;
        for (int i = -4; i <= 4; i++) {
            for (int j = -4; j <= 4; j++) {
                vec2 off = vec2(float(i), float(j)) / 4.0 * radius * TEXTURE_PIXEL_SIZE;
                float w = 1.0 / (1.0 + float(i * i + j * j));
                acc += texture(TEXTURE, UV + off) * w;
                total += w;
            }
        }
        c = acc / total;
    }
    c.rgb = (c.rgb - 0.5) * contrast + 0.5;
    // Defocused regions also fall away in value — the depth half of the
    // tilt-shift read. Backdrop-only (this shader never touches gameplay
    // elements), runtime, and shipped-tunable: not a baked vignette.
    c.rgb *= brightness * dim * (1.0 - falloff_dim * s);
    COLOR = vec4(c.rgb, c.a);
}
