shader_type canvas_item;

// Gouttes sur la vitre, pilotées par une carte de rails (Resources/Textures/waterDropMap.jpg,
// préparée par Scripts/Effects/DropRailMap.cs) :
// - R : masque flouté = profil bombé ; les perles gonflent sur les parties épaisses et s'amincissent
//   sur les parties fines ;
// - G, B, A : phase, vitesse et seuil de présence propres à chaque rail ;
// - les perles glissent vers l'extérieur le long de leur rail (flow_time, nitro), à-coups compris,
//   et laissent une pellicule d'eau qui s'estompe ;
// - renouvellement en mouvement (renew_time) : chaque perle a un cycle de vie (impact, vie, séchage),
//   puis réapparaît ailleurs sur son rail ;
// - rendu : lentille inversée (la goutte montre la scène qui l'entoure, à l'envers), bord net antialiasé,
//   bord sombre, reflet et éclat opposé.
// Piloté par Scripts/Effects/ScreenDroplets.cs.

uniform float coverage : hint_range(0.0, 1.0) = 0.0;   // part des gouttes présentes
uniform float flow_time = 0.0;                          // avance des gouttes (nitro)
uniform float renew_time = 0.0;                         // cycles de vie (en mouvement)
uniform float streak : hint_range(0.0, 1.0) = 0.0;      // étirement pendant la glisse
uniform sampler2D rail_map : filter_linear;             // R utilisé
uniform sampler2D rail_ids : filter_nearest;            // même texture, G B A sans mélange aux bords
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform float map_scale = 1.15;         // largeur d'écran couverte par la carte
uniform float drop_spacing = 0.22;      // écart entre deux gouttes d'un même rail (largeurs d'écran)
uniform float drop_length = 0.28;       // longueur d'une perle (fraction de l'écart)
uniform float trail_length = 0.6;       // pellicule derrière la perle (fraction de l'écart)
uniform float trail_height = 0.12;
uniform float stick_slip = 0.12;        // irrégularité de la glisse
uniform float lens_extent_px = 120.0;   // rayon de la scène vue (inversée) à travers une goutte
uniform float lens_blur = 0.6;
uniform float normal_strength = 2.0;
uniform vec4 tint : source_color = vec4(0.88, 0.93, 1.0, 1.0);
uniform float highlight_strength = 1.4;
uniform float glint_strength = 0.35;
uniform vec3 light_dir = vec3(-0.45, -0.6, 0.65);   // vers la lumière (haut-gauche, vers l'écran)
uniform float rim_darken = 0.6;

float hash11(float p) {
	p = fract(p * 0.1031);
	p *= p + 33.33;
	p *= p + p;
	return fract(p);
}

// Une couche de rails (carte tournée et mise à l'échelle). p en largeurs d'écran, centré.
// Retourne la hauteur d'eau.
float layer(vec2 p, float rot, float scale, float seed) {
	float c = cos(rot), s = sin(rot);
	vec2 pr = vec2(c * p.x - s * p.y, s * p.x + c * p.y);
	vec2 muv = pr / (map_scale * scale) + 0.5;
	if (muv.x < 0.0 || muv.y < 0.0 || muv.x > 1.0 || muv.y > 1.0) {
		return 0.0;
	}
	float thick = texture(rail_map, muv).r;
	if (thick < 0.06) {
		return 0.0;
	}
	vec4 id = texture(rail_ids, muv);
	float phase = fract(id.g + seed * 0.37);
	float speed = mix(0.6, 1.4, fract(id.b + seed * 0.61));
	float spacing = drop_spacing * mix(0.75, 1.3, fract(phase * 7.3));

	// Glisse irrégulière mais toujours vers l'avant (dérivée > 0 tant que stick_slip * 2.3 * 1.4 < 1).
	float t = flow_time * speed + stick_slip * sin(flow_time * 2.3 * speed + phase * 50.0);
	float sc = length(pr) / spacing - t + phase * 13.0;
	float cell = floor(sc);
	float g = 1.0 - (sc - cell);            // distance derrière le front, en écarts
	float slot = cell * 7.13 + phase * 311.0 + seed * 59.0;

	// Cycle de vie : la perle frappe la vitre, vit, sèche, puis réapparaît ailleurs dans son emplacement.
	float life = renew_time * mix(0.7, 1.3, hash11(slot + 3.1)) + hash11(slot + 9.7);
	float k = slot + floor(life) * 17.31;
	float lp = fract(life);
	float env = smoothstep(0.0, 0.03, lp) * (1.0 - smoothstep(0.82, 1.0, lp));
	float present = clamp((coverage - mix(id.a, hash11(k), 0.5) * 0.95) / 0.12, 0.0, 1.0);
	float keep = env * present;

	float profile = smoothstep(0.06, 0.95, thick);           // section bombée du rail
	float fat = mix(0.4, 1.0, smoothstep(0.35, 0.8, thick)) * mix(0.75, 1.0, hash11(k + 1.3));
	float dl = drop_length * (1.0 + 1.2 * streak);
	float gb = g - hash11(k + 2.9) * (1.0 - dl);             // position de la perle dans l'emplacement
	float bead = smoothstep(0.0, 0.3 * dl, gb) * (1.0 - smoothstep(0.4 * dl, dl, gb));
	float film = gb > 0.4 * dl ? exp(-(gb - 0.4 * dl) / trail_length) * trail_height : 0.0;
	return max(bead * fat, film) * profile * keep;               // séchage : la perle s'estompe
}

float height(vec2 p) {
	return max(layer(p, 0.0, 1.0, 0.0), layer(p, 2.1, 0.85, 1.0));
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec3 col = texture(screen_texture, uv).rgb;

	if (coverage >= 0.001) {
		float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;   // largeur / hauteur
		vec2 p = (uv - 0.5) * vec2(1.0, 1.0 / aspect);              // 1 px = SCREEN_PIXEL_SIZE.x
		float e = SCREEN_PIXEL_SIZE.x * 1.5;
		float h = height(p);
		float hx = height(p + vec2(e, 0.0));
		float hy = height(p + vec2(0.0, e));

		if (max(h, max(hx, hy)) > 0.001) {
			vec2 grad = vec2(hx - h, hy - h) / 1.5;                   // par pixel, vers le sommet
			vec3 n = normalize(vec3(-grad * normal_strength * 10.0, 1.0));
			float a = smoothstep(0.005, 0.03, h);
			float slope = length(n.xy);                                // 0 au sommet, ~1 au bord

			// Lentille : décalage vers le cœur, croissant vers le bord -> image inversée et réduite.
			vec2 off = -n.xy * lens_extent_px * SCREEN_PIXEL_SIZE;
			vec3 lens = textureLod(screen_texture, uv + off, lens_blur).rgb * tint.rgb;
			lens *= 1.0 - rim_darken * smoothstep(0.35, 0.95, slope);

			vec3 l = normalize(light_dir);
			vec3 half_v = normalize(l + vec3(0.0, 0.0, 1.0));
			float spec = pow(max(dot(n, half_v), 0.0), 140.0) * highlight_strength;
			// Éclat doux du côté opposé à la lumière (la goutte concentre la lumière qui la traverse).
			float glint = pow(max(dot(normalize(n.xy + 1e-5), -normalize(l.xy)), 0.0), 4.0)
			            * smoothstep(0.2, 0.7, slope) * glint_strength;

			col = mix(col, lens, a) + vec3(spec + glint) * a;
		}
	}

	COLOR = vec4(col, 1.0);
}
