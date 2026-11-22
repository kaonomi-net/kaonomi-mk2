// Background of the IronMan theme, after the armour of Iron Man and the head-up display of its helmet: a dark armour of
// red plates joined by fine gold seams, with a metallic sheen sliding very slowly across it; on the left the arc reactor,
// a white-blue core in a ring of ten glowing cells, inside slowly turning rings of the HUD (ticks, arcs, a scale); on the
// right a targeting reticle drifting gently, with a scanning arc; a few columns of figures, faint horizontal lines of a
// display. Nothing flashes. "energy" (0 to 1) brightens the reactor and turns the rings a little faster while Kaonomi
// speaks.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 armour_dark : source_color = vec4(0.07, 0.02, 0.025, 1.0);
uniform vec4 armour : source_color = vec4(0.36, 0.05, 0.06, 1.0);
uniform vec4 gold : source_color = vec4(0.95, 0.70, 0.25, 1.0);
uniform vec4 reactor : source_color = vec4(0.45, 0.88, 1.0, 1.0);
uniform vec4 hud : source_color = vec4(0.55, 0.85, 1.0, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

vec2 rotate(vec2 v, float a) {
    float c = cos(a), s = sin(a);
    return vec2(c * v.x - s * v.y, s * v.x + c * v.y);
}

// 1 on a ring of this radius and half width.
float ring(float d, float radius, float halfWidth, float px) {
    return 1.0 - smoothstep(halfWidth - px, halfWidth + px, abs(d - radius));
}

// A ring cut into segments: n segments, this share of each lit, turned by "turn".
float segments(vec2 q, float n, float share, float turn) {
    float a = fract((atan(q.y, q.x) + turn) / 6.2832 * n);
    return step(a, share);
}

// A glyph of the figures: 3 x 5 blocks lit by the bits of a random number, changed slowly.
float glyph(vec2 inCell, float seed, float t) {
    float bits = hash(vec2(seed, floor(t * 0.7 + seed)));
    vec2 g = (inCell - vec2(0.15, 0.12)) / vec2(0.7, 0.76);
    if (g.x < 0.0 || g.x >= 1.0 || g.y < 0.0 || g.y >= 1.0)
        return 0.0;
    vec2 b = floor(g * vec2(3.0, 5.0));
    return step(0.5, hash(vec2(bits * 97.0, b.y * 3.0 + b.x)));
}

// The HUD around a centre: rings of ticks and arcs turning slowly.
float hudRings(vec2 q, float scale, float t, float px) {
    float d = length(q);
    float v = 0.0;
    v += ring(d, 1.0 * scale, px * 0.8, px) * 0.6;
    v += ring(d, 1.18 * scale, px * 3.0, px) * segments(q, 60.0, 0.25, t * 0.05) * 0.8;
    v += ring(d, 1.32 * scale, px * 1.2, px) * segments(q, 3.0, 0.22, -t * 0.08) ;
    v += ring(d, 1.45 * scale, px * 0.8, px) * segments(q, 120.0, 0.5, t * 0.02) * 0.5;
    v += ring(d, 1.62 * scale, px * 4.0, px) * segments(q, 4.0, 0.12, t * 0.04 + 0.4) * 0.7;
    // Long ticks every quarter.
    float a = fract(atan(q.y, q.x) / 6.2832 * 8.0 + t * 0.01);
    v += step(abs(a - 0.5), 0.004) * step(1.68 * scale, d) * step(d, 1.8 * scale) * 0.8;
    return v;
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float px = SCREEN_PIXEL_SIZE.y * 1.2;
    float t = TIME * (1.0 + energy * 0.8);

    // The armour: red plates on a slant, darker towards the edges, joined by fine gold seams.
    vec2 a = rotate(p, -0.35) * vec2(2.6, 4.2);
    vec2 plate = floor(a);
    vec2 inPlate = fract(a);
    float shade = 0.55 + 0.45 * hash(plate);
    vec3 color = mix(armour_dark.rgb, armour.rgb, shade * 0.75);
    // A bevel: lighter on the upper edge of each plate, darker on the lower one.
    color *= 1.0 + 0.25 * smoothstep(0.1, 0.0, inPlate.y) - 0.3 * smoothstep(0.9, 1.0, inPlate.y);
    vec2 edge = min(inPlate, 1.0 - inPlate) / vec2(2.6, 4.2);
    float seam = 1.0 - smoothstep(0.0, px * 1.4, min(edge.x, edge.y));
    color = mix(color, gold.rgb * 0.75, seam * 0.7);
    // A metallic sheen sliding very slowly across the plates.
    float sheen = exp(-pow((a.x * 0.35 + a.y * 0.15) - 1.5 - 4.0 * fract(TIME * 0.012), 2.0) * 3.0);
    color += vec3(1.0, 0.75, 0.6) * sheen * 0.06;
    // Darker towards the edges of the screen, a faint lighting from above.
    color *= mix(1.0, 0.5, smoothstep(0.35, 1.0, distance(uv, vec2(0.4, 0.45)) * 1.2));

    // Faint horizontal lines of a display.
    color += hud.rgb * 0.015 * step(0.5, fract(FRAGCOORD.y / 4.0));

    // The arc reactor, on the left.
    vec2 rc = vec2(0.27 * aspect, 0.5);
    vec2 q = p - rc;
    float d = length(q);
    float r = 0.11;
    float glow = 0.8 + 0.2 * energy + 0.05 * sin(TIME * 1.3);
    // A dark housing, a gold rim.
    color = mix(color, vec3(0.04, 0.04, 0.05), 1.0 - smoothstep(r * 1.18 - px, r * 1.18 + px, d));
    color = mix(color, gold.rgb, ring(d, r * 1.18, px * 3.0, px) * 0.9);
    // The ring of ten cells.
    float cells = segments(q, 10.0, 0.78, 0.0) * step(r * 0.62, d) * step(d, r * 1.02);
    color = mix(color, reactor.rgb * glow, cells * 0.85);
    color = mix(color, vec3(0.15, 0.2, 0.25), ring(d, r * 0.6, px * 2.0, px));
    // The white core and the glow around.
    color = mix(color, mix(reactor.rgb, vec3(1.0), 0.7) * glow, 1.0 - smoothstep(r * 0.42, r * 0.5, d));
    color += reactor.rgb * exp(-d / (r * 0.9)) * 0.35 * glow;
    // The rings of the HUD around it.
    color = mix(color, hud.rgb, clamp(hudRings(q, 0.17, t, px), 0.0, 1.0) * 0.65);

    // A targeting reticle on the right, drifting gently, with its scanning arc.
    vec2 tc = vec2((0.74 + 0.03 * sin(TIME * 0.07)) * aspect, 0.36 + 0.03 * sin(TIME * 0.05));
    vec2 tq = p - tc;
    float td = length(tq);
    float reticle = ring(td, 0.07, px, px) * segments(tq, 4.0, 0.7, 0.785 + TIME * 0.03);
    reticle += ring(td, 0.1, px * 0.8, px) * segments(tq, 36.0, 0.4, -TIME * 0.02) * 0.6;
    reticle += step(abs(tq.x), px) * step(0.02, abs(tq.y)) * step(abs(tq.y), 0.05);
    reticle += step(abs(tq.y), px) * step(0.02, abs(tq.x)) * step(abs(tq.x), 0.05);
    float sweep = fract(atan(tq.y, tq.x) / 6.2832 - TIME * 0.08);
    reticle += ring(td, 0.13, px * 2.5, px) * smoothstep(0.85, 1.0, sweep) * 0.8;
    color = mix(color, mix(hud.rgb, gold.rgb, 0.3), clamp(reticle, 0.0, 1.0) * 0.7);

    // Columns of figures next to the reticle and under the reactor.
    for (int i = 0; i < 2; i++) {
        vec2 origin = i == 0 ? vec2(0.82 * aspect, 0.5) : vec2(0.17 * aspect, 0.72);
        vec2 cell = (p - origin) / vec2(0.011, 0.018);
        if (cell.x >= 0.0 && cell.x < 6.0 && cell.y >= 0.0 && cell.y < 6.0) {
            vec2 ci = floor(cell);
            float on = glyph(fract(cell), ci.x * 7.0 + ci.y * 31.0 + float(i) * 101.0, TIME);
            color = mix(color, hud.rgb, on * 0.55 * step(ci.x, 2.0 + 3.0 * hash(vec2(ci.y, float(i)))));
        }
    }

    COLOR = vec4(color, 1.0);
}
