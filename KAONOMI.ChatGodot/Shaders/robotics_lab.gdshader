// Background of the Robotics theme, after a robotics lab: the picture Assets/robotics-lab.jpg (a humanoid robot hung from
// its gantry in a workshop of brick and steel, drones on the benches, tools, monitors of code and an engineer at his
// laptop), set by the chat as the "stamp" at full width, covering the screen around the robot. The shader brings it to
// life gently: a very slow travelling (a slight zoom and pan), shafts of daylight falling from the tall windows and
// breathing as clouds pass, motes of dust floating in them, and a soft blue light pulsing on the chest of the robot, as
// if it were powered on. Nothing flashes. "energy" (0 to 1) makes the robot's light brighter while Kaonomi speaks.
shader_type canvas_item;

uniform float energy = 0.0;
// The picture (Palette.Stamp, at its full width, with mipmaps).
uniform sampler2D stamp : source_color, filter_linear_mipmap, repeat_disable;
uniform float picture_ratio = 1.7917;   // width / height of the picture (2752 x 1536)
uniform vec2 focus = vec2(0.5, 0.33);    // the point of the picture kept in view when it is cut (the robot and its head)
uniform vec2 core = vec2(0.52, 0.375);   // the chest of the robot in the picture
uniform vec4 daylight : source_color = vec4(1.0, 0.97, 0.9, 1.0);
uniform vec4 power : source_color = vec4(0.3, 0.7, 1.0, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(float x) {
    float i = floor(x);
    float f = fract(x);
    return mix(fract(sin(i * 78.233) * 43758.5453), fract(sin((i + 1.0) * 78.233) * 43758.5453), f * f * (3.0 - 2.0 * f));
}

void fragment() {
    vec2 uv = SCREEN_UV;
    float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
    vec2 p = vec2(uv.x * aspect, uv.y);
    float t = TIME;

    // The picture covers the screen around its focus, never showing past its edges, with a very slow travelling.
    vec2 shown = aspect > picture_ratio ? vec2(1.0, picture_ratio / aspect) : vec2(aspect / picture_ratio, 1.0);
    shown /= 1.05 + 0.02 * sin(t * 0.021);
    vec2 centre = clamp(focus + vec2(0.012 * sin(t * 0.016), 0.008 * cos(t * 0.02)), shown * 0.5, 1.0 - shown * 0.5);
    vec2 tuv = centre + (uv - 0.5) * shown;
    vec3 color = texture(stamp, tuv).rgb;

    // Shafts of daylight from the windows above, slanting down to the right, breathing as clouds pass.
    float slant = tuv.x - tuv.y * 0.45;
    float shafts = 0.0;
    for (int i = 0; i < 4; i++) {
        float k = float(i);
        float at = 0.12 + 0.23 * k + 0.02 * sin(t * 0.05 + k);
        shafts += exp(-pow((slant - at) * 22.0, 2.0)) * (0.5 + 0.5 * noise(t * 0.12 + k * 3.7));
    }
    shafts *= smoothstep(0.95, 0.1, tuv.y);
    color = mix(color, color + daylight.rgb * 0.22, clamp(shafts, 0.0, 1.0) * 0.6);

    // Motes of dust floating in the light.
    vec2 g = vec2(p.x + 0.01 * sin(t * 0.2 + p.y * 4.0), p.y - t * 0.006) * 70.0;
    vec2 cell = floor(g);
    if (hash(cell + 9.0) < 0.06) {
        vec2 c = cell + vec2(hash(cell), hash(cell + 1.0));
        float life = 0.5 + 0.5 * sin(t * (0.5 + hash(cell + 2.0)) + hash(cell + 3.0) * 6.28);
        color += daylight.rgb * (1.0 - smoothstep(0.0, 0.12, length(g - c))) * life * (0.12 + 0.3 * clamp(shafts, 0.0, 1.0));
    }

    // The robot powered on: a soft blue light pulsing on its chest.
    float d = length((tuv - core) * vec2(picture_ratio, 1.0));
    float pulse = 0.55 + 0.45 * sin(t * 1.4);
    color += power.rgb * exp(-d * 60.0) * (0.35 + 0.35 * energy) * pulse;
    color += power.rgb * exp(-d * 18.0) * 0.05 * pulse;

    // A light vignette.
    color *= mix(1.0, 0.72, smoothstep(0.5, 1.05, distance(uv, vec2(0.5)) * 1.25));
    COLOR = vec4(color, 1.0);
}
