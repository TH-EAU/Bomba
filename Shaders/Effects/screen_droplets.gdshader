shader_type canvas_item;

// Gouttes sur l'écran : composition. La carte d'eau (water_map, R = hauteur d'eau en pixels, B = rayon) est dessinée
// chaque image par Scripts/Effects/ScreenDroplets.cs à partir de la simulation (DropletSim.cs).
// Rien n'est peint par-dessus : tout vient de l'image.
// - Déformation : la goutte est une lentille, elle montre la scène qui l'entoure, inversée.
// - Reflet : la goutte reflète la scène autour d'elle ; seules les parties lumineuses de l'image (soleil, nuage éclairé,
//   éclair) y font un éclat, plus fort sur les bords (Fresnel). Sans lumière, pas de reflet.
// - Vitre mouillée : hors des gouttes, la scène est floue (buée, pluie) ; les sillons des gouttes qui coulent la dégagent.

uniform sampler2D water_map : filter_linear;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform float lens_extent_px = 160.0;   // rayon de la scène vue (inversée) à travers une goutte
uniform float lens_blur = 0.0;          // net : à travers la goutte, la scène est au point
uniform float normal_strength = 1.0;
uniform float reflect_extent_px = 60.0; // portée du reflet : jusqu'où la goutte va chercher la lumière autour d'elle
uniform float reflect_blur = 1.5;       // le reflet sur une surface courbe est un peu flou (niveau de mip)
uniform float spec_threshold = 0.6;     // luminance à partir de laquelle la scène reflétée fait un éclat
uniform float spec_strength = 1.2;
// Vitre mouillée : wipe_map (R = 1) garde les sillons des gouttes qui coulent, qui se referment peu à peu.
uniform sampler2D wipe_map : filter_linear;
uniform float fog_level = 0.0;        // niveau de buée (0..1), piloté par le script
uniform float fog_max = 0.7;          // opacité maximale du voile
uniform float fog_blur = 3.5;         // flou de la scène vue à travers la buée (niveau de mip)

void fragment() {
	vec2 uv = SCREEN_UV;
	vec3 col = texture(screen_texture, uv).rgb;
	vec2 px = SCREEN_PIXEL_SIZE;

	if (fog_level > 0.001) {
		float wiped = clamp(texture(wipe_map, uv).r, 0.0, 1.0);
		float fog = fog_level * fog_max * (1.0 - wiped);
		col = mix(col, textureLod(screen_texture, uv, fog_blur).rgb, fog);
	}

	float h = texture(water_map, uv).r;
	float hl = texture(water_map, uv - vec2(px.x, 0.0)).r;
	float hr = texture(water_map, uv + vec2(px.x, 0.0)).r;
	float hu = texture(water_map, uv - vec2(0.0, px.y)).r;
	float hd = texture(water_map, uv + vec2(0.0, px.y)).r;

	if (max(h, max(max(hl, hr), max(hu, hd))) > 0.001) {
		vec2 grad = vec2(hr - hl, hd - hu) * 0.5;                 // pente par pixel, vers le sommet
		vec3 n = normalize(vec3(-grad * normal_strength, 1.0));
		float a = smoothstep(0.0, 0.6, h);                        // bord antialiasé
		// Taille de la goutte (B = rayon en px) : les toutes petites (traîne, bruine) déforment moins et ne brillent pas.
		float big = smoothstep(1.5, 4.0, texture(water_map, uv).b);

		// Déformation : décalage vers le cœur, croissant vers le bord -> image inversée et réduite.
		vec2 off = -n.xy * lens_extent_px * mix(0.25, 1.0, big) * px;
		vec3 lens = textureLod(screen_texture, uv + off, lens_blur).rgb;

		// Reflet : la scène autour, vue dans la surface courbe ; seul ce qui est lumineux ressort.
		vec3 env = textureLod(screen_texture, uv + n.xy * reflect_extent_px * px, reflect_blur).rgb;
		float lum = dot(env, vec3(0.2126, 0.7152, 0.0722));
		float fresnel = 0.04 + 0.96 * pow(1.0 - n.z, 2.0);
		vec3 spec = env * smoothstep(spec_threshold, 1.0, lum) * (0.3 + 4.0 * fresnel) * spec_strength * big;

		col = mix(col, lens, a) + spec * a;
	}

	COLOR = vec4(col, 1.0);
}
