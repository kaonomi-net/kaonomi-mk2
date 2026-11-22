// Background of the Minecraft theme, after the game Minecraft: the picture Assets/minecraft-valley.jpg (a player with a
// horse and a wolf looking over a valley of blocks at sunset, a river, a village, mountains and a waterfall), set by the
// chat as the "stamp" at full width, covering the screen around the valley. The shader brings it to life gently, in
// blocks: a very slow travelling (a slight zoom and pan), flat clouds of blocks drifting across the sky, the square sun
// breathing, the river and the waterfalls shimmering in square pixels, square motes floating up in the light. Nothing
// flashes. "energy" (0 to 1) warms the sun and lifts more motes while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.45);    // the point of the picture kept in view when it is cut (the valley, the sun)
uniform vec2 sun = vec2(0.405, 0.31);    // the square sun in the picture
uniform vec4 cloud : source_color = vec4(1.0, 0.93, 0.9, 1.0);
uniform vec4 glow : source_color = vec4(1.0, 0.82, 0.5, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

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

    // The square sun breathing: a soft square glow around it.
    vec2 sd = abs(tuv - sun) * vec2(picture_ratio, 1.0);
    float square = max(sd.x, sd.y);
    color += glow.rgb * exp(-square * 9.0) * (0.12 + 0.06 * sin(t * 0.5) + 0.12 * energy);

    // The river and the waterfalls shimmer in square pixels: where the picture is blue.
    float water = smoothstep(0.08, 0.25, color.b - max(color.r, color.g * 0.95)) * smoothstep(0.42, 0.55, tuv.y + 0.2 * step(0.6, tuv.x));
    vec2 block = floor(tuv * vec2(picture_ratio, 1.0) * 220.0);
    float sparkle = step(0.93, hash(block + floor(t * 1.5 + hash(block) * 3.0)));
    color += vec3(0.55, 0.7, 1.0) * water * sparkle * 0.35;

    // Flat clouds of blocks drifting across the sky, high in the picture.
    vec2 c = vec2(tuv.x * picture_ratio * 9.0 + t * 0.05, tuv.y * 28.0);
    vec2 cell = floor(c);
    float row = cell.y;
    float inCloud = step(0.72, hash(vec2(floor(cell.x / 3.0), row))) * step(0.5, hash(vec2(cell.x, row + 0.5)) + 0.4);
    float sky = 1.0 - smoothstep(0.12, 0.3, tuv.y);
    color = mix(color, cloud.rgb, inCloud * sky * 0.35);

    // Square motes floating up in the light.
    vec2 g = vec2(p.x + 0.01 * sin(t * 0.3 + p.y * 6.0), p.y + t * (0.012 + 0.01 * energy)) * 45.0;
    vec2 gc = floor(g);
    if (hash(gc + 7.0) < 0.06) {
        vec2 at = gc + vec2(0.3) + 0.4 * vec2(hash(gc), hash(gc + 1.0));
        vec2 q = abs(g - at);
        float mote = step(max(q.x, q.y), 0.07);
        float life = 0.5 + 0.5 * sin(t * (0.5 + hash(gc + 2.0)) + hash(gc + 3.0) * 6.28);
        color += mix(glow.rgb, vec3(0.75, 1.0, 0.55), hash(gc + 4.0)) * mote * life * 0.45;
    }

    // A vignette.
    color *= mix(1.0, 0.65, smoothstep(0.45, 1.0, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
