shader_type canvas_item;

// Gouttes d'eau sur l'ecran : trois couches de cellules, une goutte au plus par cellule.
// Pilote par Scripts/Effects/ScreenDroplets.cs.

uniform float coverage : hint_range(0.0, 1.0) = 0.0;
uniform float streak : hint_range(0.0, 1.0) = 0.0;
uniform float flow_time = 0.0;
uniform float drop_scale = 20.0;
uniform float refraction = 0.06;
uniform vec4 tint : source_color = vec4(0.97, 0.98, 1.0, 1.0);
uniform float darken_rim = 0.4;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

// uvq : coordonnees ecran corrigees de l'aspect (x = uv.x * aspect, y = uv.y).
// Retour : xy = normale de lentille (norme = d), z = opacite, w = distance normalisee au centre (0 centre, 1 bord).
vec4 drop_layer(vec2 uvq, float cells, float seed, float aspect, float density, out float glint) {
	vec2 q = uvq * cells;
	vec2 screen_center = vec2(0.5 * aspect, 0.5) * cells;
	vec2 cell = floor(q);
	glint = 0.0;
	vec4 best = vec4(0.0);
	float best_d = 1.0;

	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id = cell + vec2(float(i), float(j));
			vec2 hid = id + vec2(seed);

			float appear = hash12(hid + vec2(17.3, 3.1));
			// density : part maximale des cellules occupees dans cette couche (les grosses gouttes sont rares).
			float present = clamp((coverage * density - appear) / 0.06, 0.0, 1.0);
			if (present <= 0.0) {
				continue;
			}

			float radius = mix(0.1, 0.32, hash12(hid + vec2(41.7, 9.2)));
			vec2 center = id + mix(vec2(radius), vec2(1.0 - radius), hash22(hid));
			float speed = mix(0.6, 1.4, hash12(hid + vec2(73.1, 5.9)));
			float phase = hash12(hid + vec2(97.9, 2.4));

			// Direction radiale depuis le centre de l'ecran.
			vec2 dc = center - screen_center;
			float dl = length(dc);
			vec2 radial = dl > 0.0001 ? dc / dl : vec2(0.0, 1.0);
			vec2 tangent = vec2(-radial.y, radial.x);

			// Glissement vers le bord, en boucle dans la cellule (fract) : la goutte disparait avant de revenir.
			float travel = fract(phase + flow_time * speed * 0.35);
			center += radial * streak * travel * 0.9;
			float life = 1.0 - streak * smoothstep(0.7, 1.0, travel);

			// Etirement radial (facteur 1 + 4 streak). La moitie arriere (trainee) est plus fine et plus transparente.
			vec2 rel = q - center;
			float along = dot(rel, radial);
			float across = dot(rel, tangent);
			float behind = step(along, 0.0) * streak;
			float stretch = 1.0 + streak * 4.0;
			float width = 1.0 - 0.55 * behind;
			vec2 e = vec2(along / stretch, across / width) / radius;
			float d = length(e);
			if (d >= 1.0) {
				continue;
			}

			float rim = 1.0 - smoothstep(0.9, 1.0, d);
			float alpha = present * life * rim * mix(1.0, 0.4, behind);
			if (alpha <= 0.0 || d >= best_d) {
				continue;
			}

			best_d = d;
			best = vec4(radial * e.x + tangent * e.y, alpha, d);
			glint = smoothstep(0.18, 0.05, length(rel / radius - vec2(-0.35, -0.4))) * (1.0 - streak);
		}
	}
	return best;
}

vec3 apply_drop(vec3 col, vec2 uv, float aspect, vec4 drop, float glint) {
	if (drop.z <= 0.0) {
		return col;
	}
	float d = drop.w;
	vec2 nuv = vec2(drop.x / aspect, drop.y);
	vec3 lens = texture(screen_texture, uv - nuv * refraction * (1.0 - d * d)).rgb;
	lens *= 1.0 - darken_rim * smoothstep(0.82, 1.0, d);
	lens *= tint.rgb;
	lens += vec3(glint * 0.25);
	return mix(col, lens, drop.z);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec3 col = texture(screen_texture, uv).rgb;

	// Hors des gouttes (coverage nul), l'image reste telle quelle.
	if (coverage >= 0.001) {
		float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
		vec2 uvq = vec2(uv.x * aspect, uv.y);
		float glint;

		// Couches de la plus grosse a la plus fine ; la couche principale (x1) est dessinee en dernier.
		vec4 layer = drop_layer(uvq, drop_scale / 2.2, 211.0, aspect, 0.25, glint);
		col = apply_drop(col, uv, aspect, layer, glint);
		layer = drop_layer(uvq, drop_scale / 1.4, 97.0, aspect, 0.45, glint);
		col = apply_drop(col, uv, aspect, layer, glint);
		layer = drop_layer(uvq, drop_scale, 0.0, aspect, 0.7, glint);
		col = apply_drop(col, uv, aspect, layer, glint);
	}

	COLOR = vec4(col, 1.0);
}
