shader_type canvas_item;

uniform vec4 color_primary : source_color = vec4(0.856, 0.0, 0.0, 1.0); // Red
uniform vec4 color_secondary : source_color = vec4(1.0, 1.0, 1.0, 1.0); // White
uniform float stripe_frequency : hint_range(1.0, 50.0) = 8.0;
uniform float stripe_angle : hint_range(0.0, 90.0) = 45.0;

void fragment() {
	// Calculate stripe pattern using UV coordinates
	float angle_rad = radians(stripe_angle);
	float pattern = UV.x * cos(angle_rad) + UV.y * sin(angle_rad);
	float stripe = mod(pattern * stripe_frequency, 2.0);

	// Alternate between colors
	COLOR = stripe < 1.0 ? color_primary : color_secondary;
}
