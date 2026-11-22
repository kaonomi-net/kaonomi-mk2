// Background of the MarieKilleuse theme: half kawaii, half gore, like the robot doll with a knife of the avatar.
// On the left a pastel pink sheet with white polka dots, hearts, stars and sparkles floating up and bobbing (the pink,
// the mint and the yellow of her bows and headphones); on the right, past a wavy seam stitched like a doll's (cross
// stitches), a dark crimson where the same hearts show only as dark shapes. Blood runs from the top of the screen in
// drips that grow and fall, few and short on the kawaii side, many and long on the gore side, and splatters (a blob and
// its drops) stain both, mostly the gore side. "energy" (0 to 1) makes the blood run faster and the hearts beat while
// Kaonomi speaks. Small hearts framed in white (in blood on the gore side) hold her portrait, bobbing as they drift.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 pink_light : source_color = vec4(1.0, 0.91, 0.95, 1.0);
uniform vec4 pink : source_color = vec4(1.0, 0.80, 0.88, 1.0);
uniform vec4 hot_pink : source_color = vec4(0.95, 0.36, 0.58, 1.0);
uniform vec4 mint : source_color = vec4(0.45, 0.85, 0.80, 1.0);
uniform vec4 lemon : source_color = vec4(1.0, 0.88, 0.45, 1.0);
uniform vec4 lilac : source_color = vec4(0.78, 0.68, 0.95, 1.0);
uniform vec4 gore_dark : source_color = vec4(0.11, 0.015, 0.03, 1.0);
uniform vec4 gore : source_color = vec4(0.26, 0.03, 0.06, 1.0);
uniform vec4 blood : source_color = vec4(0.56, 0.0, 0.06, 1.0);
uniform vec4 blood_light : source_color = vec4(0.85, 0.12, 0.16, 1.0);
uniform vec4 stitch : source_color = vec4(0.23, 0.10, 0.16, 1.0);
// The portrait of Marie Killeuse (Assets/mariekilleuse-face.png, set by the chat: Palette.Stamp), drawn in small hearts.
uniform sampler2D stamp : source_color, filter_linear;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 78.233) * 43758.5453); }
float dot2(vec2 v) { return dot(v, v); }

// 1 inside (d below 0), 0 outside, smoothed over a pixel.
float fill(float d, float px) { return 1.0 - smoothstep(-px, px, d); }

float segment(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

// A heart, its point at the origin, its lobes up to about y = 1 (y up).
float heart(vec2 p) {
    p.x = abs(p.x);
    if (p.y + p.x > 1.0)
        return sqrt(dot2(p - vec2(0.25, 0.75))) - 0.35355;
    return sqrt(min(dot2(p - vec2(0.0, 1.0)), dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y);
}

// A five-pointed star of radius r, its inner points at rf of it.
float star(vec2 p, float r, float rf) {
    vec2 k1 = vec2(0.809016994, -0.587785252);
    vec2 k2 = vec2(-k1.x, k1.y);
    p.x = abs(p.x);
    p -= 2.0 * max(dot(k1, p), 0.0) * k1;
    p -= 2.0 * max(dot(k2, p), 0.0) * k2;
    p.x = abs(p.x);
    p.y -= r;
    vec2 ba = rf * vec2(-k1.y, k1.x) - vec2(0.0, 1.0);
    float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
    return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
}

// A four-pointed sparkle of radius r.
float sparkle(vec2 p, float r) {
    vec2 a = abs(p) / r;
    return (sqrt(a.x) + sqrt(a.y) - 1.0) * r * 0.5;
}

vec2 rotate(vec2 v, float a) {
    float c = cos(a), s = sin(a);
    return vec2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// Where the seam is, across the screen (in the units of SCREEN_UV.x) at this height: a slanted, wavy line.
float seamAt(float y, float t) {
    return 0.6 - 0.16 * y + 0.018 * sin(y * 9.0 + t * 0.35);
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float px = SCREEN_PIXEL_SIZE.y * 1.2;
    float t = TIME;

    // Which side: 0 kawaii, 1 gore.
    float side = uv.x - seamAt(uv.y, t);
    float goreSide = smoothstep(-0.002, 0.002, side);

    // The sheets.
    vec3 kawaii = mix(pink_light.rgb, pink.rgb, smoothstep(0.0, 1.2, uv.y + uv.x * 0.3));
    vec2 dots = fract(p * 18.0 + vec2(0.5 * mod(floor(p.y * 18.0), 2.0), 0.0)) - 0.5;
    kawaii = mix(kawaii, vec3(1.0), fill(length(dots) - 0.12, px * 18.0) * 0.45);
    vec3 dark = mix(gore.rgb, gore_dark.rgb, smoothstep(0.0, 1.0, uv.y * 0.8 + (1.0 - uv.x) * 0.2));
    dark *= 0.9 + 0.1 * hash(floor(FRAGCOORD.xy / 3.0));
    vec3 color = mix(kawaii, dark, goreSide);

    // Hearts, stars and sparkles floating up, one per cell of a grid that rises.
    float rise = t * 0.025;
    vec2 g = (p + vec2(0.0, rise)) * 5.0;
    vec2 cellIndex = floor(g);
    vec2 inCell = fract(g) - 0.5;
    float seed = hash(cellIndex);
    if (seed > 0.35) {
        vec2 offset = (vec2(hash(cellIndex + 1.3), hash(cellIndex + 2.7)) - 0.5) * 0.4;
        float size = 0.12 + 0.1 * hash(cellIndex + 4.1);
        float beat = 1.0 + (0.06 + 0.12 * energy) * pow(abs(sin(t * (1.2 + energy * 3.0) + seed * 6.28)), 6.0);
        vec2 q = rotate(inCell - offset + vec2(0.0, 0.03 * sin(t * 0.8 + seed * 9.0)), 0.3 * sin(t * 0.5 + seed * 12.0));
        q.y = -q.y;
        float kind = hash(cellIndex + 7.7);
        float d;
        vec3 tint;
        if (kind < 0.5) {
            d = heart((q / (size * beat)) + vec2(0.0, 0.5)) * size * beat;
            tint = kind < 0.3 ? hot_pink.rgb : lilac.rgb;
        } else if (kind < 0.78) {
            d = star(q, size * 0.75, 0.45);
            tint = kind < 0.64 ? lemon.rgb : mint.rgb;
        } else {
            d = sparkle(q, size * 0.8);
            tint = vec3(1.0);
        }
        float w = px * 5.0;
        // Kawaii: a pastel shape with a darker edge and a white shine; gore: only a dark shape.
        vec3 shape = mix(tint, vec3(1.0), 0.15);
        shape = mix(shape, tint * 0.7, 1.0 - smoothstep(0.0, w * 1.5, -d));
        vec3 shine = mix(shape, vec3(1.0), fill(length(q - vec2(-size * 0.25, size * 0.35)) - size * 0.1, w) * step(kind, 0.78));
        vec3 kawaiiShape = shine;
        vec3 goreShape = mix(dark, blood.rgb * 0.45, 0.8);
        color = mix(color, mix(kawaiiShape, goreShape, goreSide), fill(d, w) * mix(0.95, 0.6, goreSide));
    }

    // Her portrait in small hearts framed in white (in blood on the gore side), one in some cells of a coarse grid,
    // drifting sideways and bobbing, turned a little.
    {
        vec2 hg = (p + vec2(t * 0.008, 0.0)) * 3.2 + vec2(0.5, 0.25);
        vec2 hi = floor(hg);
        float hs = hash(hi + 21.0);
        if (hs > 0.42) {
            vec2 offset = (vec2(hash(hi + 22.0), hash(hi + 23.0)) - 0.5) * 0.35;
            float size = 0.2 + 0.06 * hash(hi + 24.0);
            float beat = 1.0 + (0.03 + 0.08 * energy) * pow(abs(sin(t * (1.0 + energy * 3.0) + hs * 6.28)), 6.0);
            vec2 q = fract(hg) - 0.5 - offset + vec2(0.0, 0.025 * sin(t * 0.7 + hs * 11.0));
            q = rotate(q, 0.25 * sin(hs * 17.0) + 0.06 * sin(t * 0.4 + hs * 5.0));
            q.y = -q.y;
            float s = size * beat;
            float d = heart(q / s + vec2(0.0, 0.5)) * s;
            float w = px * 3.2;
            // Where the heart stands on the screen: on which side of the seam.
            float hx = (hi.x + 0.5 + offset.x) / 3.2 / aspect;
            float heartSide = step(seamAt((hi.y + 0.5) / 3.2, t), hx);
            // A soft shadow under it, the frame, then the portrait inside.
            float shadow = fill(heart((q + vec2(-0.012, 0.018)) / s + vec2(0.0, 0.5)) * s - s * 0.06, w * 4.0);
            color = mix(color, vec3(0.2, 0.0, 0.05), shadow * 0.25);
            vec3 frame = mix(vec3(1.0), blood_light.rgb, heartSide);
            color = mix(color, frame, fill(d - s * 0.07, w));
            vec2 tuv = vec2(0.5, 0.46) + vec2(q.x, -q.y) / (s * 2.3);
            vec4 face = texture(stamp, tuv);
            vec3 inside = mix(mix(pink_light.rgb, hot_pink.rgb, 0.25), face.rgb, face.a);
            inside = mix(inside, inside * vec3(0.75, 0.35, 0.38), heartSide * 0.55);
            color = mix(color, inside, fill(d, w));
            // A shine on the upper left lobe.
            color = mix(color, vec3(1.0), fill(length(q - vec2(-s * 0.32, s * 0.38)) - s * 0.06, w) * 0.7);
        }
    }

    // Splatters: on a coarse grid, a blob and its drops; often on the gore side, sometimes on the kawaii one.
    {
        vec2 sg = p * 3.0;
        vec2 si = floor(sg);
        float splat = 1e3;
        for (int y = -1; y <= 1; y++) {
            for (int x = -1; x <= 1; x++) {
                vec2 c = si + vec2(float(x), float(y));
                vec2 centre = c + 0.25 + 0.5 * vec2(hash(c + 5.1), hash(c + 8.3));
                float cx = centre.x / 3.0 / aspect;
                float cside = step(seamAt(centre.y / 3.0, t), cx);
                if (hash(c + 3.3) > mix(0.8, 0.3, cside)) {
                    float r = 0.07 + 0.08 * hash(c + 6.6);
                    // The blob is not round: its edge waves around it.
                    vec2 v = sg - centre;
                    float around = atan(v.y, v.x);
                    float edge = 1.0 + 0.12 * sin(around * 3.0 + hash(c + 9.9) * 6.28) + 0.09 * sin(around * 7.0 + hash(c + 1.1) * 6.28) + 0.05 * sin(around * 13.0 + hash(c + 2.2) * 6.28);
                    splat = min(splat, length(v) - r * edge);
                    for (int k = 0; k < 7; k++) {
                        float fk = float(k);
                        float a = 6.2832 * hash(c + fk * 1.7);
                        float dist = r * (1.4 + 1.8 * hash(c + fk * 2.9));
                        float dr = r * (0.12 + 0.25 * hash(c + fk * 4.3));
                        splat = min(splat, length(sg - centre - vec2(cos(a), sin(a)) * dist) - dr);
                    }
                }
            }
        }
        float m = fill(splat, px * 3.0);
        color = mix(color, mix(blood_light.rgb, blood.rgb, 0.4 + 0.4 * goreSide), m * 0.9);
    }

    // Blood running from the top: a band along the edge and a drip per column, growing then falling again.
    {
        float speedUp = 1.0 + energy * 1.5;
        float band = 0.012 + 0.008 * sin(p.x * 40.0) + 0.006 * sin(p.x * 97.0 + 1.0);
        float d = p.y - band * mix(0.6, 1.6, goreSide);
        float cw = 0.028;
        float column = floor(p.x / cw);
        float cs = hash1(column * 3.1 + 1.0);
        float cx = (column + 0.3 + 0.4 * hash1(column * 5.7)) * cw;
        float colSide = step(seamAt(0.0, t), cx / aspect);
        if (cs < mix(0.35, 0.85, colSide)) {
            float maxLen = mix(0.12, 0.6, colSide) * (0.4 + 0.6 * hash1(column * 7.9));
            float grow = fract(t * (0.03 + 0.05 * hash1(column * 9.1)) * speedUp + cs * 7.0);
            float len = maxLen * smoothstep(0.0, 0.85, grow);
            float width = cw * (0.12 + 0.1 * hash1(column * 2.2));
            // The drip narrows towards its end, a round drop hangs from it.
            float along = clamp(p.y / max(len, 0.001), 0.0, 1.0);
            float drip = segment(p, vec2(cx, 0.0), vec2(cx, len)) - width * mix(1.0, 0.65, along);
            float drop = length(p - vec2(cx, len + width * 0.6)) - width * 1.25;
            // Past its length the drip lets its drop fall.
            float falling = smoothstep(0.85, 1.0, grow);
            vec2 fall = vec2(cx, len + width * 0.6 + falling * falling * 0.8);
            drop = mix(drop, length(p - fall) - width * 1.1, step(0.85, grow));
            d = min(d, min(drip, drop));
        }
        float m = fill(d, px * 1.5);
        vec3 liquid = mix(blood.rgb, blood_light.rgb, 0.3 * (1.0 - goreSide));
        // A wet shine along the left of each drip.
        float shine = fill(abs(p.x - (cx - cw * 0.05)) - cw * 0.02, px) * step(0.03, p.y) * m * 0.35;
        color = mix(color, liquid, m);
        color = mix(color, vec3(1.0, 0.75, 0.78), shine);
    }

    // The seam: a dark line with cross stitches across it, like the seam of a doll.
    {
        float sx = seamAt(uv.y, t) * aspect;
        float line = abs(p.x - sx);
        color = mix(color, stitch.rgb, fill(line - px * 1.5, px) * 0.7);
        float k = (floor(uv.y * 36.0) + 0.5) / 36.0;
        float kx = seamAt(k, t) * aspect;
        vec2 c = vec2(kx, k);
        float a = 0.013, b = 0.009;
        float stitches = min(segment(p, c - vec2(a, b), c + vec2(a, b)), segment(p, c - vec2(a, -b), c + vec2(a, -b)));
        color = mix(color, stitch.rgb, fill(stitches - px * 2.0, px));
        color = mix(color, mix(vec3(1.0, 0.85, 0.9), vec3(0.9, 0.5, 0.55), goreSide), fill(stitches - px * 0.8, px) * 0.6);
    }

    // A soft vignette, darker on the gore side.
    color *= mix(1.0, mix(0.85, 0.55, goreSide), smoothstep(0.35, 0.95, distance(uv, vec2(0.5)) * 1.3));
    COLOR = vec4(color, 1.0);
}
