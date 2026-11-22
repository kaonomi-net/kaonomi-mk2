// Background of the Ubuntu theme, after the wallpapers of Ubuntu: a deep aubergine turning to purple, then to the warm
// orange of Ubuntu in the lower right corner; translucent ribbons of orange, coral and violet sweeping across the
// screen in slow waves, each with a fine bright edge; soft round lights rising slowly; a faint grain. Nothing flashes.
// "energy" (0 to 1) warms and brightens the ribbons while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 aubergine_dark : source_color = vec4(0.11, 0.0, 0.075, 1.0);
uniform vec4 aubergine : source_color = vec4(0.30, 0.05, 0.27, 1.0);
uniform vec4 purple : source_color = vec4(0.47, 0.13, 0.44, 1.0);
uniform vec4 orange : source_color = vec4(0.914, 0.329, 0.125, 1.0);
uniform vec4 coral : source_color = vec4(0.98, 0.52, 0.38, 1.0);
uniform vec4 violet : source_color = vec4(0.62, 0.30, 0.78, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// A ribbon: the band between two waves; its colour mixed in (a) with a bright fine edge on its upper side.
vec4 ribbon(vec2 uv, float aspect, float t, float base, float tilt, float amplitude, float frequency, float speed,
            float phase, float width, vec3 tint, float strength) {
    float x = uv.x * aspect;
    float centre = base + tilt * (uv.x - 0.5)
                   + amplitude * sin(x * frequency + t * speed + phase)
                   + amplitude * 0.45 * sin(x * frequency * 2.3 - t * speed * 0.7 + phase * 1.7);
    float thickness = width * (0.75 + 0.25 * sin(x * frequency * 0.6 + phase + t * speed * 0.5));
    float d = (uv.y - centre) / thickness;
    // Soft body, stronger near its upper edge, fading towards the lower one.
    float body = smoothstep(-1.0, -0.85, d) * (1.0 - smoothstep(0.2, 1.0, d)) * mix(1.0, 0.35, clamp((d + 1.0) * 0.5, 0.0, 1.0));
    float edge = exp(-pow((d + 0.93) * 18.0, 2.0));
    return vec4(tint, clamp(body * strength + edge * 0.5 * strength, 0.0, 1.0));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME * (1.0 + energy * 0.6);

    // The gradient: aubergine in the upper left, purple, the orange of Ubuntu glowing in the lower right.
    float diagonal = clamp(uv.x * 0.55 + uv.y * 0.6, 0.0, 1.2);
    vec3 color = mix(aubergine_dark.rgb, aubergine.rgb, smoothstep(0.0, 0.55, diagonal));
    color = mix(color, purple.rgb, smoothstep(0.45, 0.9, diagonal) * 0.8);
    float glow = exp(-dot(uv - vec2(0.95, 1.05), uv - vec2(0.95, 1.05)) * 3.0);
    color = mix(color, orange.rgb, glow * 0.75);

    // The ribbons, from the back to the front.
    float warm = 1.0 + energy * 0.35;
    vec4 r1 = ribbon(uv, aspect, t, 0.42, 0.25, 0.05, 1.3, 0.05, 0.0, 0.16, violet.rgb, 0.22);
    color = mix(color, r1.rgb, r1.a);
    vec4 r2 = ribbon(uv, aspect, t, 0.58, 0.18, 0.06, 1.0, 0.04, 2.1, 0.2, orange.rgb * warm, 0.3);
    color = mix(color, r2.rgb, r2.a);
    vec4 r3 = ribbon(uv, aspect, t, 0.68, 0.3, 0.045, 1.6, 0.06, 4.0, 0.12, coral.rgb * warm, 0.28);
    color = mix(color, r3.rgb, r3.a);
    vec4 r4 = ribbon(uv, aspect, t, 0.8, 0.12, 0.04, 2.1, 0.035, 5.5, 0.09, mix(orange.rgb, vec3(1.0, 0.8, 0.6), 0.35) * warm, 0.24);
    color = mix(color, r4.rgb, r4.a);

    // Soft round lights rising slowly, out of focus.
    vec2 g = vec2(p.x, p.y + TIME * 0.01) * 4.0;
    vec2 cell = floor(g);
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 c = cell + vec2(float(x), float(y));
            if (hash(c + 4.0) < 0.55)
                continue;
            vec2 centre = c + vec2(hash(c), hash(c + 1.0));
            float r = 0.05 + 0.1 * hash(c + 2.0);
            float d = length(g - centre);
            float light = (1.0 - smoothstep(r * 0.6, r, d)) * (0.025 + 0.035 * hash(c + 3.0));
            light += exp(-pow((d - r * 0.8) * 30.0, 2.0)) * 0.02;
            color += mix(vec3(1.0, 0.75, 0.6), vec3(0.9, 0.7, 1.0), hash(c + 5.0)) * light;
        }
    }

    // Grain and a soft vignette.
    color += (hash(floor(FRAGCOORD.xy)) - 0.5) * 0.018;
    color *= mix(1.0, 0.7, smoothstep(0.45, 1.0, distance(uv, vec2(0.45, 0.45)) * 1.2));
    COLOR = vec4(color, 1.0);
}
