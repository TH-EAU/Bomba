shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled, fog_disabled;

// Point d'impact de la foudre sur l'engin (Scripts/Clouds/LightningBolt.cs) : disque lumineux HDR tourné vers la caméra,
// qui masque la jonction entre le canal et la coque (sinon le canal semble coupé net).

uniform vec3 bolt_color = vec3(0.8, 0.85, 1.0);
uniform float energy = 20.0;
uniform float flash = 0.0;

void vertex() {
	// Billboard : le quad fait face à la caméra.
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}

void fragment() {
	float r = length(UV * 2.0 - 1.0);
	float core = exp(-r * r * 18.0);
	float halo = exp(-r * 5.0) * 0.25;
	float a = (core + halo) * (1.0 - smoothstep(0.85, 1.0, r));
	if (flash * a < 0.0005) discard;
	ALBEDO = mix(bolt_color, vec3(1.0), core) * energy * 0.3 * flash * a;
}
