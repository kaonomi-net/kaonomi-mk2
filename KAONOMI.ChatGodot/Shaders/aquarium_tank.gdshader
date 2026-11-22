// Background of the Aquarium theme, after a planted aquarium: the picture Assets/aquarium-tank.jpg (a nature aquarium seen
// from the front: driftwood and dark stones covered in moss, a small ruined arch, stem plants and red Ludwigia, a lawn of
// grass on black gravel, light falling from the surface), set by the chat as the "stamp" at full width, covering the
// screen around the middle of the tank. The shader brings it to life gently: a very slow travelling, the surface
// shimmering at the top, the plants swaying in the current, a net of light (caustics) dancing on the plants, the stones
// and the gravel, slanting rays from the surface breathing, streams of bubbles rising, specks drifting, and a small
// school of neon tetras crossing the open water now and then. Nothing flashes. "energy" (0 to 1): more bubbles, rising
// faster, while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7921;   // width / height of the picture (2000 x 1116)
uniform vec2 focus = vec2(0.52, 0.55);   // the point of the picture kept in view when it is cut (the middle of the tank)
uniform vec4 light_color : source_color = vec4(0.85, 1.0, 0.94, 1.0);

float hash(float n) { return fract(sin(n * 127.1) * 43758.5453); }

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(float x) {
    float i = floor(x);
    float f = fract(x);
    return mix(hash(i), hash(i + 1.0), f * f * (3.0 - 2.0 * f));
}

// One layer of the net of light: the crests of a grid of waves, warped twice by slow currents, so that its lines bend,
// meet and part as on the bottom of a tank.
float caustic_layer(vec2 p, float t) {
    vec2 w = p + 0.35 * vec2(sin(p.y * 1.7 + t * 0.6), sin(p.x * 1.9 - t * 0.5));
    w += 0.22 * vec2(sin(w.y * 3.1 - t * 0.9), sin(w.x * 2.7 + t * 0.8));
    return pow(1.0 - abs(sin(w.x * 3.14159)), 7.0) + pow(1.0 - abs(sin(w.y * 3.14159)), 7.0);
}

float caustics(vec2 p, float t) {
    return 0.6 * caustic_layer(p * 4.0, t) + 0.4 * caustic_layer(p * 6.5 + vec2(3.1, 1.7), t * 1.3);
}

// A neon tetra at pos heading along dir (a unit vector), seen at p (units of the height of the screen, y down): its
// colour and how much it covers. Silvery and a little see-through, a glowing blue line along its back, red under its
// rear half, a dark eye; its tail beats.
vec4 tetra(vec2 p, vec2 pos, vec2 dir, float size, float beat) {
    vec2 d = p - pos;
    // Across the fish, towards its belly: down the screen whichever way it swims.
    vec2 across = vec2(-dir.y, dir.x) * (dir.x >= 0.0 ? 1.0 : -1.0);
    vec2 q = vec2(dot(d, dir), dot(d, across)) / size;
    if (abs(q.x) > 2.0 || abs(q.y) > 1.0)
        return vec4(0.0);
    q.y += 0.18 * sin(beat) * smoothstep(-0.2, -1.6, q.x);
    float body = 1.0 - smoothstep(0.85, 1.0, length(q / vec2(1.15, 0.38)));
    float tail = step(-1.75, q.x) * step(q.x, -0.9) * (1.0 - smoothstep(0.0, 0.08, abs(q.y) - (0.06 + (-0.9 - q.x) * 0.55)));
    float shape = max(body, tail);
    if (shape <= 0.0)
        return vec4(0.0);
    vec3 col = vec3(0.6, 0.66, 0.7);
    float red = body * step(0.02, q.y) * smoothstep(0.3, -0.2, q.x);
    col = mix(col, vec3(0.92, 0.14, 0.2), red * 0.9);
    float stripe = body * (1.0 - smoothstep(0.0, 0.06, abs(q.y + 0.11) - 0.06)) * smoothstep(-1.0, -0.8, q.x) * smoothstep(0.95, 0.75, q.x);
    col = mix(col, vec3(0.25, 0.85, 1.0) * 1.3, stripe);
    float eye = 1.0 - smoothstep(0.1, 0.16, length(q - vec2(0.78, -0.05)));
    col = mix(col, vec3(0.05), eye);
    return vec4(col, shape * 0.88);
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.021);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.017), 0.008 * cos(t * 0.019)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;

    // The surface shimmers: the band at the top of the tank wavers.
    float surface = smoothstep(0.14, 0.05, tuv.y);
    tuv += surface * vec2(0.004 * sin(tuv.x * 38.0 + t * 1.4) + 0.002 * sin(tuv.x * 71.0 - t * 2.1), 0.0025 * sin(tuv.x * 23.0 - t * 1.1));
    // The plants sway in the current, the tall stems the most (where the picture, read blurred, is green); the stones,
    // the wood and the water stay still.
    vec3 blurred = textureLod(stamp, tuv, 4.0).rgb;
    float plant = smoothstep(0.03, 0.12, blurred.g - max(blurred.r, blurred.b));
    float sway = plant * smoothstep(0.95, 0.35, tuv.y);
    tuv.x += sway * (0.0028 * sin(t * 0.8 + tuv.x * 5.0 + tuv.y * 3.0) + 0.0012 * sin(t * 1.9 + tuv.y * 11.0));
    vec3 color = texture(stamp, tuv).rgb;
    float lum = dot(color, vec3(0.299, 0.587, 0.114));

    // The net of light dances on what lies under the open water (the plants, the stones, the gravel), stronger on what
    // is lit, and glints on the surface.
    vec2 cp = vec2(tuv.x * picture_ratio, tuv.y);
    float net = caustics(cp, t * 0.55);
    float under = smoothstep(0.3, 0.55, tuv.y);
    color += light_color.rgb * net * (under * (0.08 + 0.4 * lum) + surface * 0.07);

    // Slanting rays from the surface, swaying and breathing.
    float slant = tuv.x + (tuv.y - 0.08) * 0.2;
    float rays = 0.0;
    for (int i = 0; i < 5; i++) {
        float k = float(i);
        float at = 0.28 + 0.11 * k + 0.015 * sin(t * 0.07 + k * 1.3);
        rays += exp(-pow((slant - at) * (24.0 + 8.0 * hash(k)), 2.0)) * (0.35 + 0.65 * noise(t * 0.11 + k * 3.7));
    }
    rays *= smoothstep(0.85, 0.08, tuv.y);
    color += light_color.rgb * clamp(rays, 0.0, 1.0) * 0.09;

    // Specks drifting in the current, catching the light.
    vec2 g = vec2(p.x - t * 0.004 + 0.01 * sin(t * 0.15 + p.y * 3.0), p.y + 0.008 * sin(t * 0.21 + p.x * 4.0)) * 80.0;
    vec2 cell = floor(g);
    if (hash2(cell + 9.0) < 0.05) {
        vec2 c = cell + vec2(hash2(cell), hash2(cell + 1.0));
        float life = 0.5 + 0.5 * sin(t * (0.4 + hash2(cell + 2.0)) + hash2(cell + 3.0) * 6.28);
        color += light_color.rgb * (1.0 - smoothstep(0.0, 0.14, length(g - c))) * life * 0.1;
    }

    // Streams of bubbles rising to the surface from the plants (in the picture, so that they stay with it); more of them,
    // and faster, while Kaonomi speaks.
    vec2 bp = vec2(tuv.x * picture_ratio, tuv.y);
    for (int j = 0; j < 3; j++) {
        float fj = float(j);
        vec2 from = j == 0 ? vec2(0.175, 0.6) : (j == 1 ? vec2(0.895, 0.7) : vec2(0.62, 0.86));
        float rate = 0.05 + 0.02 * fj;
        for (int k = 0; k < 10; k++) {
            float fk = float(k);
            if (hash(fk + fj * 13.0) > 0.5 + 0.5 * energy)
                continue;
            float ph = fract(t * rate * (1.0 + 0.8 * energy) + fk / 10.0 + hash(fj + 4.0));
            float y = mix(from.y, 0.07, ph);
            float x = from.x + 0.006 * sin(ph * 18.0 + fk * 2.1) * ph;
            float r = (0.0032 + 0.0022 * ph) * (0.8 + 0.4 * hash(fk + fj * 10.0));
            vec2 db = bp - vec2(x * picture_ratio, y);
            float dist = length(db);
            if (dist > r * 1.5)
                continue;
            float fade = smoothstep(0.0, 0.05, ph) * (1.0 - smoothstep(0.88, 1.0, ph));
            float ring = (1.0 - smoothstep(r * 0.8, r, dist)) - 0.75 * (1.0 - smoothstep(r * 0.45, r * 0.75, dist));
            float glint = exp(-length(db + vec2(0.35, 0.35) * r) / (0.22 * r));
            color += light_color.rgb * (ring * 0.4 + glint * 0.7) * fade;
        }
    }

    // A school of neon tetras crosses the open water now and then (it turns round off the screen, out of sight).
    float s = t * 0.035;
    vec2 school = vec2((0.5 + 0.62 * sin(s)) * aspect, 0.31 + 0.05 * sin(s * 2.7) + 0.015 * sin(t * 0.3));
    vec2 heading = vec2(0.62 * cos(s) * aspect, 0.135 * cos(s * 2.7));
    heading = length(heading) > 1e-5 ? normalize(heading) : vec2(1.0, 0.0);
    for (int i = 0; i < 7; i++) {
        float fi = float(i);
        vec2 at = school + vec2((hash(fi) - 0.5) * 0.16, (hash(fi + 7.0) - 0.5) * 0.07)
                  + 0.008 * vec2(sin(t * 0.7 + fi * 1.9), sin(t * 0.9 + fi * 2.3));
        vec2 dir = normalize(heading + 0.12 * vec2(0.0, sin(t * 0.8 + fi)));
        vec4 fish = tetra(p, at, dir, 0.011 * (0.85 + 0.3 * hash(fi + 3.0)), t * 9.0 + fi * 1.7);
        color = mix(color, fish.rgb, fish.a);
    }

    // A light vignette.
    color *= mix(1.0, 0.7, smoothstep(0.5, 1.05, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
