#[compute]
#version 450

// Copie de l'image (lecture separee de l'ecriture pour le flou de mouvement).

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba16f, set = 0, binding = 0) uniform restrict readonly image2D src_image;
layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D dst_image;

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(src_image);
    if (px.x >= size.x || px.y >= size.y)
        return;
    imageStore(dst_image, px, imageLoad(src_image, px));
}
