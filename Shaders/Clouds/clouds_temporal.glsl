#[compute]
#version 450

// Accumulation temporelle des nuages (demi-resolution).
// Chaque pixel est reprojete dans l'image precedente a la profondeur moyenne du nuage,
// l'historique est borne par le voisinage 3x3 courant (anti-trainees), puis melange.
// Le jitter par frame du ray marching est ainsi moyenne : moins de grain, detail fin stable.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D cur_cloud;   // rgba : diffusion + transmittance
layout(set = 0, binding = 1) uniform sampler2D cur_depth;   // r = distance de scene, g = profondeur du nuage
layout(set = 0, binding = 2) uniform sampler2D history;     // image resolue precedente (filtrage lineaire)
layout(rgba16f, set = 0, binding = 3) uniform restrict writeonly image2D out_resolved;

layout(set = 0, binding = 4, std140) uniform Params {
    mat4 inv_proj;
    mat4 cam_to_world;
    vec4 cam_pos_time;
    vec4 sun_dir_intensity;
    vec4 sun_color_extinction;
    vec4 ambient_top_threshold;
    vec4 ambient_bottom_detail;
    vec4 volume;
    vec4 offset_frame;
    vec4 lightning;
    vec4 sizes;
    vec4 fog;
    vec4 march;
    mat4 prev_view_proj;
    vec4 temporal;                // x poids de l'image courante, y historique valide
} p;

vec3 view_from_depth(vec2 uv, float d) {
    vec4 ndc = vec4(uv * 2.0 - 1.0, d, 1.0);
    vec4 v = p.inv_proj * ndc;
    return v.xyz / v.w;
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 half_size = ivec2(p.sizes.xy);
    if (px.x >= half_size.x || px.y >= half_size.y)
        return;

    vec4 cur = texelFetch(cur_cloud, px, 0);
    if (p.temporal.y < 0.5) {
        imageStore(out_resolved, px, cur);
        return;
    }

    // Voisinage 3x3 : boite min/max de la couleur courante.
    vec4 mn = cur, mx = cur;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            if (i == 0 && j == 0) continue;
            vec4 s = texelFetch(cur_cloud, clamp(px + ivec2(i, j), ivec2(0), half_size - 1), 0);
            mn = min(mn, s);
            mx = max(mx, s);
        }
    }

    // Reprojection a la profondeur moyenne du nuage.
    vec2 uv = (vec2(px) + 0.5) / vec2(half_size);
    vec3 dir = normalize(mat3(p.cam_to_world) * normalize(view_from_depth(uv, 0.5)));
    float cd = texelFetch(cur_depth, px, 0).g;
    vec3 world = p.cam_pos_time.xyz + dir * cd;
    vec4 clip = p.prev_view_proj * vec4(world, 1.0);

    vec4 result = cur;
    if (clip.w > 1e-4) {
        vec2 puv = clip.xy / clip.w * 0.5 + 0.5;
        if (all(greaterThanEqual(puv, vec2(0.0))) && all(lessThanEqual(puv, vec2(1.0)))) {
            vec4 h = textureLod(history, puv, 0.0);
            vec4 margin = (mx - mn) * 0.15 + 0.01;
            h = clamp(h, mn - margin, mx + margin);
            result = mix(h, cur, p.temporal.x);
        }
    }
    imageStore(out_resolved, px, result);
}
