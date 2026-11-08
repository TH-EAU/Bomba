shader_type sky;

// Ciel de crépuscule : dégradé d'élévation, cirrus peints en traits fins, soleil bas.
// Tout est procédural (aucune texture). Les couleurs suivent la référence « rose et bleu ».

uniform vec4 color_under : source_color = vec4(0.01, 0.015, 0.05, 1.0);
uniform vec4 color_horizon : source_color = vec4(0.98, 0.72, 0.86, 1.0);
uniform vec4 color_lavender : source_color = vec4(0.62, 0.58, 0.92, 1.0);
uniform vec4 color_light_blue : source_color = vec4(0.3, 0.55, 0.88, 1.0);
uniform vec4 color_blue : source_color = vec4(0.1, 0.35, 0.7, 1.0);
uniform vec4 color_night : source_color = vec4(0.02, 0.03, 0.12, 1.0);

uniform vec4 cirrus_light_blue : source_color = vec4(0.55, 0.72, 0.95, 1.0);
uniform vec4 cirrus_lavender : source_color = vec4(0.75, 0.68, 0.96, 1.0);
uniform vec4 cirrus_salmon : source_color = vec4(1.0, 0.55, 0.6, 1.0);
uniform float cirrus_opacity : hint_range(0.0, 1.0) = 0.7;
uniform float cirrus_threshold : hint_range(0.0, 1.0) = 0.52;
uniform float cirrus_softness = 0.012;
uniform float cirrus_angle = 0.45;
uniform float cirrus_scale = 1.0;

uniform vec4 sun_halo_color : source_color = vec4(1.0, 0.66, 0.5, 1.0);
uniform vec4 sun_disc_color : source_color = vec4(1.0, 0.86, 0.78, 1.0);
uniform float sun_halo_strength = 0.25;

uniform float star_strength = 0.5;
uniform float sky_energy = 1.0;

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

	// Dégradé d'élévation : rose à l'horizon, lavande vers 15°, bleu clair, bleu, bleu nuit au zénith.
	vec3 col = color_horizon.rgb;
	col = mix(col, color_lavender.rgb, smoothstep(0.0, 0.26, y));
	col = mix(col, color_light_blue.rgb, smoothstep(0.26, 0.42, y));
	col = mix(col, color_blue.rgb, smoothstep(0.42, 0.7, y));
	col = mix(col, color_night.rgb, smoothstep(0.7, 1.0, y));
	// Sous l'horizon : transition douce (le bord lointain de la mer est juste sous l'horizon).
	col = mix(color_under.rgb, col, smoothstep(-0.25, 0.0, y));

	// Soleil : halo doux pêche et disque discret, direction LIGHT0_DIRECTION (vers le soleil).
	float sun_dot = 0.0;
	if (LIGHT0_ENABLED) {
		sun_dot = dot(dir, LIGHT0_DIRECTION);
	}
	float s = max(sun_dot, 0.0);
	float halo = pow(s, 5.0) * 0.45 + pow(s, 30.0) * 0.35;
	col += sun_halo_color.rgb * halo * sun_halo_strength;
	float disc = smoothstep(0.9996, 0.9998, sun_dot);
	col = mix(col, sun_disc_color.rgb, disc);

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
