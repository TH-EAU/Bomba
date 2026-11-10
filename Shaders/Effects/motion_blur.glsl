#[compute]
#version 450

// Flou de mouvement de camera (Scripts/Effects/MotionBlur.cs), apres les nuages et les transparents.
// Chaque pixel (uv, profondeur) est reprojete vers l'image precedente par une seule matrice
// (vue-projection precedente x inverse de la vue-projection courante) ; l'image est moyennee le long
// de ce deplacement. Ce qui est proche (l'engin, le cockpit) bouge avec la camera : il reste net.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform restrict writeonly image2D color_image;
layout(set = 0, binding = 1) uniform sampler2D src_tex;     // copie de l'image, filtrage lineaire
layout(set = 0, binding = 2) uniform sampler2D depth_tex;

layout(set = 0, binding = 3, std140) uniform Params {
    mat4 reproj;        // NDC courant -> clip precedent
    mat4 inv_proj;      // NDC courant -> vue (distance)
    vec4 settings;      // x force, y longueur max (uv), z net en deca (m), w flou plein au-dela (m)
    vec4 sizes;         // xy taille, z echantillons
} p;

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = ivec2(p.sizes.xy);
    if (px.x >= size.x || px.y >= size.y)
        return;
    vec2 uv = (vec2(px) + 0.5) / vec2(size);

    float depth = max(texture(depth_tex, uv).r, 1e-7);   // profondeur inversee : 0 = infini
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth, 1.0);
    vec4 prev = p.reproj * ndc;
    vec2 prev_uv = prev.xy / prev.w * 0.5 + 0.5;
    vec4 view = p.inv_proj * ndc;
    float dist = length(view.xyz / view.w);

    vec2 vel = (uv - prev_uv) * p.settings.x;
    float len = length(vel);
    if (len > p.settings.y)
        vel *= p.settings.y / len;
    vel *= smoothstep(p.settings.z, p.settings.w, dist);

    vec3 col;
    if (length(vel) * float(size.x) < 0.75) {
        col = texture(src_tex, uv).rgb;
    } else {
        // Les echantillons qui tombent sur un objet proche (l engin) sont ignores : sinon la coque bave sur le decor.
        int n = int(p.sizes.z);
        vec3 center = texture(src_tex, uv).rgb;
        col = vec3(0.0);
        float w = 0.0;
        for (int i = 0; i < n; i++) {
            float t = float(i) / float(n - 1) - 0.5;
            vec2 suv = uv - vel * t;
            vec4 sv = p.inv_proj * vec4(suv * 2.0 - 1.0, max(texture(depth_tex, suv).r, 1e-7), 1.0);
            float keep = smoothstep(p.settings.z, p.settings.w, length(sv.xyz / sv.w));
            // Moyenne de Karis : un echantillon tres lumineux (eclair HDR) pese peu, sinon il s etale en large bande.
            vec3 c = texture(src_tex, suv).rgb;
            float kw = keep / (1.0 + max(c.r, max(c.g, c.b)));
            col += c * kw;
            w += kw;
        }
        col = w > 0.001 ? (col + center * 0.001) / (w + 0.001) : center;
        // Les traits tres lumineux restent visibles (a moitie) au lieu de disparaitre dans la moyenne.
        col = max(col, center * 0.5);
    }
    imageStore(color_image, px, vec4(col, 1.0));
}
