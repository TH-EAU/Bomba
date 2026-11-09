shader_type sky;
render_mode use_quarter_res_pass;

// Ciel physique : diffusion de Rayleigh, de Mie et absorption par l'ozone, et cirrus texturés.
// Adapté de « Sky++ (sorta) » d'Ansol (godotshaders.com, CC0), lui-même basé sur
// « Rayleigh, Mie, Ozone based atmospheric scatter » (port de MinimalAtmosphere, de Fewes).
// Structure reprise : la diffusion est calculée dans le cubemap de radiance (recalculé seulement quand
// le ciel change), la transmittance en quart de résolution, et l'écran ne fait que lire.
// Adaptations Bomba :
// - le soleil vient de Twilight (paramètres) et non de LIGHT0 : TwilightSun est masqué dans l'ombre de la Terre ;
// - la diffusion utilise un soleil à hauteur fixe (scatter_sun_elevation_deg) : le cubemap reste statique ;
// - ombre de la Terre dans la diffusion (soleil sous l'horizon) et diffusion multiple approchée (ciel de nuit) :
//   l'air non éclairé s'assombrit vers le bleu de la nuit, la lumière rasante rougie y ajoute un dégradé mauve ;
// - le disque solaire est à l'élévation apparente fournie par TwilightSun (sun_disc_elevation), visible
//   seulement au-dessus de l'ombre de la Terre, comme l'ancien ciel (sky_dusk) ;
// - teintes poussées vers le rose et le violet (rayleigh_tint, mie_tint, ozone) ;
// - pas de cumulus (nuages volumétriques du jeu), ni lunes, ni arc-en-ciel ; étoiles procédurales.

const float PLANET_RADIUS = 6371000.0;
const vec3 PLANET_CENTER = vec3(0.0, -PLANET_RADIUS, 0.0);
const float ATMOSPHERE_HEIGHT = 100000.0;
const float RAYLEIGH_HEIGHT = (ATMOSPHERE_HEIGHT * 0.08);
const float MIE_HEIGHT = (ATMOSPHERE_HEIGHT * 0.012);

const vec3 C_RAYLEIGH = vec3(5.802, 13.558, 33.100) * 1e-6;
const vec3 C_MIE = vec3(3.996, 3.996, 3.996) * 1e-6;
const vec3 C_OZONE = vec3(0.650, 1.881, 0.085) * 1e-6;

uniform bool use_debanding = true;

group_uniforms Atmosphere;
uniform int atmosphere_sample_count : hint_range(8, 64) = 48;
uniform int optical_depth_samples : hint_range(4, 32) = 16;
uniform float elevation = 0.0;            // altitude (m) du point de vue de la diffusion
uniform float rayleigh_strength : hint_range(0.0, 5.0) = 1.0;
uniform float mie_strength : hint_range(0.0, 5.0) = 1.5;
uniform float ozone_strength : hint_range(0.0, 5.0) = 2.2;
// Teinte de la diffusion (rouge renforcé : ciel plus rose ; bleu léger : violet au zénith).
uniform vec3 rayleigh_tint = vec3(0.97, 0.97, 1.1);
uniform vec3 mie_tint = vec3(1.0, 0.72, 0.88);
uniform float mie_anisotropy_factor = 0.75;
uniform float mie_back_anisotropy = -0.4;
uniform float mie_back_weight : hint_range(0.0, 1.0) = 0.3;
uniform float atmosphere_density : hint_range(0.0, 5.0) = 1.0;
uniform float exposure = 22.0;
uniform vec4 color_under : source_color = vec4(0.01, 0.015, 0.05, 1.0);

group_uniforms Sun;
uniform vec2 sun_azimuth = vec2(0.0, -1.0);
uniform float scatter_sun_elevation_deg = -1.5;  // hauteur du soleil pour les couleurs (négatif = couché)
uniform vec3 sun_light_color = vec3(1.0, 0.92, 0.95);
uniform float sun_disc_elevation = -1.0;          // élévation apparente (rad), renseignée par TwilightSun
uniform float sun_disc_size = 0.0093;             // diamètre apparent (rad)
uniform float sun_disc_feather : hint_range(0.0, 1.0) = 0.5;
uniform float sundisc_intensity = 30.0;

group_uniforms Night;
// Diffusion multiple approchée : lumière d'ambiance diffusée par l'air le long du regard, que le soleil
// éclaire ou non. Dans l'ombre de la Terre, il ne reste qu'elle : le bleu sombre de la nuit. Au-dessus,
// le rouge faible de la lumière rasante s'y ajoute (dégradé mauve).
uniform vec3 night_sky_color = vec3(1.1, 0.85, 1.2);   // bleu-violet de la nuit (Rayleigh le bleuit encore)
uniform float night_sky_strength = 0.009;

group_uniforms Stars;
uniform float star_strength = 0.5;

group_uniforms CirrusClouds;
uniform bool use_cirrus = true;
uniform sampler2D cirrus_texture : hint_default_black, repeat_enable;
uniform vec2 cirrus_squish = vec2(4.0, 1.0);
uniform float cirrus_scale = 0.05;
uniform float cirrus_treshold : hint_range(0.0, 1.0) = 0.6;
uniform float cirrus_feather : hint_range(0.0, 1.0) = 0.2;
uniform vec2 cirrus_offset = vec2(0.0);
uniform sampler2D cirrus_distortion_texture : hint_default_black, repeat_enable;
uniform float cirrus_distortion_scale = 0.08;
uniform float cirrus_distortion_strength = 0.4;
uniform vec2 cirrus_distortion_offset = vec2(0.0);
uniform sampler2D cirrus_mask_texture : hint_default_white, repeat_enable;
uniform float cirrus_mask_scale = 0.02;
uniform float cirrus_mask_treshold : hint_range(0.0, 1.0) = 0.5;
uniform float cirrus_mask_feather : hint_range(0.0, 1.0) = 0.25;
uniform float cirrus_opacity : hint_range(0.0, 1.0) = 0.35;
uniform vec4 cirrus_color : source_color = vec4(0.78, 0.7, 0.98, 1.0);      // lavande
uniform vec4 cirrus_sun_color : source_color = vec4(1.0, 0.6, 0.66, 1.0);   // saumon rose côté soleil

#define saturate(a) clamp(a, 0.0, 1.0)

vec2 sphere_intersection(vec3 ray_start, vec3 ray_dir, vec3 center, float radius) {
	ray_start -= center;
	float a = dot(ray_dir, ray_dir);
	float b = 2.0 * dot(ray_start, ray_dir);
	float c = dot(ray_start, ray_start) - radius * radius;
	float d = b * b - 4.0 * a * c;
	if (d < 0.0) {
		return vec2(-1.0);
	}
	d = sqrt(d);
	return vec2(-b - d, -b + d) / (2.0 * a);
}

vec2 atmosphere_intersection(vec3 ray_start, vec3 ray_dir) {
	return sphere_intersection(ray_start, ray_dir, PLANET_CENTER, PLANET_RADIUS + ATMOSPHERE_HEIGHT);
}

float phase_rayleigh(float costh) {
	return 3.0 * (1.0 + costh * costh) / (16.0 * PI);
}

float phase_schlick(float costh, float g) {
	g = clamp(g, -0.9381, 0.9381);
	float k = 1.55 * g - 0.55 * g * g * g;
	float kcosth = k * costh;
	return (1.0 - k * k) / ((4.0 * PI) * (1.0 - kcosth) * (1.0 - kcosth));
}

// Deux lobes : vers l'avant (halo du soleil) et vers l'arrière (aérosols : c'est la rétrodiffusion
// de la lumière rougie qui dessine la ceinture de Vénus à l'opposé du soleil).
float phase_mie(float costh) {
	return mix(phase_schlick(costh, mie_anisotropy_factor), phase_schlick(costh, mie_back_anisotropy), mie_back_weight);
}

float atmosphere_height(vec3 p) {
	return distance(p, PLANET_CENTER) - PLANET_RADIUS;
}

vec3 density_atmosphere(float h) {
	return vec3(exp(-max(0.0, h / RAYLEIGH_HEIGHT)),
	            exp(-max(0.0, h / MIE_HEIGHT)),
	            max(0.0, 1.0 - abs(h - 25000.0) / 15000.0));
}

vec3 integrate_optical_depth(vec3 ray_start, vec3 ray_dir) {
	float ray_length = atmosphere_intersection(ray_start, ray_dir).y;
	float step_size = ray_length / float(optical_depth_samples);
	vec3 depth = vec3(0.0);
	for (int i = 0; i < optical_depth_samples; i++) {
		vec3 p = ray_start + ray_dir * (float(i) + 0.5) * step_size;
		depth += density_atmosphere(atmosphere_height(p)) * step_size;
	}
	return depth;
}

vec3 absorb(vec3 depth) {
	return exp(-(depth.x * C_RAYLEIGH * rayleigh_strength
	           + depth.y * C_MIE * mie_strength
	           + depth.z * C_OZONE * ozone_strength) * atmosphere_density);
}

// Pas répartis en puissance (denses près du sol), comme la diffusion.
float sample_time(int i, int count, float distribution, float ray_length) {
	return pow(float(i) / float(count), distribution) * ray_length;
}

vec3 integrate_scattering(vec3 ray_start, vec3 ray_dir, vec3 light_dir, vec3 light_color, out vec3 transmittance) {
	float distribution = 1.0 + saturate(1.0 - atmosphere_height(ray_start) / ATMOSPHERE_HEIGHT) * 8.0;
	vec2 hit = atmosphere_intersection(ray_start, ray_dir);
	float ray_length = hit.y;
	if (hit.x > 0.0) {
		ray_start += ray_dir * hit.x;
		ray_length -= hit.x;
	}
	float costh = dot(ray_dir, light_dir);
	float phase_r = phase_rayleigh(costh);
	float phase_m = phase_mie(costh);

	vec3 depth = vec3(0.0);
	vec3 rayleigh = vec3(0.0);
	vec3 ambient = vec3(0.0);
	vec3 mie = vec3(0.0);
	float prev_t = 0.0;
	for (int i = 0; i < atmosphere_sample_count; i++) {
		float t = sample_time(i, atmosphere_sample_count, distribution, ray_length);
		float step_size = t - prev_t;
		vec3 p = ray_start + ray_dir * t;
		vec3 local_density = density_atmosphere(atmosphere_height(p));
		depth += local_density * step_size;
		vec3 view_t = absorb(depth);
		// Ombre de la Terre : la lumiere n atteint pas ce point si son rayon vers le soleil traverse la planete.
		float lit = sphere_intersection(p, light_dir, PLANET_CENTER, PLANET_RADIUS).x > 0.0 ? 0.0 : 1.0;
		vec3 light_t = absorb(integrate_optical_depth(p, light_dir)) * lit;
		rayleigh += view_t * light_t * phase_r * local_density.x * step_size;
		ambient += view_t * local_density.x * step_size;
		mie += view_t * light_t * phase_m * local_density.y * step_size;
		prev_t = t;
	}
	transmittance = absorb(depth);
	return ((rayleigh * C_RAYLEIGH * rayleigh_strength * rayleigh_tint
	       + mie * C_MIE * mie_strength * mie_tint) * light_color
	      + ambient * C_RAYLEIGH * rayleigh_strength * night_sky_color * night_sky_strength) * exposure;
}

// Transmittance seule (disque solaire, cirrus) : même répartition des pas, sans diffusion.
vec3 view_transmittance(vec3 ray_start, vec3 ray_dir) {
	float distribution = 1.0 + saturate(1.0 - atmosphere_height(ray_start) / ATMOSPHERE_HEIGHT) * 8.0;
	float ray_length = atmosphere_intersection(ray_start, ray_dir).y;
	vec3 depth = vec3(0.0);
	float prev_t = 0.0;
	for (int i = 1; i <= 16; i++) {
		float t = sample_time(i, 16, distribution, ray_length);
		depth += density_atmosphere(atmosphere_height(ray_start + ray_dir * t)) * (t - prev_t);
		prev_t = t;
	}
	return absorb(depth);
}

// Sous l'horizon, on prolonge l'horizon (la mer couvre le reste).
vec3 above_horizon(vec3 dir) {
	return normalize(vec3(dir.x, max(dir.y, 0.0), dir.z));
}

float rand(vec2 co) {
	return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

void sky() {
	vec2 az = normalize(sun_azimuth);
	vec3 start = vec3(0.0, elevation, 0.0);

	if (AT_CUBEMAP_PASS) {
		float se = radians(scatter_sun_elevation_deg);
		vec3 scatter_sun = vec3(az.x * cos(se), sin(se), az.y * cos(se));
		vec3 tr;
		vec3 col = integrate_scattering(start, above_horizon(EYEDIR), scatter_sun, sun_light_color, tr);

		COLOR = mix(color_under.rgb, col, smoothstep(-0.25, 0.0, EYEDIR.y));
	} else if (AT_QUARTER_RES_PASS) {
		COLOR = view_transmittance(start, above_horizon(EYEDIR));
	} else {
		vec3 dir = EYEDIR;
		vec3 transmittance = QUARTER_RES_COLOR.rgb;
		vec3 col = texture(RADIANCE, dir).rgb;

		// Disque solaire (visible seulement au-dessus de l'ombre de la Terre).
		float se = sun_disc_elevation;
		vec3 sun_dir = vec3(az.x * cos(se), sin(se), az.y * cos(se));
		float sun_up = smoothstep(-0.01, 0.005, se);
		float r = sun_disc_size * 0.5;
		float disc = smoothstep(cos(r * (1.0 + sun_disc_feather)), cos(r * (1.0 - sun_disc_feather)), dot(dir, sun_dir));
		col += vec3(disc) * transmittance * sundisc_intensity * sun_up * step(0.0, dir.y);

		// Étoiles très faibles, seulement vers le zénith.
		float star_mask = smoothstep(0.5, 0.75, dir.y);
		vec3 cell = floor(dir * 220.0);
		float h = fract(sin(dot(cell, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
		col += vec3(0.8, 0.85, 1.0) * step(0.9965, h) * star_mask * star_strength;

		// Cirrus : projection sur un plan, étirés, déformés et masqués par grandes plaques.
		if (dir.y > 0.0 && use_cirrus) {
			vec2 coords = dir.xz / dir.y;
			float mask = smoothstep(cirrus_mask_treshold - cirrus_mask_feather, cirrus_mask_treshold + cirrus_mask_feather,
			                        texture(cirrus_mask_texture, coords * cirrus_mask_scale).r);
			vec2 distortion = (texture(cirrus_distortion_texture, coords * cirrus_distortion_scale + cirrus_distortion_offset).xy - 0.5)
			                * cirrus_distortion_strength;
			float cirrus = smoothstep(cirrus_treshold - cirrus_feather, cirrus_treshold + cirrus_feather,
			                          texture(cirrus_texture, (coords * cirrus_scale + cirrus_offset) * cirrus_squish + distortion).r)
			             * mask * cirrus_opacity * smoothstep(0.5, 3.0, length(coords));
			float toward_sun = pow(saturate(0.5 + 0.5 * dot(normalize(dir.xz), az)), 3.0);
			vec3 ccol = mix(cirrus_color.rgb, cirrus_sun_color.rgb, toward_sun) * (0.35 + 0.65 * transmittance);
			col = mix(col, ccol, cirrus);
		}

		if (use_debanding) {
			col += (rand(SKY_COORDS) - 0.5) * 0.001;
		}
		COLOR = col;
	}
}
