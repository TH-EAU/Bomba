shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// Canal de foudre : splines lissées et effilées, en rubans tournés vers la caméra (Scripts/Clouds/LightningBolt.cs).
// Chaque segment est un quad ; ses sommets portent :
// - VERTEX : extrémité du segment (monde, le nœud est à l'origine) ;
// - CUSTOM0 : xyz = autre extrémité, w = côté (signe) et début du segment (|w| < 1.5) ;
// - CUSTOM1 : x = temps d'arrivée du traceur (s), y = visibilité à travers les nuages devant, z = largeur (effilement).
// Additif et HDR, dessiné après les nuages : un cœur fin et très lumineux, le halo est le vrai glow de l'environnement. Les nuages, rendus en
// demi-résolution, ne peuvent pas masquer un trait aussi fin : le script estime leur atténuation (v_vis).
// La largeur a un minimum en pixels : un canal reste visible à des kilomètres.
// Le tracé est une spline Catmull-Rom effilée (tronc épais, branches fines), calculée côté CPU.

uniform float progress = 0.0;        // temps depuis le départ (s)
uniform float leader_end = 0.3;      // fin du traceur, début de l'arc en retour (s)
uniform float flash = 0.0;           // intensité de l'arc en retour (0..1), pilotée par le script
uniform float leader_level = 0.3;    // intensité du traceur déjà tracé
uniform float head_boost = 3.0;      // éclat de la tête du traceur
uniform vec3 bolt_color = vec3(0.8, 0.85, 1.0);
uniform float energy = 20.0;         // HDR : le glow de l'environnement (seuil 8) fait rayonner le canal
uniform float core_width = 2.5;      // demi-largeur du canal pour une largeur relative de 1 (m)
uniform float min_core_px = 1.0;     // demi-largeur minimale du canal (pixels)

varying float v_arrival;
varying float v_x;
varying float v_vis;

void vertex() {
	float side = sign(CUSTOM0.w);
	bool is_start = abs(CUSTOM0.w) < 1.5;
	vec3 pv = (VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 ov = (VIEW_MATRIX * vec4(CUSTOM0.xyz, 1.0)).xyz;
	vec3 dir = normalize(is_start ? ov - pv : pv - ov);
	vec3 perp = cross(dir, normalize(-pv));
	perp = length(perp) > 1e-4 ? normalize(perp) : vec3(1.0, 0.0, 0.0);

	float depth = max(-pv.z, 0.1);
	float px_world = 2.0 * depth / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float half_width = max(core_width * CUSTOM1.z, min_core_px * px_world);
	pv += perp * side * half_width;
	POSITION = PROJECTION_MATRIX * vec4(pv, 1.0);

	v_arrival = CUSTOM1.x;
	v_vis = CUSTOM1.y;
	v_x = side;
}

void fragment() {
	if (v_arrival > progress) discard;

	float x = abs(v_x);
	float head = exp(-max(progress - v_arrival, 0.0) * 25.0);
	float level = progress < leader_end ? leader_level + head_boost * head : flash;
	if (level < 0.001) discard;

	// Coeur fin et très lumineux, bord antialiasé : pas de halo dessiné, le rayonnement vient du glow (post-traitement).
	// Atténué par le nuage entre la caméra et le canal (v_vis, calculé par le script).
	ALBEDO = bolt_color * energy * level * v_vis * (1.0 - smoothstep(0.5, 1.0, x));
}
