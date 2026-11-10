shader_type canvas_item;
render_mode blend_add, unshaded;

// Une goutte dans la carte d'eau (Scripts/Effects/ScreenDroplets.cs) : hauteur d'eau additive dans R (px), en calotte
// parabolique : pente 2 × dome_height au bord quel que soit le rayon. Les hauteurs s'additionnent : deux gouttes proches se rejoignent
// en douceur avant de fusionner. Quad : axe x = sens de la glisse, y = travers.
// À l'impact, la goutte est écrasée : un disque presque plat, plus large, au bord irrégulier. Elle se rassemble
// (gather 0 -> 1) : l'emprise se rétracte (côté script) et le profil passe du disque à la calotte bombée.
// Couleur d'instance : r = rayon de l'emprise (px de la carte) / 64, g = étirement de la glisse (0..1) ou, immobile, −graine,
// b = présence (apparition), a = rassemblement (0 = écrasée, 1 = bombée).
// Sortie : R = hauteur d'eau (px), B = rayon (px).

uniform float splash_thickness = 0.35;   // épaisseur (px) de la goutte écrasée
uniform float dome_height = 0.3;         // hauteur de la calotte (part du rayon) : plus bas = goutte plus plate, plus discrète

varying vec4 v_data;

void vertex() {
	v_data = COLOR;
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float stretch = max(v_data.g, 0.0);
	float seed = max(-v_data.g, 0.0) * 6.283;   // goutte immobile : g < 0 porte sa graine (festons)
	float gather = v_data.a;
	float radius = v_data.r * 64.0;

	// Goutte qui coule : tête ronde à l'avant, arrière effilé.
	float back = clamp(-p.x, 0.0, 1.0);
	float across = p.y / mix(1.0, 0.55, stretch * back);
	float d2 = p.x * p.x + across * across;
	// Écrasée : bord irrégulier (festons), qui s'arrondit en se rassemblant.
	float ang = atan(p.y, p.x);
	float lobes = 0.12 * sin(ang * 5.0 + seed) + 0.07 * sin(ang * 9.0 - seed * 1.7);
	d2 *= 1.0 + lobes * (1.0 - gather);
	if (d2 >= 1.0) {
		discard;
	}

	float dome = dome_height * radius * (1.0 - d2);
	float disc = splash_thickness * (1.0 - pow(d2, 4.0));   // plat, bord à peine relevé
	float h = mix(disc, dome, gather * gather) * v_data.b;
	// B : rayon de la goutte (px), pour que la composition traite à part les toutes petites (ni bord sombre, ni reflet).
	COLOR = vec4(h, 0.0, radius * mix(0.3, 1.0, gather) * v_data.b, 1.0);
}
