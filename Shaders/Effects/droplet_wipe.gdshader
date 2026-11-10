shader_type canvas_item;
render_mode blend_mix, unshaded;

// Passage d'une goutte qui coule dans la carte d'essuyage (Scripts/Effects/ScreenDroplets.cs) : R = 1 là où la buée a
// été essuyée. La carte n'est jamais effacée ; un voile noir translucide la ramène vers 0 chaque image (la buée se reforme).

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float d2 = dot(p, p);
	if (d2 >= 1.0) {
		discard;
	}
	COLOR = vec4(1.0, 1.0, 1.0, 1.0 - smoothstep(0.6, 1.0, d2));
}
