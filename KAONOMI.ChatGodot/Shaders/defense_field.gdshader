// Background of the Defense theme, a military theme: the picture Assets/defense-field.jpg (soldiers of the French army,
// a tank, an armoured vehicle, helicopters and a dog on a field of dust, with the marks of a tactical display), set by
// the chat as the "stamp" at full width, covering the screen around the troops. The shader brings it to life gently: a
// very slow travelling (a slight zoom and pan), clouds of dust and sand drifting across the field, motes of dust floating
// in the light, the headlights of the tank and of the vehicle glowing softly. Nothing flashes. "energy" (0 to 1)
// brightens the headlights a little while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.48);    // the point of the picture kept in view when it is cut (the troops)
uniform vec4 dust : source_color = vec4(0.72, 0.6, 0.42, 1.0);
uniform vec4 light : source_color = vec4(1.0, 0.85, 0.55, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) {
        v += a * noise(p);
        p = p * 2.03 + vec2(1.7, 9.2);
        a *= 0.5;
    }
    return v;
}

// A headlight of the picture at this point: a soft glow, breathing slowly.
float headlight(vec2 tuv, vec2 at, float t, float phase) {
    float d = length((tuv - at) * vec2(picture_ratio, 1.0));
    return exp(-d * 70.0) * (0.75 + 0.25 * sin(t * 0.9 + phase));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.024);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.018), 0.008 * cos(t * 0.022)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // The headlights of the tank and of the armoured vehicle.
    float lights = headlight(tuv, vec2(0.055, 0.545), t, 0.0) + headlight(tuv, vec2(0.245, 0.548), t, 1.3)
                 + headlight(tuv, vec2(0.6, 0.548), t, 2.1) + headlight(tuv, vec2(0.672, 0.548), t, 2.9);
    color += light.rgb * lights * (0.35 + 0.25 * energy);

    // Clouds of dust and sand drifting across the field, thicker low.
    float cloud = fbm(vec2(tuv.x * 3.5 - t * 0.025, tuv.y * 2.5 + t * 0.004));
    float low = smoothstep(0.35, 0.95, tuv.y) * 0.8 + 0.2;
    color = mix(color, dust.rgb * (0.6 + 0.5 * cloud), smoothstep(0.5, 0.85, cloud) * low * 0.3);

    // Motes of dust floating in the light, drifting with the wind.
    vec2 g = vec2(p.x - t * 0.015, p.y + 0.02 * sin(p.x * 4.0 + t * 0.2)) * 55.0;
    vec2 cell = floor(g);
    if (hash(cell + 13.0) < 0.09) {
        vec2 c = cell + vec2(hash(cell), hash(cell + 1.0));
        float life = 0.5 + 0.5 * sin(t * (0.4 + hash(cell + 2.0)) + hash(cell + 3.0) * 6.28);
        color += dust.rgb * (1.0 - smoothstep(0.0, 0.14, length(g - c))) * life * 0.3;
    }

    // A vignette.
    color *= mix(1.0, 0.6, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
