// Background of the Mars theme, after the rover Curiosity of NASA (Mars Science Laboratory): the picture
// Assets/mars-curiosity.jpg (the rover on the rocks of a crater, its arm on a rock, the sun low over a dust storm, the
// Earth as a small point in the sky), set by the chat as the "stamp" at full width, covering the screen around the rover.
// The shader brings it to life gently: a very slow travelling (a slight zoom and pan), veils of red dust blown across
// the ground and thicker towards the storm on the right, fine grains carried by the wind, the low sun breathing, a few
// stars twinkling softly in the sky. Nothing flashes. "energy" (0 to 1) warms the sun a little while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.48);    // the point of the picture kept in view when it is cut (the rover)
uniform vec2 sun = vec2(0.705, 0.398);   // the low sun in the picture
uniform vec4 dust : source_color = vec4(0.78, 0.45, 0.28, 1.0);
uniform vec4 glow : source_color = vec4(1.0, 0.85, 0.65, 1.0);

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

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.024);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.017), 0.008 * cos(t * 0.021)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // The low sun breathing softly.
    float sd = length((tuv - sun) * vec2(picture_ratio, 1.0));
    color += glow.rgb * exp(-sd * 14.0) * (0.12 + 0.05 * sin(t * 0.4) + 0.1 * energy);

    // A few stars twinkling in the sky (where the picture is dark, high up).
    vec2 sg = tuv * vec2(picture_ratio, 1.0) * 160.0;
    vec2 sc = floor(sg);
    float star = step(0.985, hash(sc)) * (1.0 - smoothstep(0.0, 0.12, length(fract(sg) - 0.5)));
    float sky = (1.0 - smoothstep(0.08, 0.3, tuv.y)) * (1.0 - smoothstep(0.25, 0.45, dot(color, vec3(0.33))));
    color += vec3(1.0, 0.95, 0.9) * star * sky * (0.35 + 0.35 * sin(t * (0.8 + 2.0 * hash(sc + 1.0)) + hash(sc + 2.0) * 6.28));

    // Veils of red dust blown across the ground, thicker towards the storm on the right and near the horizon.
    float veil = fbm(vec2(tuv.x * 5.0 + t * 0.05, tuv.y * 9.0 - t * 0.01));
    float thick = smoothstep(0.3, 1.0, tuv.x) * 0.6 + 0.25;
    float band = exp(-pow((tuv.y - 0.42) * 4.0, 2.0)) * 0.8 + smoothstep(0.5, 1.0, tuv.y) * 0.4;
    color = mix(color, dust.rgb * (0.7 + 0.5 * veil), smoothstep(0.45, 0.85, veil) * thick * band * 0.35);

    // Fine grains carried by the wind (from the right to the left).
    vec2 g = vec2(p.x + t * 0.04, p.y + 0.01 * sin(p.x * 5.0 + t * 0.3)) * 80.0;
    vec2 gc = floor(g);
    if (hash(gc + 9.0) < 0.05) {
        vec2 at = gc + vec2(hash(gc), hash(gc + 1.0));
        float life = 0.5 + 0.5 * sin(t * (0.6 + hash(gc + 2.0)) + hash(gc + 3.0) * 6.28);
        color += vec3(1.0, 0.75, 0.55) * (1.0 - smoothstep(0.0, 0.12, length((g - at) * vec2(0.5, 1.0)))) * life * 0.22;
    }

    // A vignette.
    color *= mix(1.0, 0.62, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
