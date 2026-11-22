// Background of the Ghost theme, after Ghost in the Shell: the picture Assets/ghost-rooftop.jpg (a cyborg woman sitting
// on a rooftop over a megacity at night, cables at her neck, holographic fish in the rain), set by the chat as the
// "stamp" at full width, covering the screen as a wallpaper does, around her face. The shader brings it to life gently: a very slow
// travelling (a slight zoom and pan), the neons breathing each at its own pace (their glow read from a blurred copy of
// the picture, its mipmaps) and, rarely, one of them faltering; a mist drifting through the city, thicker below; thin
// rain slanting down with a faint shimmer of the air; a vignette. Nothing flashes. "energy" (0 to 1) brightens the
// neons while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.4);     // the point of the picture kept in view when it is cut (her face, the city)
uniform vec4 mist : source_color = vec4(0.55, 0.72, 0.78, 1.0);
uniform vec4 rain : source_color = vec4(0.7, 0.85, 0.95, 1.0);

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
    float t = TIME;

    // The picture covers the screen around its focus (cut above and below on a wider screen, on the sides on a
    // narrower one), never showing past its edges.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    // A very slow travelling: a slight zoom breathing and a small pan.
    float zoom = 1.05 + 0.02 * sin(t * 0.027);
    shown /= zoom;
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.019), 0.008 * cos(t * 0.023)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;

    // The air shimmers faintly in the rain.
    vec2 shimmer = vec2(noise(uv * vec2(60.0, 25.0) + vec2(0.0, -t * 1.2)), noise(uv * vec2(25.0, 60.0) + vec2(t * 0.4, 3.0))) - 0.5;
    vec3 color = texture(stamp, tuv + shimmer * 0.0012).rgb;

    // The neons breathe: the saturated, bright parts of a blurred copy glow, each region at its own pace.
    vec3 blurred = textureLod(stamp, tuv, 5.0).rgb;
    float top = max(blurred.r, max(blurred.g, blurred.b));
    float low = min(blurred.r, min(blurred.g, blurred.b));
    float neon = smoothstep(0.22, 0.55, top) * smoothstep(0.12, 0.4, top - low);
    vec2 region = floor(tuv * vec2(14.0, 8.0));
    float pace = 0.4 + 0.6 * hash(region);
    float breath = 0.55 + 0.45 * sin(t * pace + hash(region + 3.0) * 6.2832);
    // Rarely, one of them falters for a moment.
    float falter = 1.0 - 0.6 * step(0.996, hash(region + floor(t * 4.0)));
    color += blurred * neon * (0.3 * breath * falter + 0.35 * energy);

    // Mist drifting through the city, thicker towards the bottom.
    float m = fbm(vec2(uv.x * aspect * 1.6 + t * 0.015, uv.y * 3.0 - t * 0.004));
    float density = smoothstep(0.3, 1.0, uv.y) * 0.7 + 0.12;
    color = mix(color, mist.rgb * (0.35 + 0.4 * m), smoothstep(0.45, 0.85, m) * density * 0.3);

    // Thin rain slanting down.
    vec2 r = vec2(uv.x * aspect + uv.y * 0.12, uv.y);
    float column = floor(r.x * 300.0);
    float speed = 1.3 + hash(vec2(column, 1.0)) * 1.2;
    float drop = fract(r.y * 2.0 - t * speed + hash(vec2(column, 2.0)));
    float streak = step(0.75, hash(vec2(column, 3.0))) * (1.0 - smoothstep(0.0, 0.06, drop)) * step(drop, 0.06);
    color += rain.rgb * streak * 0.1;

    // A vignette.
    color *= mix(1.0, 0.6, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
