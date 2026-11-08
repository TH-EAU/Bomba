shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

uniform vec4 beam_color : source_color = vec4(0.55, 0.75, 1.0, 1.0);
uniform float intensity = 0.55;
uniform float length = 500.0;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

// Nuages (renseignés par CloudField.ApplyCloudUniforms) : le faisceau est transparent, donc dessiné
// après les nuages ; il calcule lui-même la transmittance des nuages entre la caméra et chacun de ses points.
uniform sampler3D cloud_tex : filter_linear, repeat_enable;
uniform vec4 cloud_volume = vec4(12288.0, 250.0, 4600.0, 1.8); // période, alt min, alt max, exposant vertical
uniform sampler3D giant_tex : filter_linear, repeat_enable;
uniform vec4 giant_volume = vec4(49152.0, 250.0, 14000.0, 2.2);
uniform vec2 giant_origin = vec2(0.0); // coin du domaine géant (la grille géante ne se répète pas)
uniform vec2 cloud_offset = vec2(0.0);
uniform float cloud_extinction = 0.12;
uniform bool use_clouds = true;

// Distance normalisée le long du faisceau : 0 près de la lampe, 1 au bout.
varying float along;
varying vec3 world_pos;

void vertex() {
	// Le cône est centré et orienté selon Y : le sommet étroit (+Y) est près de la lampe.
	along = clamp((0.5 * length - VERTEX.y) / length, 0.0, 1.0);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_density(sampler3D tex, vec4 vol, vec3 p) {
	if (p.y < vol.y || p.y > vol.z) return 0.0;
	vec3 uvw = vec3((p.x - cloud_offset.x) / vol.x,
	                pow((p.y - vol.y) / (vol.z - vol.y), 1.0 / vol.w),
	                (p.z - cloud_offset.y) / vol.x);
	return texture(tex, uvw).g;
}

// Grille géante : un seul exemplaire, domaine [origine, origine + période].
float giant_density(vec3 p) {
	if (p.y < giant_volume.y || p.y > giant_volume.z) return 0.0;
	vec2 l = p.xz - cloud_offset - giant_origin;
	if (l.x < 0.0 || l.y < 0.0 || l.x > giant_volume.x || l.y > giant_volume.x) return 0.0;
	vec3 uvw = vec3(l.x / giant_volume.x,
	                pow((p.y - giant_volume.y) / (giant_volume.z - giant_volume.y), 1.0 / giant_volume.w),
	                l.y / giant_volume.x);
	return texture(giant_tex, uvw).g;
}

// Union des deux grilles de CloudField (locale et géante).
float cloud_density(vec3 p) {
	return max(grid_density(cloud_tex, cloud_volume, p), giant_density(p));
}

// Transmittance des nuages sur le segment caméra -> point, limitée à la tranche d'altitude des nuages.
float cloud_transmittance(vec3 from, vec3 to) {
	vec3 d = to - from;
	float len = length(d);
	if (len < 1.0) return 1.0;
	vec3 dir = d / len;
	float t0 = 0.0, t1 = len;
	if (abs(dir.y) > 1e-4) {
		float ta = (min(cloud_volume.y, giant_volume.y) - from.y) / dir.y;
		float tb = (max(cloud_volume.z, giant_volume.z) - from.y) / dir.y;
		t0 = max(t0, min(ta, tb));
		t1 = min(t1, max(ta, tb));
	} else if (from.y < min(cloud_volume.y, giant_volume.y) || from.y > max(cloud_volume.z, giant_volume.z)) {
		return 1.0;
	}
	if (t0 >= t1) return 1.0;
	const int STEPS = 16;
	float step_len = (t1 - t0) / float(STEPS);
	float tau = 0.0;
	for (int i = 0; i < STEPS; i++) {
		float t = t0 + (float(i) + 0.5) * step_len;
		tau += cloud_density(from + dir * t) * cloud_extinction * step_len;
		if (tau > 6.0) return 0.0;
	}
	return exp(-tau);
}

void fragment() {
	float fade = pow(1.0 - along, 2.0);
	float edge = pow(abs(dot(NORMAL, VIEW)), 1.5);

	// Atténuation de proximité : le faisceau s'estompe là où il touche une surface opaque.
	float raw_depth = texture(depth_texture, SCREEN_UV).r;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
	vec4 scene_view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	scene_view.xyz /= scene_view.w;
	float scene_dist = -scene_view.z;
	float frag_dist = -VERTEX.z;
	float soft = clamp((scene_dist - frag_dist) / 3.0, 0.0, 1.0);

	float clouds = 1.0;
	if (use_clouds) {
		vec3 cam = INV_VIEW_MATRIX[3].xyz;
		clouds = cloud_transmittance(cam, world_pos);
	}

	ALBEDO = beam_color.rgb * intensity;
	ALPHA = fade * edge * soft * clouds * beam_color.a;
}
