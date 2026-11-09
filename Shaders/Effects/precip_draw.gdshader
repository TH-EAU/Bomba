shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_schlick_ggx;

// Traits de pluie et de grêle (Scripts/Effects/Precipitation.cs) : un quad 1x1 par particule, UV dans 0..1.
// Le trait part de la particule (UV.y = 0, tête, la plus claire) et s'étire vers l'arrière selon la vitesse
// relative à la caméra : au repos, des traits verticaux ; en vol, des traits qui foncent vers le joueur.
// Le trait fait toujours face à la caméra. Largeur minimale en pixels, pour rester visible de loin.
// Éclairé : la normale suit un petit cylindre calculé (pas de texture). Les particules apparaissent une à une :
// celles dont le hachage (INSTANCE_CUSTOM.w, écrit par precip_process) est sous l'intensité sont visibles.

uniform vec3 camera_velocity;
uniform float streak_time = 0.05;  // longueur du trait = vitesse relative × streak_time (s)
uniform float width = 0.02;        // largeur du trait (m)
uniform float min_px = 1.0;        // largeur minimale (pixels)
uniform vec4 color : source_color = vec4(0.75, 0.8, 0.9, 1.0);
uniform float opacity = 0.6;
uniform float intensity = 0.0;     // intensité de la précipitation (0..1), pilotée par Precipitation.cs

varying vec3 perp_v;     // direction latérale du trait (espace vue)
varying vec3 to_cam_v;   // direction vers la caméra (espace vue)
varying float hash_v;    // hachage fixe de la particule (0..1)

void vertex() {
	// Particules non locales : MODEL_MATRIX donne la position monde de la particule.
	vec3 head = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 rel_vel = INSTANCE_CUSTOM.xyz - camera_velocity;
	vec3 tail = head - rel_vel * streak_time;

	vec3 head_v = (VIEW_MATRIX * vec4(head, 1.0)).xyz;
	vec3 tail_v = (VIEW_MATRIX * vec4(tail, 1.0)).xyz;
	vec3 p = mix(head_v, tail_v, UV.y);

	vec3 dir = tail_v - head_v;
	dir = length(dir) > 1e-6 ? normalize(dir) : vec3(0.0, 1.0, 0.0);
	// Trait de vitesse parallèle au rayon de vue : pas de direction perpendiculaire ; on garde un axe fixe.
	vec3 side = cross(dir, normalize(-p));
	side = length(side) > 1e-5 ? normalize(side) : vec3(1.0, 0.0, 0.0);

	perp_v = side;
	to_cam_v = normalize(-p);
	hash_v = INSTANCE_CUSTOM.w;

	float depth = max(-p.z, 0.1);
	float px_world = 2.0 * depth / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	p += side * (UV.x - 0.5) * max(width, min_px * px_world);

	POSITION = PROJECTION_MATRIX * vec4(p, 1.0);
}

void fragment() {
	float keep = smoothstep(hash_v - 0.15, hash_v, intensity);
	float x = UV.x * 2.0 - 1.0;
	NORMAL = normalize(perp_v * x + to_cam_v * sqrt(max(1.0 - x * x, 0.0)));

	ALBEDO = color.rgb;
	ALPHA = opacity * keep * (1.0 - abs(UV.x * 2.0 - 1.0)) * mix(1.0, 0.4, UV.y);
	ROUGHNESS = 0.12;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
