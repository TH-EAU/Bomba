shader_type spatial;
// Pas de reflet de ciel PBR : en incidence rasante il rendait la mer lavande.
// On garde le reflet spéculaire direct du soleil (traînée lumineuse) et on ajoute un fresnel maîtrisé.
render_mode ambient_light_disabled;

// Mer de crépuscule (référence : mer quasi noire bleutée, aplats peints, traînée de reflet,
// points lumineux crème). Tout est procédural.

uniform vec4 base_color : source_color = vec4(0.006, 0.012, 0.04, 1.0);
uniform vec4 stroke_color : source_color = vec4(0.018, 0.04, 0.09, 1.0);
// Aplats peints : longs traits horizontaux (étirés en X).
uniform vec2 stroke_scale = vec2(0.0035, 0.022);
uniform float stroke_strength : hint_range(0.0, 1.0) = 0.85;

// Reflet du ciel approché (fresnel), teinte du crépuscule.
uniform vec4 sky_tint : source_color = vec4(0.36, 0.38, 0.66, 1.0);
uniform float sky_reflection : hint_range(0.0, 1.0) = 0.12;

// Vagues : seule la normale est perturbée ; amplitude réduite au loin (anti-scintillement).
uniform float wave_scale = 0.12;
uniform float wave_strength = 0.35;
uniform float wave_speed = 0.5;
uniform float wave_fade_distance = 250.0;
// Rugosité : faible près (reflets nets), plus forte au loin (traînée du soleil large et continue).
uniform float roughness_near : hint_range(0.0, 1.0) = 0.16;
uniform float roughness_far : hint_range(0.0, 1.0) = 0.32;

// Points lumineux fixes dans le monde.
uniform vec4 dot_color : source_color = vec4(0.95, 0.9, 0.75, 1.0);
uniform float dot_spacing = 18.0;
uniform float dot_radius = 0.45;
uniform float dot_density : hint_range(0.0, 1.0) = 0.3;
uniform float dot_brightness = 4.0;
uniform float dot_fade_start = 120.0;
uniform float dot_fade_end = 380.0;

varying vec3 world_pos;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
	           mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int k = 0; k < 4; k++) {
		s += a * value_noise(p);
		p = p * 2.03 + vec2(17.1, 9.3);
		a *= 0.5;
	}
	return s;
}

float waves(vec2 p) {
	float t = TIME * wave_speed;
	return value_noise(p + vec2(t * 0.11, t * 0.07)) * 0.6
	     + value_noise(p * 2.3 + vec2(-t * 0.13, t * 0.09)) * 0.4;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 cam = INV_VIEW_MATRIX[3].xyz;
	float dist = length(world_pos - cam);

	// Aplats peints.
	float stroke = smoothstep(0.5, 0.72, fbm(world_pos.xz * stroke_scale));
	ALBEDO = mix(base_color.rgb, stroke_color.rgb, stroke * stroke_strength);
	METALLIC = 0.0;
	SPECULAR = 0.5;
	ROUGHNESS = mix(roughness_near, roughness_far, smoothstep(40.0, 1500.0, dist));

	// Normale des vagues (différences finies), atténuée avec la distance.
	vec2 p = world_pos.xz * wave_scale;
	float e = 0.05;
	float h0 = waves(p);
	vec2 slope = vec2(waves(p + vec2(e, 0.0)) - h0, waves(p + vec2(0.0, e)) - h0) / e;
	slope *= wave_strength / (1.0 + dist / wave_fade_distance);
	vec3 n_world = normalize(vec3(-slope.x, 1.0, -slope.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);

	// Reflet du ciel approché.
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 5.0);
	vec3 emission = sky_tint.rgb * fresnel * sky_reflection;

	// Points crème : un point possible par cellule, position et présence aléatoires, scintillement.
	vec2 cell = floor(world_pos.xz / dot_spacing);
	vec2 f = world_pos.xz / dot_spacing - cell;
	float r = hash12(cell);
	vec2 site = vec2(hash12(cell + 7.3), hash12(cell + 19.1)) * 0.8 + 0.1;
	float d = length((f - site) * dot_spacing);
	float present = step(1.0 - dot_density, r);
	float disc = 1.0 - smoothstep(dot_radius * 0.4, dot_radius, d);
	float twinkle = 0.6 + 0.4 * sin(TIME * (0.8 + r * 1.7) + r * 50.0);
	float fade = 1.0 - smoothstep(dot_fade_start, dot_fade_end, dist);
	emission += dot_color.rgb * disc * present * twinkle * dot_brightness * fade;

	EMISSION = emission;
}
