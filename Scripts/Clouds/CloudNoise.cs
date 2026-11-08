using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Bruits 3D tuilables (Perlin et Worley) pour la génération des nuages.
/// Toutes les fonctions prennent des coordonnées en "cellules de réseau" et une période
/// entière par axe : le bruit se raccorde parfaitement aux bords de la période.
/// Thread-safe (aucun état mutable).
/// </summary>
public static class CloudNoise
{
    private static uint Hash(int x, int y, int z, uint seed)
    {
        unchecked
        {
            uint h = seed;
            h ^= (uint)x * 0x8da6b343u;
            h ^= (uint)y * 0xd8163841u;
            h ^= (uint)z * 0xcb1ab31fu;
            h ^= h >> 15;
            h *= 0x2c1b3c6du;
            h ^= h >> 12;
            h *= 0x297a2d39u;
            h ^= h >> 15;
            return h;
        }
    }

    private static int Wrap(int v, int period) => period <= 0 ? v : ((v % period) + period) % period;

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Grad(uint h, float x, float y, float z)
    {
        switch (h & 15u)
        {
            case 0: return x + y;
            case 1: return -x + y;
            case 2: return x - y;
            case 3: return -x - y;
            case 4: return x + z;
            case 5: return -x + z;
            case 6: return x - z;
            case 7: return -x - z;
            case 8: return y + z;
            case 9: return -y + z;
            case 10: return y - z;
            case 11: return -y - z;
            case 12: return x + y;
            case 13: return -x + y;
            case 14: return -y + z;
            default: return -y - z;
        }
    }

    /// <summary>Perlin 3D tuilable, résultat ≈ [-1, 1]. Période 0 = non tuilé sur cet axe.</summary>
    public static float Perlin(float x, float y, float z, int px, int py, int pz, uint seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
        float fx = x - x0, fy = y - y0, fz = z - z0;
        float u = Fade(fx), v = Fade(fy), w = Fade(fz);

        int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px);
        int ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
        int za = Wrap(z0, pz), zb = Wrap(z0 + 1, pz);

        float n000 = Grad(Hash(xa, ya, za, seed), fx, fy, fz);
        float n100 = Grad(Hash(xb, ya, za, seed), fx - 1, fy, fz);
        float n010 = Grad(Hash(xa, yb, za, seed), fx, fy - 1, fz);
        float n110 = Grad(Hash(xb, yb, za, seed), fx - 1, fy - 1, fz);
        float n001 = Grad(Hash(xa, ya, zb, seed), fx, fy, fz - 1);
        float n101 = Grad(Hash(xb, ya, zb, seed), fx - 1, fy, fz - 1);
        float n011 = Grad(Hash(xa, yb, zb, seed), fx, fy - 1, fz - 1);
        float n111 = Grad(Hash(xb, yb, zb, seed), fx - 1, fy - 1, fz - 1);

        float x00 = Mathf.Lerp(n000, n100, u), x10 = Mathf.Lerp(n010, n110, u);
        float x01 = Mathf.Lerp(n001, n101, u), x11 = Mathf.Lerp(n011, n111, u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    /// <summary>
    /// Worley (F1) 3D tuilable, résultat dans [0, 1] : 1 au point caractéristique, 0 loin de lui
    /// (forme "boursouflée" utile pour les nuages).
    /// </summary>
    public static float Worley(float x, float y, float z, int px, int py, int pz, uint seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
        float best = 9f;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = x0 + dx, cy = y0 + dy, cz = z0 + dz;
            uint h = Hash(Wrap(cx, px), Wrap(cy, py), Wrap(cz, pz), seed);
            float ox = (h & 0x3FFu) / 1023f;
            float oy = ((h >> 10) & 0x3FFu) / 1023f;
            float oz = ((h >> 20) & 0x3FFu) / 1023f;
            float ddx = cx + ox - x, ddy = cy + oy - y, ddz = cz + oz - z;
            float d = ddx * ddx + ddy * ddy + ddz * ddz;
            if (d < best) best = d;
        }
        return 1f - Mathf.Clamp(Mathf.Sqrt(best), 0f, 1f);
    }

    /// <summary>FBM de Perlin tuilable, résultat ≈ [0, 1]. La période double à chaque octave.</summary>
    public static float PerlinFbm(float x, float y, float z, int px, int py, int pz, int octaves, uint seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Perlin(x, y, z, px, py, pz, seed + (uint)o * 101u) * amp;
            norm += amp;
            x *= 2f; y *= 2f; z *= 2f;
            px *= 2; py *= 2; pz *= 2;
            amp *= 0.5f;
        }
        return Mathf.Clamp(sum / norm * 0.5f + 0.5f, 0f, 1f);
    }

    /// <summary>FBM de Worley tuilable, résultat dans [0, 1].</summary>
    public static float WorleyFbm(float x, float y, float z, int px, int py, int pz, int octaves, uint seed)
    {
        float sum = 0f, amp = 0.625f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += Worley(x, y, z, px, py, pz, seed + (uint)o * 977u) * amp;
            norm += amp;
            x *= 2f; y *= 2f; z *= 2f;
            px *= 2; py *= 2; pz *= 2;
            amp *= 0.5f;
        }
        return sum / norm;
    }
}
