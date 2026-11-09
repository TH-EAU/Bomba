shader_type sky;

// Ciel de crépuscule : dégradé d'élévation, cirrus peints en traits fins, soleil bas.
// Tout est procédural (aucune texture). Les couleurs suivent la référence « rose et bleu ».

uniform vec4 color_under : source_color = vec4(0.01, 0.015, 0.05, 1.0);
// Ciel éclairé (côté soleil) : bleu clair pâle à l'horizon, bleu profond au zénith.
uniform vec4 day_horizon : source_color = vec4(0.72, 0.84, 0.97, 1.0);
uniform vec4 day_zenith : source_color = vec4(0.3, 0.44, 0.62, 1.0);
// Ciel dans l'ombre (côté opposé) : bleu nuit.
uniform vec4 night_horizon : source_color = vec4(0.21, 0.33, 0.43, 1.0);
uniform vec4 night_zenith : source_color = vec4(0.31, 0.43, 0.62, 1.0);

uniform vec4 cirrus_light_blue : source_color = vec4(0.55, 0.72, 0.95, 1.0);
uniform vec4 cirrus_lavender : source_color = vec4(0.75, 0.68, 0.96, 1.0);
uniform vec4 cirrus_salmon : source_color = vec4(1.0, 0.55, 0.6, 1.0);
uniform float cirrus_opacity : hint_range(0.0, 1.0) = 0.7;
uniform float cirrus_threshold : hint_range(0.0, 1.0) = 0.52;
uniform float cirrus_softness = 0.012;
uniform float cirrus_angle = 0.45;
uniform float cirrus_scale = 0.0;

uniform vec4 sun_halo_color : source_color = vec4(1.0, 0.66, 0.5, 1.0);
uniform vec4 sun_disc_color : source_color = vec4(1.0, 0.86, 0.78, 1.0);
uniform float sun_halo_strength = 0.25;

uniform float star_strength = 0.5;
uniform float sky_energy = 1.0;

// Crépuscule (renseignés par ArenaBuilder depuis Twilight).
uniform vec2 sun_azimuth = vec2(0.0, -1.0);
uniform float sun_depression = 0.021;
uniform float earth_radius = 5.9e6;
// Hauteur angulaire (rad) du haut de l ombre de la Terre côté opposé au soleil.
uniform float shadow_top = 0.12;
uniform vec4 glow_color : source_color = vec4(1.0, 0.62, 0.52, 1.0);
uniform vec4 belt_color : source_color = vec4(0.16, 0.08, 0.2, 1.0);
uniform vec4 earth_shadow_color : source_color = vec4(0.3, 0.36, 0.62, 1.0);

float hash12(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// FBM 2D, 4 octaves au maximum (contrainte de performance).
float cirrus_fbm(vec2 p) {
	float sum = 0.0;
	float amp = 0.5;
	for (int k = 0; k < 4; k++) {
		sum += amp * value_noise(p);
		p = p * 2.03 + vec2(17.1, 9.3);
		amp *= 0.5;
	}
	return sum;
}

void sky() {
	vec3 dir = EYEDIR;
	float y = dir.y;

	// --- Crépuscule : soleil couché de sun_depression, ombre de la Terre (voir Scripts/Core/Twilight.cs) ---
	vec2 dxz = length(dir.xz) > 1e-4 ? normalize(dir.xz) : sun_azimuth;
	float facing = dot(dxz, sun_azimuth);          // 1 côté soleil, -1 côté opposé
	float to_sun = 0.5 + 0.5 * facing;
	float anti = 1.0 - to_sun;
	float e = asin(clamp(y, -1.0, 1.0));             // élévation (rad)

	// Atmosphère éclairée : côté soleil, le ciel reste bleu (diffusion de Rayleigh), pâle à l'horizon ;
	// côté opposé, il est dans l'ombre de la Terre : bleu nuit, la partie claire restant près de l'horizon.
	float lit = smoothstep(-0.3, 0.9, facing);
	vec3 day = mix(day_horizon.rgb, day_zenith.rgb, smoothstep(0.0, 0.6, e));
	vec3 night = mix(night_horizon.rgb, night_zenith.rgb, smoothstep(0.0, 0.6, e));
	vec3 col = mix(night, day, lit);

	// Lueur de couchant : seulement très bas, côté soleil.
	float glow = exp(-max(e, 0.0) / 0.07) * pow(to_sun, 4.0);
	col = mix(col, glow_color.rgb, glow * 0.45 * lit);

	// Ombre de la Terre (côté opposé) : bande bleu nuit sombre, puis ceinture de Vénus rose au-dessus.
	float shadow_band = 1.0 - smoothstep(shadow_top * 0.6, shadow_top, e);
	float belt = smoothstep(shadow_top * 0.7, shadow_top * 1.3, e) * (1.0 - smoothstep(shadow_top * 1.6, shadow_top * 3.5, e));
	col = mix(col, belt_color.rgb, belt * anti * 0.15);
	col = mix(col, night_horizon.rgb * 0.6, shadow_band * anti * 0.6);

	// Sous l'horizon : transition douce (le bord lointain de la mer est juste sous l'horizon).
	col = mix(color_under.rgb, col, smoothstep(-0.25, 0.0, y));

	// Soleil : n'apparaît que si la caméra dépasse l'ombre de la Terre (élévation apparente = abaissement
	// de l'horizon − abaissement du soleil), posé sur l'horizon plat de la mer.
	float sun_elev = sqrt(2.0 * max(POSITION.y, 0.0) / earth_radius) - sun_depression;
	vec3 sun_dir = vec3(sun_azimuth.x * cos(sun_elev), sin(sun_elev), sun_azimuth.y * cos(sun_elev));
	float sun_dot = dot(dir, sun_dir);
	float s = max(sun_dot, 0.0);
	float sun_up = smoothstep(-0.01, 0.005, sun_elev);
	col += sun_halo_color.rgb * (pow(s, 8.0) * 0.5 + pow(s, 60.0) * 0.8) * sun_halo_strength * sun_up;
	col = mix(col, sun_disc_color.rgb, smoothstep(0.99985, 0.99993, sun_dot) * sun_up * step(0.0, y));
	s = s * sun_up + pow(to_sun, 3.0) * exp(-max(e, 0.0) / 0.1) * 0.5;   // chaleur des cirrus côté soleil

	// Cirrus : projection sur un plan, étirement d'environ 8x, seuil en traits fins.
	float cirrus = 0.0;
	vec3 cirrus_col = vec3(0.0);
	if (y > 0.0) {
		vec2 plane = dir.xz / max(y, 0.05);
		float ca = cos(cirrus_angle);
		float sa = sin(cirrus_angle);
		vec2 rp = vec2(ca * plane.x - sa * plane.y, sa * plane.x + ca * plane.y);
		vec2 q = rp * cirrus_scale * vec2(0.35, 2.8);
		float n = cirrus_fbm(q);
		float line = 1.0 - smoothstep(0.0, cirrus_softness, abs(n - cirrus_threshold));
		cirrus = line * smoothstep(0.03, 0.12, y);

		cirrus_col = mix(cirrus_light_blue.rgb, cirrus_lavender.rgb, smoothstep(0.1, 0.4, y));
		float warm = clamp((1.0 - smoothstep(0.0, 0.25, y)) + pow(s, 3.0) * 0.6, 0.0, 1.0);
		cirrus_col = mix(cirrus_col, cirrus_salmon.rgb, warm);
	}
	col = mix(col, cirrus_col, cirrus * cirrus_opacity);

	// Étoiles très faibles, seulement vers le zénith.
	float star_mask = smoothstep(0.5, 0.75, y);
	vec3 cell = floor(dir * 220.0);
	float h = fract(sin(dot(cell, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
	float star = step(0.9965, h) * star_mask * star_strength;
	col += vec3(0.8, 0.85, 1.0) * star;

	COLOR = col * sky_energy;
}
