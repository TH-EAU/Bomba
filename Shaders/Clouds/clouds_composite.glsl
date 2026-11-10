#[compute]
#version 450

// Composition pleine resolution : upsample de la demi-resolution guide par la profondeur
// (evite les halos autour des objets), puis couleur = scene x transmittance + diffusion.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(set = 0, binding = 1) uniform sampler2D half_cloud;
layout(set = 0, binding = 2) uniform sampler2D half_depth;
layout(set = 0, binding = 3) uniform sampler2D depth_tex;

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
    vec4 temporal;
    vec4 giant_volume;
    vec4 giant_origin;
    vec4 earth_shadow;
    vec4 rain;
    vec4 storm_eye;
    vec4 near_fog;                // x densite du nuage a la camera (x renfort)
} p;

vec3 view_from_depth(vec2 uv, float d) {
    vec4 ndc = vec4(uv * 2.0 - 1.0, d, 1.0);
    vec4 v = p.inv_proj * ndc;
    return v.xyz / v.w;
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 full_size = ivec2(p.sizes.zw);
    if (px.x >= full_size.x || px.y >= full_size.y)
        return;

    vec2 uv = (vec2(px) + 0.5) / vec2(full_size);
    float d = texelFetch(depth_tex, px, 0).r;
    float dist = d > 0.0 ? min(length(view_from_depth(uv, d)), p.march.z) : p.march.z;

    // Upsample bilateral : 4 texels demi-resolution ponderes par bilineaire x similarite de profondeur.
    ivec2 half_size = ivec2(p.sizes.xy);
    vec2 hp = (vec2(px) + 0.5) * 0.5 - 0.5;
    ivec2 base = ivec2(floor(hp));
    vec2 f = hp - vec2(base);

    vec4 acc = vec4(0.0);
    float wsum = 0.0;
    for (int j = 0; j < 2; j++) {
        for (int i = 0; i < 2; i++) {
            ivec2 q = clamp(base + ivec2(i, j), ivec2(0), half_size - 1);
            float wb = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
            float hd = texelFetch(half_depth, q, 0).r;
            float wd = 1.0 / (1e-3 + abs(hd - dist) / max(dist, 1.0));
            float w = wb * wd + 1e-6;
            acc += texelFetch(half_cloud, q, 0) * w;
            wsum += w;
        }
    }
    vec4 cloud = acc / wsum;

    vec4 color = imageLoad(color_image, px);
    color.rgb = color.rgb * cloud.a + cloud.rgb;

    // Brouillard proche dans un nuage : la marche efface la densite devant la camera (fondu sur near_fade), on rend
    // ici ce voile manquant, uniforme, a la couleur moyenne du nuage visible (moyenne des texels opaques d une grille
    // de 4x3 points, les memes pour tout l ecran).
    if (p.near_fog.x > 0.001) {
        float nf = p.ambient_top_threshold.w;
        float dn = min(dist, nf);
        // Densite manquante = 1 - smoothstep(nf / 4, nf, t), integree de 0 a dn (approximation lineaire du fondu).
        float missing = min(dn, 0.25 * nf) + max(dn - 0.25 * nf, 0.0) * (1.0 - 0.5 * (dn - 0.25 * nf) / (0.75 * nf));
        float tn = exp(-p.near_fog.x * p.sun_color_extinction.w * missing);
        vec3 fog_sum = vec3(0.0);
        float fog_w = 0.0;
        for (int j = 0; j < 3; j++) {
            for (int i = 0; i < 4; i++) {
                vec4 c = texelFetch(half_cloud, ivec2(vec2(half_size) * vec2((float(i) + 0.5) / 4.0, (float(j) + 0.5) / 3.0)), 0);
                fog_sum += c.rgb;
                fog_w += 1.0 - c.a;
            }
        }
        vec3 fog_col = fog_w > 0.05 ? fog_sum / fog_w : p.fog.rgb;
        color.rgb = color.rgb * tn + fog_col * (1.0 - tn);
    }
    imageStore(color_image, px, color);
}
