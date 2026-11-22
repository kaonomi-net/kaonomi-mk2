// Background of the StarCitizen theme, after the space of the game Star Citizen: a deep space of blue-black with a faint
// nebula of teal and violet, three layers of stars drifting slowly at their own speeds and twinkling softly; a star in
// the upper left with its glow and a long horizontal (anamorphic) flare; a gas giant rising in the lower right, its bands
// of pink, ochre and cream turning slowly, lit from the star with a soft terminator and a thin bright atmosphere on its
// rim; now and then a ship crossing the screen far away, its engines glowing blue; corner brackets and a small scanner
// in the cyan of a mobiGlas. Nothing flashes. "energy" (0 to 1) brightens the star and the HUD while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
uniform vec4 space_top : source_color = vec4(0.008, 0.016, 0.04, 1.0);
uniform vec4 space_low : source_color = vec4(0.02, 0.04, 0.08, 1.0);
uniform vec4 nebula_a : source_color = vec4(0.05, 0.35, 0.42, 1.0);
uniform vec4 nebula_b : source_color = vec4(0.32, 0.12, 0.45, 1.0);
uniform vec4 sun : source_color = vec4(1.0, 0.92, 0.78, 1.0);
uniform vec4 hud : source_color = vec4(0.31, 0.82, 0.91, 1.0);

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
    for (int i = 0; i < 5; i++) {
        v += a * noise(p);
        p = p * 2.02 + vec2(3.1, 1.7);
        a *= 0.5;
    }
    return v;
}

// A layer of stars: one in some cells of a grid, drifting to the left at this speed.
vec3 stars(vec2 p, float density, float speed, float size, float t, float px) {
    vec2 g = (p + vec2(t * speed, 0.0)) * density;
    vec2 cell = floor(g);
    if (hash(cell + 17.0) > 0.35)
        return vec3(0.0);
    vec2 centre = cell + 0.2 + 0.6 * vec2(hash(cell), hash(cell + 1.0));
    float d = length(g - centre) / density;
    float twinkle = 0.75 + 0.25 * sin(t * (0.8 + 1.5 * hash(cell + 2.0)) + hash(cell + 3.0) * 6.28);
    float bright = (0.4 + 0.6 * hash(cell + 4.0)) * twinkle;
    vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.85, 0.7), hash(cell + 5.0));
    return tint * bright * (1.0 - smoothstep(0.0, size * px, d));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float px = SCREEN_PIXEL_SIZE.y;
    float t = TIME;

    // Deep space and a faint nebula.
    vec3 color = mix(space_top.rgb, space_low.rgb, uv.y);
    float n = fbm(p * 1.6 + vec2(t * 0.003, 0.0));
    float n2 = fbm(p * 2.4 - vec2(0.0, t * 0.002) + 5.0);
    color += nebula_a.rgb * smoothstep(0.45, 0.85, n) * 0.35;
    color += nebula_b.rgb * smoothstep(0.5, 0.9, n2) * 0.3 * smoothstep(0.2, 0.9, uv.x);

    // Stars, the far ones slower.
    color += stars(p, 90.0, 0.002, 1.6, t, px);
    color += stars(p, 45.0, 0.005, 2.2, t, px);
    color += stars(p, 20.0, 0.01, 3.2, t, px) * 1.2;

    // The star in the upper left: its glow and a long horizontal flare.
    vec2 sc = vec2(0.12 * aspect, 0.16);
    vec2 sq = p - sc;
    float sd = length(sq);
    float shine = 1.0 + 0.3 * energy;
    color += sun.rgb * (exp(-sd * 9.0) * 0.55 + exp(-sd * 60.0) * 1.2) * shine;
    color += vec3(0.5, 0.8, 1.0) * exp(-abs(sq.y) * 260.0) * exp(-abs(sq.x) * 2.2) * 0.35 * shine;

    // The gas giant in the lower right, lit from the star.
    vec2 pc = vec2(0.86 * aspect, 1.28);
    float pr = 0.78;
    vec2 pq = p - pc;
    float pd = length(pq);
    if (pd < pr) {
        // A point of the sphere: its bands turn slowly around the axis.
        float z = sqrt(max(pr * pr - pd * pd, 0.0));
        vec3 normal = normalize(vec3(pq, z));
        float lon = atan(normal.x, normal.z) + t * 0.006;
        float lat = normal.y;
        float bands = fbm(vec2(lon * 2.0, lat * 14.0 + fbm(vec2(lon * 3.0, lat * 6.0)) * 1.5));
        vec3 surface = mix(vec3(0.85, 0.55, 0.55), vec3(0.78, 0.62, 0.42), smoothstep(0.3, 0.7, bands));
        surface = mix(surface, vec3(0.95, 0.88, 0.78), smoothstep(0.6, 0.85, bands) * 0.6);
        vec3 light = normalize(vec3(sc - pc, 0.6));
        float lit = clamp(dot(normal, light), 0.0, 1.0);
        float terminator = smoothstep(-0.05, 0.35, dot(normal, light));
        color = surface * (0.04 + 0.9 * lit * terminator);
    }
    // Its atmosphere: a thin bright rim on the lit side, a glow outside.
    float rim = exp(-abs(pd - pr) / 0.01);
    float side = clamp(dot(normalize(pq), normalize(sc - pc)) * 0.8 + 0.3, 0.0, 1.0);
    color += vec3(0.45, 0.75, 1.0) * rim * side * 0.6;
    color += vec3(0.3, 0.5, 0.9) * exp(-max(pd - pr, 0.0) / 0.05) * side * 0.15 * step(pr, pd);

    // A ship crossing far away now and then, its engines glowing blue.
    float pass = t / 75.0;
    float progress = fract(pass);
    float lane = hash(vec2(floor(pass), 7.0));
    vec2 ship = vec2((progress * 1.4 - 0.2) * aspect, 0.22 + 0.3 * lane + 0.05 * progress);
    vec2 sp = p - ship;
    sp.y += sp.x * 0.05;
    float hull = step(abs(sp.y), 0.004 * (1.0 - clamp(sp.x / 0.03, -1.0, 1.0)) * step(-0.03, sp.x) * step(sp.x, 0.03));
    color = mix(color, vec3(0.12, 0.13, 0.15), hull);
    float engine = exp(-length((sp - vec2(-0.032, 0.0)) * vec2(1.0, 3.0)) * 350.0);
    float trail = exp(-abs(sp.y) * 900.0) * smoothstep(-0.25, -0.03, sp.x) * step(sp.x, -0.03);
    color += vec3(0.4, 0.75, 1.0) * (engine * 1.5 + trail * 0.25);

    // The HUD of a mobiGlas: brackets in the corners, a small scanner in the lower left.
    float hudGlow = 0.35 + 0.15 * energy;
    vec2 m = vec2(0.025, 0.04);
    vec2 corner = min(uv, 1.0 - uv);
    float bracket = (step(abs(corner.x - m.x * 0.6), px * 1.5 / aspect * aspect) * step(corner.y, m.y + 0.04) * step(m.y * 0.6, corner.y))
                  + (step(abs(corner.y - m.y * 0.6), px * 1.5) * step(corner.x, m.x + 0.03) * step(m.x * 0.6, corner.x));
    color += hud.rgb * clamp(bracket, 0.0, 1.0) * hudGlow;
    vec2 rc = vec2(0.07 * aspect, 0.84);
    vec2 rq = p - rc;
    float rd = length(rq);
    float scanner = (1.0 - smoothstep(px, px * 2.5, abs(rd - 0.07))) * 0.7
                  + (1.0 - smoothstep(px, px * 2.0, abs(rd - 0.045))) * 0.35;
    float sweepAngle = fract(atan(rq.y, rq.x) / 6.2832 - t * 0.1);
    scanner += step(rd, 0.07) * smoothstep(0.8, 1.0, sweepAngle) * 0.25;
    scanner += step(rd, 0.07) * (step(abs(rq.x), px) + step(abs(rq.y), px)) * 0.3;
    color += hud.rgb * scanner * hudGlow;

    // A light vignette.
    color *= mix(1.0, 0.7, smoothstep(0.5, 1.0, distance(uv, vec2(0.5)) * 1.2));
    COLOR = vec4(color, 1.0);
}
