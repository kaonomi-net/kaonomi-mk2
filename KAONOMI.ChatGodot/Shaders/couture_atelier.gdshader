// Background of the Couture theme, after the haute couture of Paris: the picture Assets/couture-sketches.jpg (the
// sketches of a collection on the table of an atelier: gowns of navy tulle, black velvet and gold embroidery, the Eiffel
// tower, swatches of fabric, patterns, scissors, quills and watercolours), set by the chat as the "stamp" at full
// width, covering the screen around the gowns. The shader brings it to life gently: a very slow travelling (a slight
// zoom and pan), a soft light passing over the paper now and then like the light of a window, and flecks of gold
// drifting down and twinkling softly, like glitter fallen from the embroideries. Nothing flashes. "energy" (0 to 1)
// makes the gold twinkle a little more while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.47, 0.43);   // the point of the picture kept in view when it is cut (the gowns and their faces)
uniform vec4 gold : source_color = vec4(0.86, 0.68, 0.32, 1.0);
uniform vec4 light : source_color = vec4(1.0, 0.96, 0.88, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.022);
    vec2 centre = clamp(focus + vec2(0.014 * sin(t * 0.016), 0.008 * cos(t * 0.02)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // A soft light passing slowly over the paper, every half minute or so.
    float pass = fract(t / 34.0);
    float band = exp(-pow((uv.x + uv.y * 0.35 - (pass * 2.4 - 0.5)) * 3.2, 2.0));
    color = mix(color, color * light.rgb * 1.12, band * 0.35);

    // Flecks of gold drifting down slowly, twinkling.
    for (int layer = 0; layer < 2; layer++) {
        float l = float(layer);
        float density = 34.0 + 30.0 * l;
        vec2 g = vec2(p.x + 0.015 * sin(t * 0.25 + p.y * 5.0 + l), p.y - t * (0.008 + 0.006 * l)) * density;
        vec2 cell = floor(g);
        if (hash(cell + 3.0 + l * 7.0) < 0.07) {
            vec2 at = cell + 0.2 + 0.6 * vec2(hash(cell + l), hash(cell + 1.0 + l));
            vec2 q = g - at;
            float twinkle = pow(0.5 + 0.5 * sin(t * (0.7 + hash(cell + 2.0) * 1.4) + hash(cell + 5.0) * 6.28), 3.0) * (0.7 + 0.5 * energy);
            float fleck = 1.0 - smoothstep(0.0, 0.09, length(q));
            // A small four-pointed glint around the brightest ones.
            float glint = (1.0 - smoothstep(0.0, 0.02, abs(q.x))) * (1.0 - smoothstep(0.0, 0.3, abs(q.y)))
                        + (1.0 - smoothstep(0.0, 0.02, abs(q.y))) * (1.0 - smoothstep(0.0, 0.3, abs(q.x)));
            color += gold.rgb * (fleck * 0.5 + glint * 0.25 * twinkle) * twinkle;
        }
    }

    // A warm vignette, like the edges of an old sheet.
    color *= mix(vec3(1.0), vec3(0.82, 0.74, 0.62), smoothstep(0.5, 1.05, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
