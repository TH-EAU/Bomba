using System;
using System.Threading.Tasks;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Grille 3D de nuages tuilable horizontalement, à maillage vertical non uniforme
/// (altitude = min + H·v^k). Porte quatre canaux (densité, orage, ascendance, puis SDF calculé),
/// sait calculer son SDF exact (Felzenszwalb), s'échantillonner sur CPU et produire ses données
/// de texture RGBA16F (R = SDF m, G = densité, B = orage, A = ascendance).
/// Le remplissage des canaux est fait par <see cref="CloudField"/>.
/// </summary>
public sealed class CloudGrid
{
    public readonly int SizeX, SizeY, SizeZ;
    public readonly float TileSize, MinAltitude, MaxAltitude, VerticalExponent;

    public readonly float[] Density, Storm, Updraft;
    public float[] Sdf { get; private set; }
    public byte[] TextureData { get; private set; }

    /// <summary>Vrai : la grille se répète à l'infini. Faux : un seul exemplaire, dans [Origin, Origin + TileSize].</summary>
    public bool Repeat = true;
    /// <summary>Coin du domaine dans le monde (hors dérive), utile si <see cref="Repeat"/> est faux.</summary>
    public Vector2 Origin;

    public CloudGrid(int sizeX, int sizeY, int sizeZ, float tileSize, float minAltitude, float maxAltitude, float verticalExponent)
    {
        SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        TileSize = tileSize; MinAltitude = minAltitude; MaxAltitude = maxAltitude; VerticalExponent = verticalExponent;
        int count = sizeX * sizeY * sizeZ;
        Density = new float[count];
        Storm = new float[count];
        Updraft = new float[count];
    }

    public int Count => SizeX * SizeY * SizeZ;
    public float CellXZ => TileSize / SizeX;
    /// <summary>Hauteur maximale d'une cellule (en haut du volume).</summary>
    public float CellY => (MaxAltitude - MinAltitude) / SizeY * VerticalExponent;

    public int Index(int i, int j, int k) => i + SizeX * (j + SizeY * k);

    public float VerticalCoord(float y) =>
        Mathf.Pow(Mathf.Clamp((y - MinAltitude) / (MaxAltitude - MinAltitude), 0f, 1f), 1f / VerticalExponent);

    public float AltitudeAt(float v) =>
        MinAltitude + (MaxAltitude - MinAltitude) * Mathf.Pow(Mathf.Clamp(v, 0f, 1f), VerticalExponent);

    public float CellCenterY(int j) => AltitudeAt((j + 0.5f) / SizeY);

    /// <summary>Échantillon trilinéaire au point monde (dérive comprise). Hors tranche : SDF = distance à la tranche.</summary>
    public CloudSample Sample(Vector3 worldPos, Vector2 offset)
    {
        if (Sdf == null)
            return new CloudSample { Sdf = 1e6f };

        // Coordonnées dans la grille (dérive et origine déduites).
        float lx = worldPos.X - offset.X - Origin.X;
        float lz = worldPos.Z - offset.Y - Origin.Y;

        // Hors du domaine : vide, SDF = distance exacte au domaine (ne surestime jamais).
        float vgap = worldPos.Y < MinAltitude ? MinAltitude - worldPos.Y : Mathf.Max(worldPos.Y - MaxAltitude, 0f);
        float hgap = 0f;
        if (!Repeat)
        {
            float gx = Mathf.Max(Mathf.Max(-lx, lx - TileSize), 0f);
            float gz = Mathf.Max(Mathf.Max(-lz, lz - TileSize), 0f);
            hgap = Mathf.Sqrt(gx * gx + gz * gz);
        }
        if (vgap > 0f || hgap > 0f)
            return new CloudSample { Sdf = Mathf.Sqrt(vgap * vgap + hgap * hgap) };

        float fx = lx / CellXZ - 0.5f;
        float fy = VerticalCoord(worldPos.Y) * SizeY - 0.5f;
        float fz = lz / CellXZ - 0.5f;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy), z0 = Mathf.FloorToInt(fz);
        float tx = fx - x0, ty = fy - y0, tz = fz - z0;
        int xa = Mod(x0, SizeX), xb = Mod(x0 + 1, SizeX);
        int za = Mod(z0, SizeZ), zb = Mod(z0 + 1, SizeZ);
        int ya = Math.Clamp(y0, 0, SizeY - 1), yb = Math.Clamp(y0 + 1, 0, SizeY - 1);

        return new CloudSample
        {
            Sdf = Trilinear(Sdf, xa, xb, ya, yb, za, zb, tx, ty, tz),
            Density = Trilinear(Density, xa, xb, ya, yb, za, zb, tx, ty, tz),
            Storm = Trilinear(Storm, xa, xb, ya, yb, za, zb, tx, ty, tz),
            Updraft = Trilinear(Updraft, xa, xb, ya, yb, za, zb, tx, ty, tz),
        };
    }

    /// <summary>Calcule le SDF (m, négatif dedans, conservateur) puis les données de texture.</summary>
    public void Finish(float densityThreshold)
    {
        int count = Count;
        var featIn = new double[count];
        var featOut = new double[count];
        const double inf = 1e20;
        for (int i = 0; i < count; i++)
        {
            bool inside = Density[i] > densityThreshold;
            featIn[i] = inside ? 0 : inf;
            featOut[i] = inside ? inf : 0;
        }
        Edt3D(featIn);
        Edt3D(featOut);

        var sdf = new float[count];
        float margin = CellXZ; // conservateur : ne jamais surestimer la distance au nuage
        for (int i = 0; i < count; i++)
        {
            bool inside = Density[i] > densityThreshold;
            sdf[i] = inside ? -(float)Math.Sqrt(featOut[i]) : Mathf.Max((float)Math.Sqrt(featIn[i]) - margin, 0f);
        }
        Sdf = sdf;
        TextureData = BuildTextureData();
    }

    /// <summary>Texture 3D pour les shaders spatiaux (mêmes canaux que le rendu).</summary>
    public ImageTexture3D CreateTexture3D()
    {
        var slices = new Godot.Collections.Array<Image>();
        int sliceBytes = SizeX * SizeY * 8;
        for (int k = 0; k < SizeZ; k++)
        {
            var data = new byte[sliceBytes];
            Buffer.BlockCopy(TextureData, k * sliceBytes, data, 0, sliceBytes);
            slices.Add(Image.CreateFromData(SizeX, SizeY, false, Image.Format.Rgbah, data));
        }
        var tex = new ImageTexture3D();
        tex.Create(Image.Format.Rgbah, SizeX, SizeY, SizeZ, false, slices);
        return tex;
    }

    private void Edt3D(double[] grid)
    {
        double cell = CellXZ;
        Parallel.For(0, SizeY * SizeZ, row =>
        {
            int j = row % SizeY, k = row / SizeY;
            var line = new double[SizeX];
            for (int i = 0; i < SizeX; i++) line[i] = grid[Index(i, j, k)];
            EdtPeriodic(line, cell);
            for (int i = 0; i < SizeX; i++) grid[Index(i, j, k)] = line[i];
        });
        var yPos = new double[SizeY];
        for (int j = 0; j < SizeY; j++) yPos[j] = CellCenterY(j);
        Parallel.For(0, SizeX * SizeZ, row =>
        {
            int i = row % SizeX, k = row / SizeX;
            var line = new double[SizeY];
            for (int j = 0; j < SizeY; j++) line[j] = grid[Index(i, j, k)];
            Edt(line, yPos);
            for (int j = 0; j < SizeY; j++) grid[Index(i, j, k)] = line[j];
        });
        Parallel.For(0, SizeX * SizeY, row =>
        {
            int i = row % SizeX, j = row / SizeX;
            var line = new double[SizeZ];
            for (int k = 0; k < SizeZ; k++) line[k] = grid[Index(i, j, k)];
            EdtPeriodic(line, cell);
            for (int k = 0; k < SizeZ; k++) grid[Index(i, j, k)] = line[k];
        });
    }

    /// <summary>EDT 1D au carré sur une ligne périodique de pas constant (ligne triplée).</summary>
    private static void EdtPeriodic(double[] f, double spacing)
    {
        int n0 = f.Length, n = n0 * 3;
        var src = new double[n];
        var pos = new double[n];
        for (int q = 0; q < n; q++) { src[q] = f[q % n0]; pos[q] = q * spacing; }
        Edt(src, pos);
        Array.Copy(src, n0, f, 0, n0);
    }

    /// <summary>EDT 1D au carré (Felzenszwalb) sur des positions croissantes quelconques (m).</summary>
    private static void Edt(double[] f, double[] pos)
    {
        int n = f.Length;
        var v = new int[n];
        var z = new double[n + 1];
        var d = new double[n];
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = Intersect(f, pos, q, v[k]);
            while (s <= z[k])
            {
                k--;
                s = Intersect(f, pos, q, v[k]);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < pos[q]) k++;
            double diff = pos[q] - pos[v[k]];
            d[q] = diff * diff + f[v[k]];
        }
        Array.Copy(d, f, n);
    }

    private static double Intersect(double[] f, double[] pos, int q, int p) =>
        ((f[q] + pos[q] * pos[q]) - (f[p] + pos[p] * pos[p])) / (2.0 * (pos[q] - pos[p]));

    private byte[] BuildTextureData()
    {
        int count = Count;
        var bytes = new byte[count * 8];
        Parallel.For(0, SizeZ, k =>
        {
            for (int j = 0; j < SizeY; j++)
            for (int i = 0; i < SizeX; i++)
            {
                int idx = Index(i, j, k);
                int o = idx * 8;
                Write(bytes, o, Sdf[idx]);
                Write(bytes, o + 2, Density[idx]);
                Write(bytes, o + 4, Storm[idx]);
                Write(bytes, o + 6, Updraft[idx]);
            }
        });
        return bytes;
    }

    private static void Write(byte[] bytes, int o, float v)
    {
        ushort bits = BitConverter.HalfToUInt16Bits((Half)v);
        bytes[o] = (byte)(bits & 0xFF);
        bytes[o + 1] = (byte)(bits >> 8);
    }

    private static int Mod(int v, int m) => ((v % m) + m) % m;

    private float Trilinear(float[] g, int xa, int xb, int ya, int yb, int za, int zb, float tx, float ty, float tz)
    {
        float c000 = g[Index(xa, ya, za)], c100 = g[Index(xb, ya, za)];
        float c010 = g[Index(xa, yb, za)], c110 = g[Index(xb, yb, za)];
        float c001 = g[Index(xa, ya, zb)], c101 = g[Index(xb, ya, zb)];
        float c011 = g[Index(xa, yb, zb)], c111 = g[Index(xb, yb, zb)];
        float x00 = Mathf.Lerp(c000, c100, tx), x10 = Mathf.Lerp(c010, c110, tx);
        float x01 = Mathf.Lerp(c001, c101, tx), x11 = Mathf.Lerp(c011, c111, tx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
    }
}
