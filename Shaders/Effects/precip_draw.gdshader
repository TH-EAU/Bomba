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

// Relief de la goutte (pluie seulement) : normal map d'une traînée (Resources/Textures/WaterDropNormal.png), recadrée sur
// la traînée (tête en bas de l'image). Elle s'ajoute au cylindre : reflets plus vivants et réfraction de la scène derrière.
uniform bool use_drop_normal = false;
uniform sampler2D drop_normal : hint_normal, filter_linear_mipmap;
uniform vec4 drop_rect = vec4(0.41, 0.91, 0.55, 0.17); // u gauche, v tête, u droite, v queue
uniform float normal_strength = 1.0;
uniform float refraction_px = 6.0;  // décalage max de la scène vue à travers la goutte (pixels)
uniform float refraction = 0.7;     // part de la scène réfractée dans la couleur de la goutte
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

varying vec3 along_v;    // direction du trait, de la tête vers la queue (espace vue)
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
	along_v = dir;
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
	if (use_drop_normal) {
		vec2 duv = vec2(mix(drop_rect.x, drop_rect.z, UV.x), mix(drop_rect.y, drop_rect.w, UV.y));
		vec2 nm = texture(drop_normal, duv).xy * 2.0 - 1.0;   // x vers la droite, y vers le haut de l'image (la queue)
		// Seulement sur les gouttes assez larges à l'écran : sur un trait d'un pixel, le relief ne fait que du bruit (pointillés).
		float across_px = 1.0 / max(fwidth(UV.x), 1e-4);
		float k = smoothstep(2.0, 5.0, across_px);
		vec3 bump = (perp_v * nm.x + along_v * nm.y) * normal_strength * k;
		NORMAL = normalize(NORMAL + bump);
		// Réfraction : la goutte montre la scène derrière, décalée par son relief (cylindre + normal map).
		// NORMAL est en espace vue (y vers le haut), SCREEN_UV a y vers le bas.
		vec3 behind = textureLod(screen_texture, SCREEN_UV - vec2(NORMAL.x, -NORMAL.y) * refraction_px / VIEWPORT_SIZE, 0.0).rgb;
		ALBEDO = color.rgb * (1.0 - refraction * k);
		EMISSION = behind * color.rgb * refraction * k;
	}
	ALPHA = opacity * keep * (1.0 - abs(UV.x * 2.0 - 1.0)) * mix(1.0, 0.4, UV.y);
	ROUGHNESS = 0.12;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
