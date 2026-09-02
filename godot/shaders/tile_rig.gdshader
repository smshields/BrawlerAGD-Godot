// Stage light rig (backgrounds track Phase 4, 2026-09-02): tint-only ambient +
// cap-light on the platform tiles, mirroring LightRig.TintTile EXACTLY — the C#
// twin is the tested contract (outline bit-identity, contrast self-clamp). The
// near-black outline range is exempt from all tinting: the readability contract.
// Composes BEFORE the M5 shader-track effects (docs/features/render-chain.md).
shader_type canvas_item;

uniform vec3 ambient = vec3(1.0);
uniform float ambient_strength = 0.0;
uniform vec3 cap_light = vec3(1.0);
uniform float cap_strength = 0.0;
uniform float outline_max = 0.157; // 40/255
uniform float knee_low = 0.55;
uniform float knee_high = 0.85;
uniform float weather_mod = 1.0;   // ambient nudge, hard-capped by the planner

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float mx = max(c.r, max(c.g, c.b));
    if (mx > outline_max) {
        float lum = dot(c.rgb, vec3(0.299, 0.587, 0.114));
        c.rgb *= vec3(1.0) + (ambient * weather_mod - vec3(1.0)) * ambient_strength;
        float cap_t = cap_strength * smoothstep(knee_low, knee_high, lum);
        c.rgb += (cap_light - c.rgb) * cap_t;
    }
    COLOR = c;
}
