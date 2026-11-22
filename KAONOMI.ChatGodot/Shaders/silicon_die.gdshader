// Background of the Silicon theme, after microelectronics: the picture Assets/silicon-die.jpg (the die of a processor
// seen from above, its blocks of cache and logic in gold, copper and blue, on a dark teal circuit board with its traces,
// chips and components), set by the chat as the "stamp" at full width, covering the screen around the die. The shader
// brings it to life gently: a very slow travelling (a slight zoom and pan), pulses of signal running along the traces of
// the board (only where the picture has a thin bright line: the light follows the copper), and an iridescent sheen
// sweeping slowly across the die, as when a wafer turns under a light. Nothing flashes. "energy" (0 to 1) sends more
// pulses, faster, while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.5);     // the point of the picture kept in view when it is cut (the die)
uniform vec4 die_min = vec4(0.325, 0.18, 0.0, 0.0);   // the die in the picture: left, top
uniform vec4 die_max = vec4(0.675, 0.82, 0.0, 0.0);   // right, bottom
uniform vec4 signal : source_color = vec4(0.35, 0.95, 0.9, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 78.233) * 43758.5453); }

vec3 iridescence(float x) {
    return 0.5 + 0.5 * cos(6.2832 * (vec3(0.0, 0.33, 0.67) + x));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.023);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.017), 0.008 * cos(t * 0.021)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // The traces: thin lines brighter than around them (the picture against a blurred copy of it).
    float luma = dot(color, vec3(0.3, 0.59, 0.11));
    float around = dot(textureLod(stamp, tuv, 3.0).rgb, vec3(0.3, 0.59, 0.11));
    float trace = smoothstep(0.012, 0.07, luma - around);
    bool onDie = tuv.x > die_min.x && tuv.x < die_max.x && tuv.y > die_min.y && tuv.y < die_max.y;

    // Pulses of signal: short bright stretches running along the rows and the columns, lighting the traces they cross.
    float speed = 0.06 * (1.0 + energy * 1.2);
    vec2 pp = tuv * vec2(picture_ratio, 1.0);
    float rowId = floor(pp.y * 90.0);
    float colId = floor(pp.x * 90.0);
    float pulses = 0.0;
    if (hash1(rowId * 3.1) < 0.35 + 0.25 * energy) {
        float head = fract(t * speed * (0.6 + hash1(rowId)) + hash1(rowId * 7.0));
        float d = fract(pp.x / picture_ratio - head + 1.0);
        pulses += exp(-d * 22.0) * step(d, 0.3);
    }
    if (hash1(colId * 5.7) < 0.3 + 0.25 * energy) {
        float head = fract(t * speed * (0.6 + hash1(colId + 3.0)) + hash1(colId * 11.0));
        float d = fract(pp.y - head + 1.0);
        pulses += exp(-d * 22.0) * step(d, 0.3);
    }
    color += signal.rgb * trace * pulses * (onDie ? 0.2 : 1.1);

    // The iridescent sheen sweeping slowly across the die.
    if (onDie) {
        vec2 local = (tuv - die_min.xy) / (die_max.xy - die_min.xy);
        float sweep = fract(t / 26.0) * 2.6 - 0.8;
        float band = exp(-pow((local.x + local.y * 0.6 - sweep) * 3.0, 2.0));
        vec3 sheen = iridescence(local.x * 1.3 - local.y * 0.7 + t * 0.03);
        color = mix(color, color * (0.7 + 0.6 * sheen) + sheen * 0.08, band * 0.45);
    }

    // A vignette.
    color *= mix(1.0, 0.6, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
