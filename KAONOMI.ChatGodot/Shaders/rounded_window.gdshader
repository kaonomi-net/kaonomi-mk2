// A window of the desktop or of the stage of the voice mode: its picture with rounded corners (outside them,
// transparent: what is behind shows through), the edge smoothed over one pixel. "size" is the size of the window in
// pixels, "radius" the radius of its corners.
//
// The colours come out as the theme has them:
// - COLOR already holds the picture (the texture times the colour of the node): multiplying it by the texture again
//   squared every colour (a muted 0.7 drawn at 0.49: the texts of the dark themes dim, every window darker);
// - the picture of a transparent viewport is premultiplied (what is drawn over its transparent background is mixed
//   with black): its colour is divided by its alpha, else a translucent glass is drawn darker than its colour.
shader_type canvas_item;

uniform vec2 size = vec2(100.0, 100.0);
uniform float radius = 18.0;

void fragment() {
	float alpha = texture(TEXTURE, UV).a;
	if (alpha > 0.0)
		COLOR.rgb /= alpha;
	vec2 half_size = size * 0.5;
	vec2 corner = abs(UV * size - half_size) - (half_size - vec2(radius));
	float distance_out = length(max(corner, 0.0)) - radius;
	COLOR.a *= clamp(0.5 - distance_out, 0.0, 1.0);
}
