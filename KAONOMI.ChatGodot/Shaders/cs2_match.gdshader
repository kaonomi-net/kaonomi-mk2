// Background of the CounterStrike theme, after Counter-Strike 2: the picture Assets/cs-match.jpg (a terrorist and a
// counter-terrorist fighting in a desert town between the sites A and B, a planted bomb between them, smoke and sparks),
// set by the chat as the "stamp" at full width, covering the screen around the fight. The shader brings it to life
// gently: a very slow travelling (a slight zoom and pan), the smoke drifting over it, embers and sparks rising and
// fading, dust floating in the light, the diode of the bomb pulsing softly. Nothing flashes. "energy" (0 to 1) makes
// the embers rise a little more while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.47);    // the point of the picture kept in view when it is cut (the fight, the bomb)
uniform vec2 bomb = vec2(0.625, 0.74);   // the diode of the bomb in the picture
uniform vec4 smoke : source_color = vec4(0.62, 0.62, 0.6, 1.0);
uniform vec4 ember : source_color = vec4(1.0, 0.55, 0.15, 1.0);
uniform vec4 dust : source_color = vec4(0.95, 0.85, 0.65, 1.0);

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

// Particles rising in a layer: one in some cells of a grid that climbs, each wavering and fading as it goes.
float particles(vec2 p, float density, float speed, float size, float share, float t) {
    vec2 g = vec2(p.x, p.y + t * speed) * density;
    vec2 cell = floor(g);
    if (hash(cell + 31.0) > share)
        return 0.0;
    vec2 centre = cell + vec2(0.5 + 0.35 * sin(t * 0.7 + hash(cell) * 6.28), hash(cell + 2.0));
    float life = 0.5 + 0.5 * sin(t * (0.5 + hash(cell + 4.0)) + hash(cell + 5.0) * 6.28);
    return (1.0 - smoothstep(0.0, size, length(g - centre))) * life;
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.025);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.017), 0.008 * cos(t * 0.021)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // The diode of the bomb pulsing softly.
    float led = length((tuv - bomb) * vec2(picture_ratio, 1.0));
    color += vec3(0.3, 1.0, 0.4) * exp(-led * 160.0) * (0.25 + 0.2 * sin(t * 2.0));

    // Smoke drifting slowly over the picture, thicker in the middle and low.
    float s = fbm(vec2(tuv.x * 4.0 - t * 0.02, tuv.y * 3.0 + t * 0.006));
    float where = smoothstep(0.2, 0.6, 1.0 - abs(tuv.x - 0.45) * 1.6) * smoothstep(0.15, 0.6, tuv.y);
    color = mix(color, smoke.rgb * (0.7 + 0.4 * s), smoothstep(0.5, 0.85, s) * where * 0.28);

    // Embers and sparks rising, dust floating in the light.
    float rise = 1.0 + energy * 0.8;
    float embers = particles(p, 26.0, 0.05 * rise, 0.12, 0.18, t) + particles(p + 3.7, 48.0, 0.08 * rise, 0.1, 0.12, t) * 0.8;
    color += ember.rgb * embers * smoothstep(0.1, 0.7, uv.y) * 0.9;
    float motes = particles(vec2(p.x - t * 0.01, p.y) + 9.1, 60.0, -0.004, 0.12, 0.1, t);
    color += dust.rgb * motes * 0.18;

    // A vignette.
    color *= mix(1.0, 0.62, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
