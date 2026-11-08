using System;
using Bomba.Clouds;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// Sonde du champ de nuages : valide le SDF contre un calcul brut, mesure l'API gameplay et
/// exporte des vues PNG (projection du dessus, coupe verticale) dans le dossier donné par
/// l'argument utilisateur <c>--out=CHEMIN</c>. Puis quitte.
/// <c>godot --headless --path . res://Scenes/Debug/CloudProbe.tscn -- --out=C:/tmp</c>
/// </summary>
public partial class CloudProbe : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private void Run()
    {
        CloudField f = CloudField.Instance;
        if (f == null || !f.IsBaked)
        {
            GD.PrintErr("CloudProbe : pas de CloudField calculé.");
            GetTree().Quit(1);
            return;
        }

        string outDir = "user://";
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out="))
                outDir = arg.Substring(6);

        ValidateSdf(f);
        ProbeApi(f);
        ExportViews(f, outDir);
        GetTree().Quit();
    }

    /// <summary>Compare le SDF interpolé à la distance brute (au centre de voxel le plus proche).</summary>
    private static void ValidateSdf(CloudField f)
    {
        var rng = new RandomNumberGenerator { Seed = 42 };
        int overestimates = 0, checkedPts = 0;
        float worst = 0f;
        for (int n = 0; n < 60; n++)
        {
            var p = new Vector3(rng.RandfRange(0, f.TileSize), rng.RandfRange(f.MinAltitude + 10, f.MaxAltitude - 10), rng.RandfRange(0, f.TileSize));
            CloudSample s = f.Sample(p);
            if (s.Sdf <= 0f)
                continue;
            float brute = BruteDistance(f, p, s.Sdf + 3f * f.CellXZ);
            checkedPts++;
            // Le SDF ne doit jamais dépasser la vraie distance (sinon le ray marching traverse le nuage).
            float excess = s.Sdf - brute;
            if (excess > 0.5f) overestimates++;
            worst = Mathf.Max(worst, excess);
        }
        GD.Print($"[SDF] {checkedPts} points hors nuage testés, surestimations {overestimates}, pire excès {worst:F1} m (doit être ≤ 0)");
    }

    private static float BruteDistance(CloudField f, Vector3 p, float maxSearch)
    {
        float best = float.MaxValue;
        float step = f.CellXZ * 0.5f;
        int r = Mathf.CeilToInt(maxSearch / step);
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        for (float dy = -maxSearch; dy <= maxSearch; dy += f.CellY * 0.5f)
        {
            var q = p + new Vector3(dx * step, dy, dz * step);
            if (q.Y < f.MinAltitude || q.Y > f.MaxAltitude) continue;
            if (f.Sample(q).Density > f.DensityThreshold)
                best = Mathf.Min(best, p.DistanceTo(q));
        }
        return best;
    }

    private static void ProbeApi(CloudField f)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        float sum = 0f;
        for (int i = 0; i < 1000; i++)
            sum += f.GetTransmittance(new Vector3(i, 100, 0), new Vector3(i + 300, 40, 300));
        GD.Print($"[API] 1000 transmittances en {sw.ElapsedMilliseconds} ms (moyenne {sum / 1000f:F2})");
        GD.Print($"[API] spawn (0,200,0) : {Describe(f.Sample(new Vector3(0, 200, 0)))}");
        GD.Print($"[API] dans la couche (0,400,0) : {Describe(f.Sample(new Vector3(0, 400, 0)))}");
    }

    private static string Describe(CloudSample s) =>
        $"sdf {s.Sdf:F1} m, densité {s.Density:F2}, orage {s.Storm:F2}, ascendance {s.Updraft:F2}";

    /// <summary>Exporte la vue du dessus (densité intégrée, orage en rouge, ascendance en vert) et une coupe XY.</summary>
    private static void ExportViews(CloudField f, string outDir)
    {
        int n = 256;
        var top = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
        for (int zi = 0; zi < n; zi++)
        for (int xi = 0; xi < n; xi++)
        {
            float x = (xi + 0.5f) / n * f.TileSize, z = (zi + 0.5f) / n * f.TileSize;
            float tau = 0f, storm = 0f, up = 0f;
            for (float y = f.MinAltitude; y < f.MaxAltitude; y += 25f)
            {
                CloudSample s = f.Sample(new Vector3(x, y, z));
                tau += s.Density * 0.004f * 25f;
                storm = Mathf.Max(storm, s.Storm);
                up = Mathf.Max(up, s.Updraft);
            }
            float a = 1f - Mathf.Exp(-tau);
            top.SetPixel(xi, zi, new Color(a + storm * 0.6f, a + up * 0.25f, a));
        }
        Save(top, outDir, "clouds_top.png");

        int h = 120;
        var side = Image.CreateEmpty(n, h, false, Image.Format.Rgb8);
        float zSlice = f.TileSize * 0.5f;
        // Coupe passant par une tour : on cherche la colonne la plus haute.
        float bestTop = 0f;
        for (float z = 0; z < f.TileSize; z += 64f)
        for (float x = 0; x < f.TileSize; x += 64f)
        {
            if (f.Sample(new Vector3(x, 2500f, z)).Density > 0.1f && bestTop < 1f) { zSlice = z; bestTop = 1f; }
        }
        for (int yi = 0; yi < h; yi++)
        for (int xi = 0; xi < n; xi++)
        {
            float x = (xi + 0.5f) / n * f.TileSize;
            float y = f.MaxAltitude - (yi + 0.5f) / h * (f.MaxAltitude - f.MinAltitude);
            CloudSample s = f.Sample(new Vector3(x, y, zSlice));
            float sdfBand = s.Sdf > 0 ? Mathf.Clamp(1f - s.Sdf / 1000f, 0f, 1f) * 0.25f : 0f;
            side.SetPixel(xi, yi, new Color(s.Density + s.Storm * 0.5f, s.Density + sdfBand, s.Density + sdfBand * 2f));
        }
        Save(side, outDir, "clouds_side.png");
    }

    private static void Save(Image img, string dir, string name)
    {
        string path = dir.TrimEnd('/', '\\') + "/" + name;
        Error err = img.SavePng(path);
        GD.Print($"[PNG] {path} : {err}");
    }
}
