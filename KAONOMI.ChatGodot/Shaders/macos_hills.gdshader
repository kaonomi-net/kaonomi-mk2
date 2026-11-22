// Background of the MacOS theme, after the wallpapers of the recent versions of macOS: a sky going from a clear blue to
// a soft pink at the horizon, and five smooth hills one before the other, each a gradient of its own (lilac to pink,
// blue to violet, orange to coral, indigo, deep blue in front), with a fine light along its crest and a soft shadow
// under the crest of the one before it. The crests sway very slowly and the colours slide along them; a faint grain.
// Nothing flashes. "energy" (0 to 1) brightens the crests a little while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 sky_top : source_color = vec4(0.36, 0.55, 0.94, 1.0);
uniform vec4 sky_horizon : source_color = vec4(0.97, 0.75, 0.80, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// The crest of hill k at this x (0 at the top, 1 at the bottom of the screen).
float crest(float k, float x, float t) {
    float base = 0.36 + 0.13 * k;
    float frequency = 1.1 + 0.35 * k;
    float phase = k * 1.9;
    return base + 0.07 * sin(x * frequency + phase + t * (0.02 + 0.006 * k))
                + 0.035 * sin(x * frequency * 2.2 - phase * 1.3 - t * 0.015);
}

vec3 hillColor(int k, float along) {
    vec3 a, b;
    if (k == 0) { a = vec3(0.62, 0.52, 0.93); b = vec3(0.93, 0.62, 0.80); }
    else if (k == 1) { a = vec3(0.32, 0.45, 0.89); b = vec3(0.78, 0.48, 0.85); }
    else if (k == 2) { a = vec3(0.96, 0.62, 0.42); b = vec3(0.92, 0.40, 0.52); }
    else if (k == 3) { a = vec3(0.24, 0.32, 0.80); b = vec3(0.55, 0.36, 0.85); }
    else { a = vec3(0.12, 0.18, 0.55); b = vec3(0.35, 0.23, 0.68); }
    return mix(a, b, clamp(along, 0.0, 1.0));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    float x = uv.x * aspect;
    float t = TIME;
    float px = SCREEN_PIXEL_SIZE.y;

    // The sky.
    vec3 color = mix(sky_top.rgb, sky_horizon.rgb, smoothstep(0.0, 0.55, uv.y));
    color = mix(color, vec3(1.0, 0.9, 0.85), exp(-pow((uv.y - 0.4) * 5.0, 2.0)) * 0.25);

    // The hills, from the back to the front: each covers what is behind it below its crest.
    for (int i = 0; i < 5; i++) {
        float k = float(i);
        float top = crest(k, x, t);
        if (uv.y < top - 2.0 * px)
            continue;
        float inside = smoothstep(top - px * 1.5, top + px * 1.5, uv.y);
        // The colours slide slowly along the hill; deeper down it is a little darker.
        float along = uv.x * 1.1 - 0.05 + 0.08 * sin(t * 0.03 + k) + (uv.y - top) * 0.6;
        vec3 hill = hillColor(i, along);
        hill *= 1.0 - 0.25 * smoothstep(0.0, 0.5, uv.y - top);
        // A fine light along the crest, a soft shadow just under it.
        hill = mix(hill, vec3(1.0), exp(-(uv.y - top) / (px * 6.0)) * (0.35 + 0.2 * energy));
        hill *= 1.0 - 0.12 * exp(-(uv.y - top) / 0.03) * step(0.5, k);
        color = mix(color, hill, inside);
    }

    // A faint grain and a light vignette.
    color += (hash(floor(FRAGCOORD.xy)) - 0.5) * 0.012;
    color *= mix(1.0, 0.85, smoothstep(0.55, 1.0, distance(uv, vec2(0.5)) * 1.2));
    COLOR = vec4(color, 1.0);
}
