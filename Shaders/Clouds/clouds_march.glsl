#[compute]
#version 450

// Ray marching des nuages en demi-resolution, guide par un SDF precalcule.
// Hors du nuage : pas = distance SDF (saut du vide). Dans le nuage : pas fixe.
// Forme : couverture precalculee erodee par deux passes de detail Perlin-Worley
// (texture de forme 64^3 puis texture de detail 32^3), facon Horizon Zero Dawn / Nubis.
// L'erosion ne fait que retirer de la densite : la garantie du SDF est preservee.
// Eclairage : Beer-Lambert vers le soleil (avec la forme erodee), phase Henyey-Greenstein double,
// terme "powder", ambiance ciel/mer + occlusion par le SDF, orages sombres, flash d'eclair.
// Sorties : diffusion (rgb) + transmittance (a) ; distance de scene (r) et profondeur moyenne
// du nuage (g) pour l'upsample et la reprojection temporelle.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler3D cloud_tex;   // r = SDF (m), g = couverture, b = orage, a = ascendance
layout(set = 0, binding = 1) uniform sampler3D shape_tex;   // r = Perlin-Worley, gba = Worley FBM (3 frequences)
layout(set = 0, binding = 2) uniform sampler2D depth_tex;   // profondeur de scene pleine resolution
layout(rgba16f, set = 0, binding = 3) uniform restrict writeonly image2D out_cloud;
layout(rg32f, set = 0, binding = 4) uniform restrict writeonly image2D out_depth;
layout(set = 0, binding = 6) uniform sampler3D detail_tex;  // rgb = Worley FBM (3 frequences)
layout(set = 0, binding = 7) uniform sampler3D giant_tex;   // grille geante (cumulonimbus), memes canaux

layout(set = 0, binding = 5, std140) uniform Params {
    mat4 inv_proj;
    mat4 cam_to_world;
    vec4 cam_pos_time;            // xyz camera, w temps
    vec4 sun_dir_intensity;       // xyz vers le soleil, w intensite
    vec4 sun_color_extinction;    // rgb couleur soleil, w extinction (1/m)
    vec4 ambient_top_threshold;   // rgb ambiance haute, w distance de fondu pres de la camera (m)
    vec4 ambient_bottom_detail;   // rgb ambiance basse, w force de l'erosion
    vec4 volume;                  // x periode, y alt min, z alt max, w periode de la texture de forme (m)
    vec4 offset_frame;            // xy derive (x, z), z index de frame, w vitesse du detail
    vec4 lightning;               // xyz position, w flash 0..1
    vec4 sizes;                   // x,y demi-resolution, z,w pleine resolution
    vec4 fog;                     // rgb couleur du brouillard, w densite
    vec4 march;                   // x pas max, y pas interieur (m), z distance max, w mode debug
    mat4 prev_view_proj;          // (passe temporelle)
    vec4 temporal;                // x,y passe temporelle ; z exposant vertical local ; w croissance du pas (m)
    vec4 giant_volume;            // x periode, y alt min, z alt max, w exposant vertical (grille geante)
    vec4 giant_origin;            // xy coin du domaine geant (sans repetition), z LOD detail fin (m), w LOD bourgeons (m)
    vec4 earth_shadow;            // x altitude de l ombre a l origine, y tan(abaissement), zw azimut du soleil
    vec4 rain;                    // rideau de pluie : xy centre (x, z monde), z rayon (m), w intensite (0 : aucun)
    vec4 storm_eye;               // x rayon de l oeil du cumulonimbus (m), y montee relative du bruit (convection)
} p;

const float PI = 3.14159265;

float remap(float v, float a, float b, float c, float d) {
    return c + (v - a) / max(b - a, 1e-4) * (d - c);
}

// Echantillonne une grille (periode, alt min, alt max, exposant vertical). Hors de sa tranche
// d'altitude : vide, avec un SDF egal a la distance a la tranche.
vec4 sample_grid(sampler3D tex, vec4 vol, vec3 pos) {
    if (pos.y < vol.y || pos.y > vol.z) {
        float gap = pos.y < vol.y ? vol.y - pos.y : pos.y - vol.z;
        return vec4(gap, 0.0, 0.0, 0.0);   // la distance a la tranche ne surestime jamais
    }
    vec3 uvw = vec3((pos.x - p.offset_frame.x) / vol.x,
                    pow((pos.y - vol.y) / (vol.z - vol.y), 1.0 / vol.w),
                    (pos.z - p.offset_frame.y) / vol.x);
    return textureLod(tex, uvw, 0.0);
}

// Grille geante : un seul exemplaire (pas de repetition), domaine [origine, origine + periode].
// Hors du domaine : vide, SDF = distance exacte au domaine (ne surestime jamais).
vec4 sample_giant(vec3 pos) {
    vec4 vol = p.giant_volume;
    vec2 l = pos.xz - p.offset_frame.xy - p.giant_origin.xy;
    vec2 out_h = max(max(-l, l - vec2(vol.x)), vec2(0.0));
    float vgap = pos.y < vol.y ? vol.y - pos.y : max(pos.y - vol.z, 0.0);
    float gap = length(vec3(out_h, vgap));
    if (gap > 0.0)
        return vec4(gap, 0.0, 0.0, 0.0);
    vec3 uvw = vec3(l.x / vol.x, pow((pos.y - vol.y) / (vol.z - vol.y), 1.0 / vol.w), l.y / vol.x);
    return textureLod(giant_tex, uvw, 0.0);
}

// Union des deux grilles : nappe/cumulus locaux et cumulonimbus geant (SDF min, canaux max).
float g_giant = 0.0;

vec4 sample_cloud(vec3 pos) {
    vec4 a = sample_grid(cloud_tex, vec4(p.volume.xyz, p.temporal.z), pos);
    // L oeil du cumulonimbus perce aussi la nappe locale (qui se repete) : masquee a la position reelle de l oeil.
    // Le SDF n est pas modifie : retirer du nuage ne fait qu allonger la vraie distance (saut toujours sur).
    if (p.storm_eye.x > 0.0) {
        float de = length(pos.xz - p.rain.xy);
        a.g *= smoothstep(p.storm_eye.x * 0.75, p.storm_eye.x * 1.25, de);
    }
    vec4 b = sample_giant(pos);
    g_giant = b.g / max(a.g + b.g, 1e-4);
    return vec4(min(a.r, b.r), max(a.gba, b.gba));
}

// Tranche d'altitude totale des nuages (union des deux grilles).
float slab_lo() { return min(p.volume.y, p.giant_volume.y); }
float slab_hi() { return max(p.volume.z, p.giant_volume.z); }

float hg(float c, float g) {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5));
}

float ign(vec2 px) {
    return fract(52.9829189 * fract(dot(px, vec2(0.06711056, 0.00583715))));
}

// Rideau de pluie sous le cumulonimbus : cylindre vertical (de la mer a la base des nuages), centre p.rain.xy,
// rayon p.rain.z. Peu dense : de loin, un voile gris au travers duquel on distingue les elements. Stries verticales
// qui defilent vers le bas. Marche dediee (intersection analytique) : la tranche des nuages n'est pas elargie.
const float RAIN_FLOOR = -60.0;
const float RAIN_EXTINCTION = 3.0e-4;   // 1/m a intensite 1 : presque opaque a travers le coeur, transparent sur les bords
const int RAIN_STEPS = 20;

void march_rain(vec3 cam, vec3 dir, float t_max, float jitter, inout vec3 L, inout float T) {
    if (p.rain.w <= 0.0 || T < 0.01)
        return;
    float top = p.giant_volume.y;
    vec2 oc = cam.xz - p.rain.xy;
    float a = dot(dir.xz, dir.xz), b = dot(oc, dir.xz), c = dot(oc, oc) - p.rain.z * p.rain.z;
    float ta = 0.0, tb = t_max;
    if (a < 1e-8) {
        if (c > 0.0) return;
    } else {
        float disc = b * b - a * c;
        if (disc <= 0.0) return;
        float s = sqrt(disc);
        ta = max(ta, (-b - s) / a);
        tb = min(tb, (-b + s) / a);
    }
    if (abs(dir.y) < 1e-5) {
        if (cam.y < RAIN_FLOOR || cam.y > top) return;
    } else {
        float ya = (RAIN_FLOOR - cam.y) / dir.y, yb = (top - cam.y) / dir.y;
        ta = max(ta, min(ya, yb));
        tb = min(tb, max(ya, yb));
    }
    if (tb <= ta)
        return;

    float st = (tb - ta) / float(RAIN_STEPS);
    vec3 amb = mix(p.ambient_bottom_detail.rgb, p.ambient_top_threshold.rgb, 0.35) * 0.85;
    float fall = p.cam_pos_time.w * 9.0;
    for (int i = 0; i < RAIN_STEPS; i++) {
        float t = ta + (float(i) + jitter) * st;
        vec3 pos = cam + dir * t;
        float r = length(pos.xz - p.rain.xy) / p.rain.z;
        float edge = 1.0 - smoothstep(0.6, 1.0, r);
        float eye_r = p.storm_eye.x;
        if (eye_r > 0.0)
            edge *= smoothstep(eye_r * 0.75, eye_r * 1.25, r * p.rain.z);   // pas de pluie dans l oeil
        float floor_fade = smoothstep(RAIN_FLOOR, RAIN_FLOOR + 40.0, pos.y);
        float streak = textureLod(detail_tex, vec3(pos.x / 60.0, (pos.y + fall) / 900.0, pos.z / 60.0), 0.0).r;
        float dens = p.rain.w * edge * floor_fade * mix(0.35, 1.0, streak);
        if (dens <= 0.001)
            continue;
        vec3 lum = amb;
        if (p.lightning.w > 0.0)
            lum += vec3(0.75, 0.8, 1.0) * p.lightning.w * 6.0 * exp(-distance(pos, p.lightning.xyz) / 3000.0);
        lum = mix(lum, p.fog.rgb, 1.0 - exp(-t * p.fog.w));
        float Ts = exp(-dens * RAIN_EXTINCTION * st);
        L += T * lum * (1.0 - Ts);
        T *= Ts;
    }
}

// Reconstruit la position en vue : la projection de Godot est deja corrigee Vulkan (Y retourne, Z [0,1] inverse).
vec3 view_from_depth(vec2 uv, float d) {
    vec4 ndc = vec4(uv * 2.0 - 1.0, d, 1.0);
    vec4 v = p.inv_proj * ndc;
    return v.xyz / v.w;
}

// Derive du bruit : horizontale et montante (convection), le motif monte dans les nuages et bouillonne.
vec3 noise_drift() {
    float d = p.cam_pos_time.w * p.offset_frame.w;
    return vec3(d, -d * p.storm_eye.y, d * 0.4);
}

// Detail fin : derive differente (horizontale opposee, montee plus rapide) : le relief evolue au lieu de glisser.
vec3 detail_drift() {
    float d = p.cam_pos_time.w * p.offset_frame.w;
    return vec3(-d * 0.7, -d * p.storm_eye.y * 1.6, d * 0.9);
}

// Saturation douce : les faibles densites montent vite (bords nets), le coeur reste borne a 1.
float sharpen(float d) {
    return 1.0 - exp(-d * 4.0);
}

// Part de la grille geante dans le dernier echantillon (mise a jour par sample_cloud).
// Le bruit de detail a la meme echelle partout (p.volume.w) : le cumulonimbus a le meme grain que
// la nappe et les cumulus locaux. Pas de cisaillement vertical.
float detail_period() {
    return p.volume.w;
}

// Forme Perlin-Worley a une periode donnee (bourgeons ~ periode/4). La couverture decoupe la forme.
float shape_at(vec3 pos, float cov, float period) {
    vec4 sn = textureLod(shape_tex, pos / period + noise_drift(), 0.0);
    // Les Worley inverses sont tasses vers 1 : on etale leur contraste.
    float wfbm = smoothstep(0.45, 0.97, sn.g * 0.625 + sn.b * 0.25 + sn.a * 0.125);
    float pw = smoothstep(0.3, 0.97, sn.r);
    float base = remap(pw, -(1.0 - wfbm), 1.0, 0.0, 1.0);
    float shaped = clamp(remap(base, 1.0 - cov, 1.0, 0.0, 1.0), 0.0, 1.0) * cov;
    return mix(cov, shaped, p.ambient_bottom_detail.w);
}

// Passe 1 : forme. Grille geante (cellules ~190 m) : une passe grossiere (x3) efface les cellules et
// donne les grands bourgeons, puis la meme passe que les nuages locaux ajoute leur grain (a demi-dose).
float shape_density(vec3 pos, float cov) {
    float fine = shape_at(pos, cov, p.volume.w);
    if (g_giant < 0.01)
        return fine;
    float coarse = shape_at(pos, cov, p.volume.w * 3.0);
    float giant = mix(coarse, shape_at(pos, coarse, p.volume.w), 0.5);
    return mix(fine, giant, g_giant);
}

// Passe 2 : detail fin Worley sur les bords. Bas des nuages effiloche, haut boursoufle.
float detail_density(vec3 pos, float shape) {
    if (shape <= 0.0)
        return 0.0;
    vec3 dn = textureLod(detail_tex, pos / (detail_period() * 0.22) + detail_drift() * 3.0, 0.0).rgb;
    float dfbm = smoothstep(0.45, 0.97, dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125);
    float bottom = 1.0 - smoothstep(p.volume.y + 60.0, p.volume.y + 170.0, pos.y);
    float modifier = mix(dfbm, 1.0 - dfbm, bottom);
    float erode = modifier * 0.6 * p.ambient_bottom_detail.w;
    return clamp(remap(shape, erode, 1.0, 0.0, 1.0), 0.0, 1.0);
}

// Epaisseur optique vers le soleil (pas croissants, forme Perlin-Worley sans le detail fin).
float light_optical_depth(vec3 pos, bool use_shape) {
    vec3 L = p.sun_dir_intensity.xyz;
    float od = 0.0;
    float t = 0.0;
    float step_len = 8.0;
    for (int i = 0; i < 6; i++) {
        t += step_len;
        vec3 q = pos + L * t;
        vec4 c = sample_cloud(q);
        if (c.g > 0.0) {
            float d = sharpen(use_shape && i < 4 ? shape_density(q, c.g) : c.g);
            od += d * (1.0 + c.b * 2.0) * step_len;
        }
        step_len *= 2.1;
    }
    return od * p.sun_color_extinction.w;
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 half_size = ivec2(p.sizes.xy);
    if (px.x >= half_size.x || px.y >= half_size.y)
        return;

    // Profondeur : on prend le point le plus lointain du bloc 2x2 (Z inverse : plus petite valeur).
    ivec2 full_size = ivec2(p.sizes.zw);
    ivec2 fp = px * 2;
    float d = 1.0;
    for (int j = 0; j < 2; j++)
        for (int i = 0; i < 2; i++)
            d = min(d, texelFetch(depth_tex, clamp(fp + ivec2(i, j), ivec2(0), full_size - 1), 0).r);

    vec2 uv = (vec2(px) + 0.5) / vec2(half_size);
    vec3 cam = p.cam_pos_time.xyz;
    vec3 view_dir = normalize(view_from_depth(uv, 0.5));
    vec3 dir = normalize(mat3(p.cam_to_world) * view_dir);

    float scene_dist = p.march.z;
    if (d > 0.0) {
        vec3 vp = view_from_depth(uv, d);
        scene_dist = min(length(vp), p.march.z);
    }

    float dbg = p.march.w;
    // Mode debug 1 : bandes de distance de scene (verifie la reconstruction de profondeur).
    if (dbg > 0.5 && dbg < 1.5) {
        imageStore(out_depth, px, vec4(scene_dist, scene_dist, 0.0, 0.0));
        imageStore(out_cloud, px, vec4(vec3(fract(scene_dist / 50.0)), 0.0));
        return;
    }
    // Mode debug 2 : r = profondeur brute x20, g = bandes de distance, b = direction verticale du rayon.
    if (dbg > 1.5 && dbg < 2.5) {
        imageStore(out_depth, px, vec4(scene_dist, scene_dist, 0.0, 0.0));
        imageStore(out_cloud, px, vec4(d * 20.0, fract(scene_dist / 50.0), dir.y * 0.5 + 0.5, 0.0));
        return;
    }
    // Mode debug 3 : rouge uni (verifie la chaine complete).
    if (dbg > 2.5 && dbg < 3.5) {
        imageStore(out_depth, px, vec4(scene_dist, scene_dist, 0.0, 0.0));
        imageStore(out_cloud, px, vec4(1.0, 0.0, 0.0, 0.0));
        return;
    }
    // Mode debug 6 : nuages desactives (compare l'environnement seul).
    if (dbg > 5.5) {
        imageStore(out_depth, px, vec4(scene_dist, scene_dist, 0.0, 0.0));
        imageStore(out_cloud, px, vec4(0.0, 0.0, 0.0, 1.0));
        return;
    }

    // Rideau de pluie : devant les nuages si la camera est sous leur base, derriere sinon.
    vec3 L = vec3(0.0);
    float T = 1.0;
    float rain_jitter = ign(vec2(px) + p.offset_frame.z * 3.17);
    bool rain_first = cam.y < p.giant_volume.y;
    if (rain_first)
        march_rain(cam, dir, scene_dist, rain_jitter, L, T);

    // Intersection avec la tranche d'altitude des nuages.
    float t0 = 0.0, t1 = scene_dist;
    float y0 = slab_lo(), y1 = slab_hi();
    bool miss = false;
    if (abs(dir.y) < 1e-5) {
        miss = cam.y < y0 || cam.y > y1;
    } else {
        float ta = (y0 - cam.y) / dir.y, tb = (y1 - cam.y) / dir.y;
        t0 = max(t0, min(ta, tb));
        t1 = min(t1, max(ta, tb));
    }
    if (miss || t0 >= t1) {
        imageStore(out_depth, px, vec4(scene_dist, scene_dist, 0.0, 0.0));
        imageStore(out_cloud, px, vec4(L, T));
        return;
    }

    float step_in = p.march.y;
    float jitter = ign(vec2(px) + p.offset_frame.z * 5.588238);
    float t = t0 + jitter * step_in;
    bool entered = true;
    float first_hit = -1.0;
    float depth_w = 0.0, depth_sum = 0.0;

    vec3 sun_dir = p.sun_dir_intensity.xyz;
    float cos_theta = dot(dir, sun_dir);
    float phase = mix(hg(cos_theta, 0.6), hg(cos_theta, -0.25), 0.35) * 4.0 * PI;
    vec3 sun_col = p.sun_color_extinction.rgb * p.sun_dir_intensity.w;
    float ext = p.sun_color_extinction.w;
    float near_fade = p.ambient_top_threshold.w;
    int max_steps = int(p.march.x);
    // Echantillons consecutifs vides (erodes) : le pas s allonge (jusqu a x4). Sinon, un rayon qui longe la frange
    // d un nuage proche epuise ses pas avant d atteindre les nuages lointains, qui disparaissent.
    // A la rentree dans la densite apres de grands pas : recul, puis pas fins (evite les stries de marche).
    int thin_run = 0;
    int fine_steps = 0;
    float last_adv = 0.0;

    for (int i = 0; i < max_steps && t < t1; i++) {
        vec3 pos = cam + dir * t;
        vec4 c = sample_cloud(pos);
        float sdf = c.r;
        // Pas interieur croissant avec la distance (nuages lointains echantillonnes plus grossierement).
        float st = step_in * (1.0 + t / p.temporal.w);
        if (sdf > st) {
            t += sdf;   // saut du vide garanti par le SDF
            entered = false;
            last_adv = 0.0;
            continue;
        }
        // A l'entree dans le nuage apres un saut, on re-jitter le pas : evite les anneaux en surface.
        if (!entered) {
            entered = true;
            t += jitter * st;
            pos = cam + dir * t;
            c = sample_cloud(pos);
            sdf = c.r;
        }

        // Modes debug 4 et 5 : forme Perlin-Worley / couverture brute au premier contact.
        if (dbg > 3.5 && c.g > 0.01) {
            float v = dbg < 4.5 ? shape_density(pos, c.g) : c.g;
            imageStore(out_depth, px, vec4(scene_dist, t, 0.0, 0.0));
            imageStore(out_cloud, px, vec4(vec3(v), 0.0));
            return;
        }

        // LOD du bruit : de loin, les petites variations scintillent et coutent cher.
        // Le detail fin s'estompe puis n'est plus lu. La forme Perlin-Worley est toujours calculee :
        // sans elle, les cellules de la grille geante (~190 m) apparaissent en marches d'escalier.
        // Au-dela de giant_origin.w, l'ombrage n'utilise plus la forme (rayon d'ombre moins cher).
        float lod_fine = 1.0 - smoothstep(p.giant_origin.z * 0.25, p.giant_origin.z, t);
        float lod_shape = 1.0 - smoothstep(p.giant_origin.w * 0.3, p.giant_origin.w, t);
        float dens = 0.0;
        if (c.g > 0.0) {
            float d = shape_density(pos, c.g);
            if (lod_fine > 0.01)
                d = mix(d, detail_density(pos, d), lod_fine);
            dens = sharpen(d);
        }
        // Fondu pres de la camera : le joueur voit toujours son engin, meme dans le nuage.
        dens *= smoothstep(near_fade * 0.25, near_fade, t);

        if (dens > 0.005 && last_adv > st * 1.01) {
            t -= last_adv - st;
            fine_steps = int(ceil(last_adv / st)) + 1;
            last_adv = 0.0;
            thin_run = 0;
            continue;
        }
        thin_run = dens > 0.005 ? 0 : thin_run + 1;
        float adv = fine_steps > 0 ? st : st * min(1.0 + 0.25 * float(thin_run), 4.0);
        fine_steps = max(fine_steps - 1, 0);

        if (dens > 0.001) {
            if (first_hit < 0.0) first_hit = t;
            // Teinte d orage desactivee pour l instant (meme couleur que les autres nuages). Remettre 1.0 pour
            // assombrir le coeur du cumulonimbus. Le canal reste utilise par le gameplay (eclairs).
            float storm = c.b * 0.0;
            float sigma = dens * ext * (1.0 + storm * 2.0);
            float h = clamp((pos.y - slab_lo()) / 4000.0, 0.0, 1.0);

            // Part du geant dans cet echantillon (lue avant que les rayons d ombre ne l ecrasent) : sur le
            // cumulonimbus, transitions de lumiere a son echelle (plus douces), les autres nuages inchanges.
            float gg = g_giant;
            float od = light_optical_depth(pos, lod_shape > 0.5);
            // Diffusion multiple approchee (octaves de Wrenninge) : coeur moins noir, bords contrastes.
            float beer = exp(-od) + 0.1 * exp(-od * 0.25) + 0.03 * exp(-od * 0.06);
            float powder = 1.0 - exp(-2.0 * od - 0.1);
            // Ombre de la Terre : le soleil couche n eclaire que ce qui depasse le plan d ombre incline.
            float shadow_h = p.earth_shadow.x - dot(pos.xz, p.earth_shadow.zw) * p.earth_shadow.y;
            float shadow_soft = 60.0 + 900.0 * gg;
            float sunlit = smoothstep(-shadow_soft, shadow_soft, pos.y - shadow_h);
            vec3 sun_l = sun_col * beer * mix(1.0, powder, 0.6) * phase * (1.0 - 0.92 * storm) * sunlit;

            // Occlusion ambiante par le SDF : plus on est profond dans le nuage, moins le ciel eclaire.
            float ao = clamp(1.0 + sdf / (150.0 * (1.0 + 4.0 * gg)), 0.2, 1.0);
            // Bas des masses : eclaire par la mer sombre. Haut : par le ciel.
            float up_open = clamp((sample_cloud(pos + vec3(0.0, 80.0, 0.0)).r + 30.0) / 150.0, 0.0, 1.0);
            vec3 amb = mix(p.ambient_bottom_detail.rgb, p.ambient_top_threshold.rgb, mix(h, up_open, 0.7)) * ao;
            // Orage : masse sombre, bleu-gris ardoise.
            amb = mix(amb, amb * vec3(0.32, 0.3, 0.42), storm);

            // Flash d'eclair : lumiere ponctuelle froide qui illumine l'interieur du nuage.
            if (p.lightning.w > 0.0) {
                float ld = distance(pos, p.lightning.xyz);
                amb += vec3(0.75, 0.8, 1.0) * p.lightning.w * 22.0 * exp(-ld / 450.0);
            }

            // Perspective atmospherique par echantillon (et non au premier contact) : sinon, a travers le bord
            // fin d un nuage proche, un nuage lointain recevait le voile du proche et paraissait cerne de sombre.
            vec3 lum = mix(sun_l + amb, p.fog.rgb, 1.0 - exp(-t * p.fog.w));
            vec3 S = lum * sigma;
            float Ts = exp(-sigma * adv);
            float w = T * (1.0 - Ts);
            depth_sum += t * w;
            depth_w += w;
            L += T * (S - S * Ts) / max(sigma, 1e-5);   // integration conservative (Hillaire)
            T *= Ts;
            if (T < 0.01) { T = 0.0; break; }
        }
        t += adv;
        last_adv = adv;
    }

    if (!rain_first)
        march_rain(cam, dir, scene_dist, rain_jitter, L, T);


    float cloud_depth = depth_w > 1e-3 ? depth_sum / depth_w : scene_dist;
    imageStore(out_depth, px, vec4(scene_dist, cloud_depth, 0.0, 0.0));
    imageStore(out_cloud, px, vec4(L, T));
}
