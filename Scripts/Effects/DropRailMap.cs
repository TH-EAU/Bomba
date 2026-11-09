using System;
using System.Collections.Generic;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Prépare la carte des rails de gouttes (image noir et blanc, rails blancs) pour
/// <c>screen_droplets.gdshader</c>. Le calcul se fait une fois, puis il est mis en cache par chemin.
/// R = masque flouté : profil bombé, plus haut sur les parties épaisses.
/// G = phase, B = vitesse, A = seuil de présence : trois valeurs aléatoires par rail (composante connexe).
/// Les identifiants débordent de quelques pixels autour du rail pour que les bords restent stables.
/// </summary>
public static class DropRailMap
{
    private const int BlurRadius = 3;   // trois passes de boîte ≈ gaussienne de sigma ~3,5 px
    private const int BlurPasses = 3;
    private const int IdReach = 8;      // pixels de débordement des identifiants autour d'un rail

    private static readonly Dictionary<string, ImageTexture> Cache = new();

    public static ImageTexture Get(Texture2D source, ulong seed = 1)
    {
        string key = source.ResourcePath + "#" + seed;
        if (Cache.TryGetValue(key, out ImageTexture cached) && GodotObject.IsInstanceValid(cached))
            return cached;
        ImageTexture tex = ImageTexture.CreateFromImage(Build(source.GetImage(), seed));
        Cache[key] = tex;
        return tex;
    }

    private static Image Build(Image src, ulong seed)
    {
        Image img = (Image)src.Duplicate();
        if (img.IsCompressed())
            img.Decompress();
        img.ClearMipmaps();
        img.Convert(Image.Format.L8);
        int w = img.GetWidth(), h = img.GetHeight(), n = w * h;
        byte[] lum = img.GetData();

        int[] label = new int[n];
        int count = LabelRails(lum, label, w, h);
        DilateLabels(label, w, h);
        float[] thick = Blur(lum, w, h);

        var rng = new RandomNumberGenerator { Seed = seed };
        var props = new byte[(count + 1) * 3];
        for (int i = 3; i < props.Length; i++)
            props[i] = (byte)rng.RandiRange(0, 255);

        byte[] rgba = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            int l = label[i];
            rgba[i * 4] = (byte)Mathf.Clamp(thick[i] * 255f, 0f, 255f);
            rgba[i * 4 + 1] = l > 0 ? props[l * 3] : (byte)0;
            rgba[i * 4 + 2] = l > 0 ? props[l * 3 + 1] : (byte)0;
            rgba[i * 4 + 3] = l > 0 ? props[l * 3 + 2] : (byte)255;
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
    }

    // Composantes connexes (8-voisinage) des pixels blancs. Retourne le nombre de rails.
    private static int LabelRails(byte[] lum, int[] label, int w, int h)
    {
        var queue = new Queue<int>();
        int next = 0;
        for (int start = 0; start < lum.Length; start++)
        {
            if (lum[start] < 128 || label[start] != 0)
                continue;
            label[start] = ++next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    if (lum[j] >= 128 && label[j] == 0)
                    {
                        label[j] = next;
                        queue.Enqueue(j);
                    }
                }
            }
        }
        return next;
    }

    // Propage chaque identifiant de rail sur IdReach pixels autour (BFS multi-source).
    private static void DilateLabels(int[] label, int w, int h)
    {
        var dist = new byte[label.Length];
        var queue = new Queue<int>();
        for (int i = 0; i < label.Length; i++)
            if (label[i] != 0)
                queue.Enqueue(i);
        Span<int> nb = stackalloc int[4];
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            if (dist[i] >= IdReach)
                continue;
            int x = i % w, y = i / w;
            nb[0] = x > 0 ? i - 1 : -1;
            nb[1] = x < w - 1 ? i + 1 : -1;
            nb[2] = y > 0 ? i - w : -1;
            nb[3] = y < h - 1 ? i + w : -1;
            foreach (int j in nb)
            {
                if (j < 0 || label[j] != 0)
                    continue;
                label[j] = label[i];
                dist[j] = (byte)(dist[i] + 1);
                queue.Enqueue(j);
            }
        }
    }

    // Flou de boîte séparable répété : approxime une gaussienne. Sortie dans [0, 1].
    private static float[] Blur(byte[] lum, int w, int h)
    {
        var a = new float[lum.Length];
        var b = new float[lum.Length];
        for (int i = 0; i < lum.Length; i++)
            a[i] = lum[i] / 255f;
        for (int pass = 0; pass < BlurPasses; pass++)
        {
            for (int y = 0; y < h; y++)
                BoxLine(a, b, y * w, 1, w);
            for (int x = 0; x < w; x++)
                BoxLine(b, a, x, w, h);
        }
        return a;
    }

    // Moyenne glissante sur une ligne (bords répétés).
    private static void BoxLine(float[] src, float[] dst, int start, int stride, int len)
    {
        float norm = 1f / (2 * BlurRadius + 1);
        float s = 0f;
        for (int k = -BlurRadius; k <= BlurRadius; k++)
            s += src[start + Math.Clamp(k, 0, len - 1) * stride];
        for (int i = 0; i < len; i++)
        {
            dst[start + i * stride] = s * norm;
            s += src[start + Math.Min(i + BlurRadius + 1, len - 1) * stride];
            s -= src[start + Math.Max(i - BlurRadius, 0) * stride];
        }
    }
}
