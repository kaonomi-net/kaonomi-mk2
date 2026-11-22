// Background of the Kandinsky theme, after his geometric abstractions of the Bauhaus years (Composition VIII): a warm
// cream paper with a faint grain, circles with their coloured halos (the large violet one ringed in black, a blue one
// in a yellow halo, small red, yellow and blue ones scattered), straight black lines crossing the sheet, thin and thick,
// a yellow triangle edged in black, a checkerboard, arcs and a red half-disc. Everything drifts and turns very slowly,
// like a composition breathing. The shapes are stronger towards the edges, softer in the middle where the windows lie.
// "energy" (0 to 1) makes the colours brighter and the circles pulse while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 paper : source_color = vec4(0.965, 0.937, 0.874, 1.0);
uniform vec4 paper_shade : source_color = vec4(0.925, 0.894, 0.824, 1.0);
uniform vec4 ink : source_color = vec4(0.11, 0.10, 0.11, 1.0);
uniform vec4 red : source_color = vec4(0.84, 0.20, 0.16, 1.0);
uniform vec4 yellow : source_color = vec4(0.97, 0.77, 0.14, 1.0);
uniform vec4 blue : source_color = vec4(0.14, 0.32, 0.66, 1.0);
uniform vec4 violet : source_color = vec4(0.38, 0.24, 0.55, 1.0);
uniform vec4 pink : source_color = vec4(0.94, 0.62, 0.66, 1.0);
uniform vec4 turquoise : source_color = vec4(0.20, 0.62, 0.62, 1.0);
uniform float strength = 0.62;   // how much the composition shows on the paper (edges; half of it in the middle)

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float hash1(float p) { return fract(sin(p * 78.233) * 43758.5453); }

// 1 inside (d below 0), 0 outside, smoothed over a pixel.
float fill(float d, float px) { return 1.0 - smoothstep(-px, px, d); }

// 1 on a line of this half width around the edge (d = 0).
float stroke(float d, float halfWidth, float px) { return 1.0 - smoothstep(halfWidth - px, halfWidth + px, abs(d)); }

float segment(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

// Signed distance to a triangle (negative inside).
float triangle(vec2 p, vec2 p0, vec2 p1, vec2 p2) {
    vec2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    vec2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    vec2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    vec2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    vec2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x * e2.y - e0.y * e2.x);
    vec2 d = min(min(vec2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                     vec2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                     vec2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
}

vec2 rotate(vec2 v, float a) {
    float c = cos(a), s = sin(a);
    return vec2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// A point of the composition: its place on the sheet (x from 0 to 1 across the screen) with a slow drift of its own.
vec2 at(float aspect, float t, float x, float y, float k) {
    return vec2(x * aspect, y) + 0.012 * vec2(sin(t * 0.11 + k), cos(t * 0.09 + k * 1.3));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float px = SCREEN_PIXEL_SIZE.y * 1.2;
    float t = TIME;
    float glow = 1.0 + energy * 0.35;

    // The paper: cream, a little darker towards the lower right, a faint grain.
    vec3 sheet = mix(paper.rgb, paper_shade.rgb, smoothstep(0.0, 1.4, uv.x * 0.6 + uv.y * 0.8));
    sheet *= 0.985 + 0.03 * hash(floor(FRAGCOORD.xy / 2.0));
    vec3 color = sheet;

    // Checkerboard, turned, low on the right.
    {
        vec2 c = at(aspect, t, 0.86, 0.72, 4.0);
        vec2 q = rotate(p - c, 0.35 + 0.02 * sin(t * 0.07)) / 0.16 + 0.5;
        if (q.x >= 0.0 && q.x < 1.0 && q.y >= 0.0 && q.y < 1.0) {
            vec2 cellIndex = floor(q * 4.0);
            float odd = mod(cellIndex.x + cellIndex.y, 2.0);
            float pick = hash(cellIndex + 3.0);
            vec3 tile = odd > 0.5 ? ink.rgb : (pick > 0.7 ? red.rgb : (pick > 0.45 ? yellow.rgb : sheet));
            color = mix(color, tile, 0.85);
        }
    }

    // Small circles scattered, each a colour in a ring of ink.
    for (int i = 0; i < 9; i++) {
        float k = float(i);
        vec2 c = at(aspect, t, 0.08 + 0.84 * hash1(k * 3.7 + 1.0), 0.08 + 0.84 * hash1(k * 5.9 + 2.0), k * 2.1);
        float r = 0.018 + 0.03 * hash1(k * 7.3 + 4.0);
        r *= 1.0 + 0.08 * energy * sin(t * 3.0 + k);
        float d = length(p - c) - r;
        float pick = hash1(k * 11.1);
        vec3 tint = pick > 0.75 ? red.rgb : (pick > 0.5 ? yellow.rgb : (pick > 0.25 ? blue.rgb : turquoise.rgb));
        color = mix(color, tint * glow, fill(d, px) * 0.9);
        color = mix(color, ink.rgb, stroke(d, px * 0.9, px) * 0.8);
    }

    // The large circle, top left: a pink halo, a black ring, violet inside with a darker core.
    {
        vec2 c = at(aspect, t, 0.14, 0.3, 1.0);
        float r = 0.2 * (1.0 + 0.03 * sin(t * 0.25) + 0.05 * energy * sin(t * 2.0));
        float d = length(p - c);
        color = mix(color, pink.rgb, fill(d - r * 1.32, px) * 0.55);
        color = mix(color, ink.rgb, fill(d - r * 1.08, px));
        color = mix(color, violet.rgb * glow, fill(d - r, px));
        color = mix(color, ink.rgb * 0.6 + violet.rgb * 0.4, fill(d - r * 0.42, px) * 0.9);
    }

    // A blue circle in a yellow halo, top right.
    {
        vec2 c = at(aspect, t, 0.8, 0.2, 2.0);
        float r = 0.075 * (1.0 + 0.06 * energy * sin(t * 2.5));
        float d = length(p - c);
        color = mix(color, yellow.rgb, fill(d - r * 1.7, px) * 0.75);
        color = mix(color, blue.rgb * glow, fill(d - r, px));
        color = mix(color, ink.rgb, stroke(d - r, px * 1.2, px));
    }

    // A red half-disc on a line, lower left.
    {
        vec2 c = at(aspect, t, 0.24, 0.8, 3.0);
        vec2 q = rotate(p - c, -0.5 + 0.03 * sin(t * 0.13));
        float d = length(q) - 0.07;
        color = mix(color, red.rgb * glow, fill(d, px) * step(0.0, -q.y));
        color = mix(color, ink.rgb, stroke(q.y, px * 1.5, px) * step(abs(q.x), 0.1));
    }

    // The yellow triangle edged in ink, in the middle low.
    {
        float a = 0.04 * sin(t * 0.08);
        vec2 c = at(aspect, t, 0.56, 0.66, 5.0);
        vec2 q = rotate(p - c, a);
        float d = triangle(q, vec2(-0.12, 0.13), vec2(0.03, -0.17), vec2(0.13, 0.11));
        color = mix(color, yellow.rgb * glow, fill(d, px) * 0.8);
        color = mix(color, ink.rgb, stroke(d, px * 1.6, px));
    }

    // Arcs: parts of circles drawn in ink.
    for (int i = 0; i < 3; i++) {
        float k = float(i);
        vec2 c = at(aspect, t, 0.36 + 0.22 * k, 0.42 + 0.18 * sin(k * 2.0), 6.0 + k);
        float r = 0.09 + 0.05 * k;
        vec2 q = rotate(p - c, k * 1.9 + 0.05 * sin(t * 0.1 + k));
        float arc = stroke(length(q) - r, px * 1.1, px) * step(0.0, q.y) * step(q.x, r * 0.6);
        color = mix(color, ink.rgb, arc * 0.85);
    }

    // Straight lines across the sheet: a point, an angle turning very slowly, a width.
    for (int i = 0; i < 7; i++) {
        float k = float(i);
        vec2 c = at(aspect, t, 0.15 + 0.7 * hash1(k * 2.3 + 9.0), 0.15 + 0.7 * hash1(k * 4.1 + 5.0), k);
        float angle = 6.2832 * hash1(k * 6.7 + 3.0) + 0.04 * sin(t * 0.05 + k);
        vec2 dir = vec2(cos(angle), sin(angle));
        float len = 0.25 + 0.45 * hash1(k * 8.9);
        float width = (hash1(k * 12.7) > 0.75 ? 3.0 : 1.0) * px;
        float d = segment(p, c - dir * len, c + dir * len);
        color = mix(color, ink.rgb, (1.0 - smoothstep(width - px, width + px, d)) * 0.85);
    }

    // Stronger at the edges, softer in the middle, where the windows lie; the paper itself stays.
    float edge = smoothstep(0.15, 0.75, distance(uv, vec2(0.5)) * 1.3);
    float amount = clamp(strength * mix(0.5, 1.0, edge) * (1.0 + energy * 0.2), 0.0, 1.0);
    COLOR = vec4(mix(sheet, color, amount), 1.0);
}
