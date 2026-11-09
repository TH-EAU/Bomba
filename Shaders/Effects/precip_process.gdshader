shader_type particles;

// Pluie et grêle (Scripts/Effects/Precipitation.cs) : une boîte qui suit la caméra.
// Les particules non locales vivent en coordonnées monde ; une particule sortie de la boîte revient
// de l'autre côté (repli modulo), donc la pluie entoure la caméra en permanence.
// CUSTOM.xyz garde la vitesse de la particule : le rendu (precip_draw) en tire la longueur du trait.
// CUSTOM.w garde un hachage fixe (0..1) de la particule, écrit à start() : le rendu s'en sert pour
// faire apparaître les particules une à une selon l'intensité. process() ne l'écrit jamais.

uniform vec3 camera_pos;
uniform vec3 box_size = vec3(60.0, 40.0, 60.0);
uniform vec3 base_velocity;      // chute + vent (m/s)
uniform float turbulence = 0.0;  // agitation des rafales (m/s)
uniform float time_s = 0.0;

// Hachage entier (fmix32 de MurmurHash3), sans état.
uint hash_u(uint x) {
	x = x ^ (x >> 16u);
	x = x * 2246822507u;
	x = x ^ (x >> 13u);
	x = x * 3266489909u;
	x = x ^ (x >> 16u);
	return x;
}

// Valeur 0..1, stable pour un couple (x, salt).
float hash_f(uint x, uint salt) {
	return float(hash_u(x ^ hash_u(salt + 1u))) * (1.0 / 4294967296.0);
}

void start() {
	CUSTOM.w = hash_f(hash_u(INDEX ^ hash_u(RANDOM_SEED)), 4u);
	if (RESTART_POSITION) {
		uint seed = hash_u(INDEX ^ hash_u(RANDOM_SEED));
		vec3 r = vec3(hash_f(seed, 1u), hash_f(seed, 2u), hash_f(seed, 3u));
		vec3 p = camera_pos + (r - 0.5) * box_size;
		TRANSFORM = mat4(vec4(1.0, 0.0, 0.0, 0.0), vec4(0.0, 1.0, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(p, 1.0));
	}
}

void process() {
	uint seed = hash_u(INDEX ^ hash_u(RANDOM_SEED + 7u));
	float h1 = hash_f(seed, 11u);
	float h2 = hash_f(seed, 12u);
	float h3 = hash_f(seed, 13u);

	vec3 v = base_velocity + turbulence * vec3(sin(time_s * 1.7 + h1 * 40.0), 0.3 * sin(time_s * 2.3 + h2 * 40.0), sin(time_s * 1.3 + h3 * 40.0));
	TRANSFORM[3].xyz += v * DELTA;

	// Repli dans la boîte centrée sur la caméra.
	vec3 rel = TRANSFORM[3].xyz - camera_pos;
	rel = mod(rel + box_size * 0.5, box_size) - box_size * 0.5;
	TRANSFORM[3].xyz = camera_pos + rel;

	CUSTOM.xyz = v;
	VELOCITY = vec3(0.0);
}
