// Background of the KaonomiNet theme, after www.kaonomi.net: the picture Assets/kaonomi-net.jpg (the hooded robot of
// Kaonomi with its golden eye, its circuits of gold, the networks of agents and neurons, "Kaonomi.net · 100 % non-profit
// AGI's exploration · distributed AI · Local & Sovereign AI"), set by the chat as the "stamp" at full width, laid on the
// desk (as wide as it, centred on it: the title whole in its middle; cut above and below on a wide screen, its edges
// going on above and below on a 16:9 one). The shader
// brings it to life gently: a very slow travelling (a slight zoom and pan), the golden eye of the robot glowing and
// breathing, pulses of gold running along the circuits (only where the picture has a thin bright line), the nodes of
// the networks twinkling softly. Nothing flashes, and nothing follows the voice: "energy" (the loudness of the voice,
// set by the chat for every theme) is not used here, the picture stays as calm while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.42);    // the point of the picture kept in view when it is cut (the eye, the title)
uniform vec2 eye = vec2(0.2725, 0.318);  // the golden eye of the robot in the picture
// The desk, where the windows are (between the bar of commands and the panel of the chat): its left and right edges,
// in parts of the width of the screen; set by the chat at each change of size.
uniform vec2 desk = vec2(0.03, 0.9);
uniform vec4 gold : source_color = vec4(1.0, 0.78, 0.35, 1.0);
uniform vec4 cyan : source_color = vec4(0.45, 0.85, 1.0, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 78.233) * 43758.5453); }

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    float t = TIME;

    // The picture is laid on the desk (between the bar of commands and the panel of the chat, given by the chat as
    // "desk"): as wide as it, centred on it, so that the title is whole and in its middle whatever the screen. Taller
    // than the screen (a wide screen): cut above and below around the focus. Shorter (a 16:9 screen, 4K): centred, and
    // the colours of its edges go on above and below (and on the sides, behind the panels), blurred and a little darker,
    // with no cut. A very slow travelling (a slight zoom and pan).
    float dw = max(desk.y - desk.x, 0.2) * (1.02 + 0.015 * sin(t * 0.022));
    float dc = (desk.x + desk.y) * 0.5 + 0.004 * sin(t * 0.017);
    float h = dw * aspect / picture_ratio;
    vec2 tuv;
    tuv.x = (uv.x - dc) / dw + 0.5;
    if (h >= 1.0) {
        float cy = clamp(focus.y + 0.006 * cos(t * 0.021), 0.5 / h, 1.0 - 0.5 / h);
        tuv.y = cy + (uv.y - 0.5) / h;
    } else
        tuv.y = 0.5 + (uv.y - 0.5) / h;
    // Out of the picture: how far, and the colours of its edge there.
    float outside = max(max(-tuv.x, tuv.x - 1.0), max(-tuv.y, tuv.y - 1.0));
    float inside = step(outside, 0.0);
    vec2 edge = clamp(tuv, vec2(0.0), vec2(1.0));
    vec2 reach = vec2(tuv.x < 0.0 ? min(-tuv.x, 0.035) : (tuv.x > 1.0 ? -min(tuv.x - 1.0, 0.035) : 0.0),
                      tuv.y < 0.0 ? min(-tuv.y, 0.035) : (tuv.y > 1.0 ? -min(tuv.y - 1.0, 0.035) : 0.0));
    vec3 backdrop = textureLod(stamp, clamp(edge + reach, 0.0, 1.0), clamp(outside * 30.0, 0.0, 6.5)).rgb
                    * mix(1.0, 0.6, smoothstep(0.0, 0.7, outside));
    tuv = edge;
    vec3 color = texture(stamp, tuv).rgb;

    // The circuits: thin lines brighter than around them; warm ones (gold) take golden pulses, the others cyan ones.
    float luma = dot(color, vec3(0.3, 0.59, 0.11));
    float around = dot(textureLod(stamp, tuv, 3.0).rgb, vec3(0.3, 0.59, 0.11));
    float line = smoothstep(0.02, 0.09, luma - around);
    float warm = smoothstep(0.02, 0.12, color.r - color.b);

    // Pulses running along the rows and the columns, lighting the lines they cross.
    float speed = 0.05;
    vec2 pp = tuv * vec2(picture_ratio, 1.0);
    float rowId = floor(pp.y * 110.0);
    float colId = floor(pp.x * 110.0);
    float pulses = 0.0;
    if (hash1(rowId * 3.1) < 0.3) {
        float head = fract(t * speed * (0.6 + hash1(rowId)) + hash1(rowId * 7.0));
        float d = fract(pp.x / picture_ratio - head + 1.0);
        pulses += exp(-d * 22.0) * step(d, 0.3);
    }
    if (hash1(colId * 5.7) < 0.25) {
        float head = fract(t * speed * (0.6 + hash1(colId + 3.0)) + hash1(colId * 11.0));
        float d = fract(pp.y - head + 1.0);
        pulses += exp(-d * 22.0) * step(d, 0.3);
    }
    color += mix(cyan.rgb, gold.rgb, warm) * line * pulses * 0.9;

    // The nodes of the networks twinkling: the small bright points of the picture breathe.
    float point = smoothstep(0.08, 0.2, luma - around) * (1.0 - line * 0.5);
    float twinkle = 0.5 + 0.5 * sin(t * (0.8 + hash(floor(pp * 60.0)) * 1.6) + hash(floor(pp * 60.0) + 3.0) * 6.28);
    color += color * point * twinkle * 0.35;

    // The golden eye glowing and breathing.
    float d = length((tuv - eye) * vec2(picture_ratio, 1.0));
    float breath = 0.6 + 0.4 * sin(t * 0.9);
    color += gold.rgb * exp(-d * 55.0) * 0.35 * breath;
    color += gold.rgb * exp(-d * 14.0) * 0.06 * breath;

    // The picture over the colours of its edges, and a vignette.
    color = mix(backdrop, color, inside);
    color *= mix(1.0, 0.7, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
