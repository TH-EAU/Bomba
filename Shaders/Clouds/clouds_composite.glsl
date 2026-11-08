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
    imageStore(color_image, px, color);
}
