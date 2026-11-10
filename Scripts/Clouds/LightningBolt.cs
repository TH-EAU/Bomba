using System.Collections.Generic;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Canal de foudre visible et propagé, rendu par <c>Shaders/Effects/lightning_channel.gdshader</c> (rubans additifs
/// HDR tournés vers la caméra, largeur minimale en pixels). Un traceur faible avance le long du canal (tête brillante),
/// puis l'arc en retour l'illumine en entier, scintille et s'éteint. Le nœud se libère seul.
/// Le tracé est une spline Catmull-Rom effilée (tronc épais, branches fines). L'atténuation par les nuages situés
/// devant (caméra → point) est estimée une fois à la création : un canal derrière un nuage épais s'éteint.
/// Deux formes :
/// - <see cref="Setup"/> : éclair ramifié d'un point à un autre (vers le bas, ou vers le joueur) ;
/// - <see cref="SetupSpider"/> : éclair qui sort du cœur du nuage puis rampe à l'air libre contre ses parois
///   ou sous l'enclume (dans le nuage, un canal serait masqué).
/// </summary>
public partial class LightningBolt : MeshInstance3D
{
    private const string ShaderPath = "res://Shaders/Effects/lightning_channel.gdshader";

    // Traceur : vitesses visuelles (la vraie foudre est bien trop rapide pour que l'œil voie la propagation).
    private const float ForkLeaderSpeed = 30000f;     // m/s
    private const float SpiderLeaderSpeed = 30000f;
    // Arc en retour : éclat, creux, second éclat, extinction.
    private const float StrokeLife = 0.5f;

    // Forme ramifiée (éclair vers une cible).
    private const float Roughness = 0.25f;
    private const int MainLevels = 6;
    private const int BranchLevels = 4;

    // Éclair dans le nuage.
    private const float SpiderStep = 400f;            // m
    private const float EdgeDensity = 0.03f;          // densité visée (CPU, non érodée) : la surface visible du nuage
    private const float MinDensity = 0.005f;          // en dessous, hors du nuage
    private const float SpiderBranchChance = 0.22f;
    private const int MaxSegments = 2100;   // x3 : le lissage Catmull-Rom triple les segments

    // Lissage : points intercalés entre deux points d'origine (Catmull-Rom).
    private const int SplineSubdivisions = 3;

    private readonly List<Vector3> _points = new();
    private readonly List<float> _distance = new();   // longueur de canal depuis l'origine (m)
    private readonly List<float> _width = new();      // largeur relative du canal en ce point
    private readonly List<float> _follow = new();     // part du déplacement de la cible suivie par ce point (0..1)
    private readonly List<bool> _branch = new();      // branche (sinon tronc) : faible à l'arc en retour
    private readonly List<(int A, int B)> _segments = new();

    private ShaderMaterial _material;
    private float _age;
    private float _leaderEnd;
    private int _anchoredTip = -1;   // pointe qui touche quelque chose : pas affinée
    // Pointes libres : la largeur tombe à zéro sur les derniers points (sinon le ruban finit carré, et le glow l'élargit).
    private const int TipTaperPoints = 9;

    // Frappe : cible suivie par le canal (coup inévitable).
    private Node3D _target;
    private Vector3 _targetLocal, _impactStart;
    private ShaderMaterial _impactMaterial;   // éclat au point d'impact : masque la jonction canal / coque
    private const string ImpactShaderPath = "res://Shaders/Effects/lightning_impact.gdshader";
    private const float ImpactSize = 1.2f;   // m

    // Traceur par bonds : le canal avance par sauts, mince et faible, puis l'arc en retour le remplit.
    private const float StepTime = 0.035f;
    private const float LeaderWidth = 0.3f;
    private const float BranchFlash = 0.15f;

    public override void _Ready() => TopLevel = true;

    private (float Energy, float Width, float MinPixels, Color Color)? _look;

    /// <summary>Aspect (voir le groupe « Rendu » de LightningSystem) : à appeler avant ou après Setup.</summary>
    public void SetLook(float energy, float width, float minPixels, Color color)
    {
        _look = (energy, width, minPixels, color);
        if (_material != null)
            ApplyLook();
    }

    private void ApplyLook()
    {
        var (energy, width, minPixels, color) = _look.Value;
        _material.SetShaderParameter("energy", energy);
        _material.SetShaderParameter("core_width", width);
        _material.SetShaderParameter("min_core_px", minPixels);
        _material.SetShaderParameter("bolt_color", new Vector3(color.R, color.G, color.B));
    }

    /// <summary>Éclair ramifié de <paramref name="from"/> à <paramref name="to"/> (coordonnées globales).</summary>
    /// <param name="touches">Vrai si le canal touche quelque chose (la mer) : sa pointe reste pleine, sinon elle s'affine.</param>
    public void Setup(Vector3 from, Vector3 to, RandomNumberGenerator rng, bool touches = false)
    {
        int root = AddPoint(from, 0f, 3f);
        List<Vector3> trunk = Subdivide(from, to, MainLevels, rng);
        List<int> trunkIdx = AddChain(root, trunk, 3f);

        Vector3 mainDir = (to - from).Normalized();
        float totalLength = from.DistanceTo(to);
        int branchCount = rng.RandiRange(2, 5);
        for (int b = 0; b < branchCount; b++)
        {
            // Ni dans le premier quart, ni dans le dernier quart du tracé principal.
            int at = trunkIdx[rng.RandiRange(trunkIdx.Count / 4, trunkIdx.Count * 3 / 4)];
            Vector3 dir = mainDir.Rotated(Perpendicular(mainDir, rng), Mathf.DegToRad(rng.RandfRange(25f, 50f)));
            Vector3 start = _points[at];
            AddChain(at, Subdivide(start, start + dir * totalLength * rng.RandfRange(0.15f, 0.35f), BranchLevels, rng), 1f, true);
        }
        if (touches)
            _anchoredTip = trunkIdx[^1];
        AddSearchBranches(trunkIdx, mainDir, totalLength, rng);
        // Traceur visible : 0,25 à 0,5 s selon la longueur (la vraie foudre est bien plus rapide).
        FinishLeader(trunkIdx[^1], Mathf.Clamp(totalLength / 12000f, 0.25f, 0.5f));
    }

    /// <summary>
    /// Frappe sur une cible mobile : traceur par bonds depuis <paramref name="from"/>, entouré de filets qui cherchent
    /// leur chemin, puis arc en retour au bout de <paramref name="leaderTime"/> s. Le canal se tord pour suivre le point
    /// <paramref name="targetLocal"/> de <paramref name="target"/> : le coup est inévitable.
    /// </summary>
    public void SetupStrike(Vector3 from, Node3D target, Vector3 targetLocal, float leaderTime, RandomNumberGenerator rng)
    {
        _target = target;
        _targetLocal = targetLocal;
        _impactStart = target.GlobalTransform * targetLocal;

        int root = AddPoint(from, 0f, 3f);
        // Un niveau de plus : vu de près, le canal reste anguleux jusqu'au point d'impact.
        List<int> trunkIdx = AddChain(root, Subdivide(from, _impactStart, MainLevels + 1, rng), 3f);
        // Suivi : nul à l'origine (dans le nuage), total au point d'impact, en t² pour que le haut du canal bouge peu.
        for (int i = 0; i < trunkIdx.Count; i++)
        {
            float t = (float)i / (trunkIdx.Count - 1);
            _follow[trunkIdx[i]] = t * t;
        }

        Vector3 mainDir = (_impactStart - from).Normalized();
        float totalLength = from.DistanceTo(_impactStart);
        _anchoredTip = trunkIdx[^1];   // le point d'impact touche la coque
        AddSearchBranches(trunkIdx, mainDir, totalLength, rng);
        FinishLeader(trunkIdx[^1], leaderTime);

        _impactMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(ImpactShaderPath) };
        if (_look.HasValue)
        {
            _impactMaterial.SetShaderParameter("energy", _look.Value.Energy);
            _impactMaterial.SetShaderParameter("bolt_color", new Vector3(_look.Value.Color.R, _look.Value.Color.G, _look.Value.Color.B));
        }
        var impact = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(ImpactSize, ImpactSize) },
            MaterialOverride = _impactMaterial,
            CastShadow = ShadowCastingSetting.Off,
            TopLevel = true,
        };
        AddChild(impact);
        impact.GlobalPosition = _impactStart;
    }

    /// <summary>
    /// Filets du traceur : branches mortes, fines, réparties sur tout le tronc et orientées comme lui. Bien visibles
    /// pendant la recherche, presque éteintes à l'arc en retour.
    /// </summary>
    private void AddSearchBranches(List<int> trunkIdx, Vector3 mainDir, float totalLength, RandomNumberGenerator rng)
    {
        int count = rng.RandiRange(6, 10);
        for (int b = 0; b < count; b++)
        {
            int at = trunkIdx[rng.RandiRange(trunkIdx.Count / 10, trunkIdx.Count * 19 / 20)];
            Vector3 dir = mainDir.Rotated(Perpendicular(mainDir, rng), Mathf.DegToRad(rng.RandfRange(20f, 60f)));
            Vector3 start = _points[at];
            AddChain(at, Subdivide(start, start + dir * totalLength * rng.RandfRange(0.06f, 0.2f), BranchLevels, rng), 0.4f, true);
        }
    }

    /// <summary>Éclair qui parcourt le nuage depuis <paramref name="start"/>, en longeant sa frange.</summary>
    public void SetupSpider(Vector3 start, CloudField field, RandomNumberGenerator rng)
    {
        int root = AddPoint(start, 0f, 1.5f);
        var tips = new Queue<(int Index, Vector3 Dir, float Budget, int Generation)>();
        int arms = rng.RandiRange(2, 4);
        for (int i = 0; i < arms; i++)
            tips.Enqueue((root, Vector3.Right.Rotated(Vector3.Up, rng.Randf() * Mathf.Tau), rng.RandfRange(6000f, 12000f), 0));

        while (tips.Count > 0 && _segments.Count < MaxSegments)
        {
            var tip = tips.Dequeue();
            if (tip.Budget <= 0f)
                continue;
            Vector3 p = _points[tip.Index];

            // Parmi quelques directions voisines (surtout horizontales), celle qui reste dans le nuage au plus près
            // de sa frange : le canal sort du cœur puis rampe le long du bord.
            Vector3 best = Vector3.Zero;
            float bestScore = float.MaxValue;
            for (int k = 0; k < 6; k++)
            {
                Vector3 d = Wiggle(tip.Dir, rng, 20f, 8f);
                Vector3 q = p + d * SpiderStep;
                float dens = field.GetDensity(q);
                // Dans le nuage, le canal est masqué (il s'opacifie en quelques dizaines de mètres) : il sort d'abord
                // du cœur (densité la plus faible), puis rampe à l'air libre contre la paroi ou sous l'enclume.
                float score;
                if (dens >= MinDensity)
                    score = 1f + dens;
                else if (Mathf.Max(field.GetDensity(q + Vector3.Up * 250f), field.GetDensity(q + d * 250f)) >= EdgeDensity)
                    score = rng.Randf() * 0.1f;
                else
                    continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }
            if (best == Vector3.Zero)
                continue;

            float width = tip.Generation == 0 ? 1.5f : tip.Generation == 1 ? 0.8f : 0.5f;
            List<Vector3> path = Subdivide(p, p + best * SpiderStep, 2, rng);
            List<int> chain = AddChain(tip.Index, path, width);
            int end = chain[^1];
            tips.Enqueue((end, best, tip.Budget - SpiderStep, tip.Generation));
            if (tip.Generation < 3 && rng.Randf() < SpiderBranchChance)
                tips.Enqueue((end, Wiggle(best, rng, 65f, 8f), tip.Budget * 0.6f, tip.Generation + 1));
        }
        Finish(SpiderLeaderSpeed);
    }

    public override void _Process(double delta)
    {
        if (_material == null)
            return;
        _age += (float)delta;
        float t = _age - _leaderEnd;
        if (t > StrokeLife)
        {
            QueueFree();
            return;
        }
        // Arc en retour : éclat 0,06 s, creux 0,04 s, second éclat 0,1 s, puis extinction.
        float flash = t < 0f ? 0f : t < 0.06f ? 1f : t < 0.1f ? 0.2f : t < 0.2f ? 0.9f : 0.9f * (1f - (t - 0.2f) / (StrokeLife - 0.2f));
        if (_target != null && IsInstanceValid(_target))
            _material.SetShaderParameter("target_offset", _target.GetGlobalTransformInterpolated() * _targetLocal - _impactStart);
        _material.SetShaderParameter("progress", _age);
        _material.SetShaderParameter("flash", Mathf.Max(flash, 0f));
        if (_impactMaterial != null)
        {
            _impactMaterial.SetShaderParameter("flash", Mathf.Max(flash, 0f));
            if (_target != null && IsInstanceValid(_target) && GetChildCount() > 0 && GetChild(0) is MeshInstance3D impact)
                impact.GlobalPosition = _target.GetGlobalTransformInterpolated() * _targetLocal;
        }
    }

    private int AddPoint(Vector3 p, float distance, float width, float follow = 0f, bool branch = false)
    {
        _points.Add(p);
        _distance.Add(distance);
        _width.Add(width);
        _follow.Add(follow);
        _branch.Add(branch);
        return _points.Count - 1;
    }

    /// <summary>
    /// Relie <paramref name="from"/> aux points de <paramref name="path"/> (le premier est le point de départ), lissé en
    /// spline. La largeur décroît linéairement de <paramref name="width"/> au départ à 30 % à l'arrivée.
    /// </summary>
    private List<int> AddChain(int from, List<Vector3> path, float width, bool branch = false)
    {
        path = Smooth(path, SplineSubdivisions);
        var indices = new List<int> { from };
        int prev = from;
        for (int i = 1; i < path.Count; i++)
        {
            float w = width * (1f - 0.7f * i / (path.Count - 1));
            // Une branche suit la cible comme son point d'attache.
            int next = AddPoint(path[i], _distance[prev] + _points[prev].DistanceTo(path[i]), w, _follow[from], branch);
            _segments.Add((prev, next));
            indices.Add(next);
            prev = next;
        }
        return indices;
    }

    // Atténuation par les nuages situés entre la caméra et le canal. Le rendu des nuages, en demi-résolution,
    // ne peut pas masquer un trait de 1 à 2 px : on l'estime ici (densité CPU, échantillonnée une fois).
    private const float CloudExtinction = 0.006f;   // par mètre à densité 1
    private const int VisibilitySamples = 32;

    /// <summary>Traceur réglé pour que la pointe du tronc <paramref name="tip"/> arrive en <paramref name="leaderTime"/> s, par bonds.</summary>
    private void FinishLeader(int tip, float leaderTime)
    {
        leaderTime = Mathf.Max(leaderTime, 0.05f);
        Finish(Mathf.Max(_distance[tip], 1f) / leaderTime, leaderTime);
        _material.SetShaderParameter("step_time", StepTime);
        _material.SetShaderParameter("leader_width", LeaderWidth);
        _material.SetShaderParameter("branch_flash", BranchFlash);
    }

    /// <summary>
    /// Construit le maillage. <paramref name="leaderEnd"/> &gt; 0 fixe l'arc en retour à cet instant : les filets plus
    /// longs que le tronc sont tronqués dans le temps (ils apparaissent tous pendant le traceur).
    /// </summary>
    private void Finish(float leaderSpeed, float leaderEnd = -1f)
    {
        CloudField field = CloudField.Instance;
        Vector3? cam = GetViewport()?.GetCamera3D()?.GlobalPosition;
        float maxDistance = 0f;
        var visibility = new float[_points.Count];
        float[] tip = TipFactors();
        for (int i = 0; i < _points.Count; i++)
        {
            maxDistance = Mathf.Max(maxDistance, _distance[i]);
            visibility[i] = cam.HasValue && field != null ? Visibility(cam.Value, _points[i], field) : 1f;
        }
        _leaderEnd = leaderEnd > 0f ? leaderEnd : Mathf.Max(maxDistance / leaderSpeed, 0.05f);

        int n = _segments.Count * 4;
        var verts = new Vector3[n];
        var custom0 = new float[n * 4];
        var custom1 = new float[n * 4];
        var indices = new int[_segments.Count * 6];
        for (int s = 0; s < _segments.Count; s++)
        {
            var (a, b) = _segments[s];
            for (int k = 0; k < 4; k++)
            {
                bool isStart = k < 2;
                int self = isStart ? a : b, other = isStart ? b : a;
                float side = (k % 2 == 0 ? -1f : 1f) * (isStart ? 1f : 2f);
                int v = s * 4 + k;
                verts[v] = _points[self];
                Vector3 o = _points[other];
                custom0[v * 4] = o.X;
                custom0[v * 4 + 1] = o.Y;
                custom0[v * 4 + 2] = o.Z;
                custom0[v * 4 + 3] = side;
                custom1[v * 4] = Mathf.Min(_distance[self] / leaderSpeed, _leaderEnd * 0.98f);
                custom1[v * 4 + 1] = visibility[self];
                // Largeur et affinage des pointes, empaquetés : centièmes d'affinage × 10 + largeur (< 10).
                custom1[v * 4 + 2] = Mathf.Round(tip[self] * 100f) * 10f + Mathf.Min(_width[self], 9.99f);
                custom1[v * 4 + 3] = _branch[self] ? -1f - _follow[self] : _follow[self];
            }
            int i0 = s * 4;
            indices[s * 6] = i0;
            indices[s * 6 + 1] = i0 + 1;
            indices[s * 6 + 2] = i0 + 2;
            indices[s * 6 + 3] = i0 + 1;
            indices[s * 6 + 4] = i0 + 3;
            indices[s * 6 + 5] = i0 + 2;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Custom0] = custom0;
        arrays[(int)Mesh.ArrayType.Custom1] = custom1;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var flags = (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                                     | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift));
        var mesh = new ArrayMesh();
        if (n > 0)
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
        Mesh = mesh;

        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _material.SetShaderParameter("leader_end", _leaderEnd);
        if (_look.HasValue)
            ApplyLook();
        MaterialOverride = _material;
        CastShadow = ShadowCastingSetting.Off;
        ExtraCullMargin = 2000f;   // les rubans sont élargis dans le shader
        GlobalTransform = Transform3D.Identity;
    }

    /// <summary>
    /// Affinage 0..1 de chaque point : 1 partout, descendant à 0 sur les <see cref="TipTaperPoints"/> derniers points de
    /// chaque pointe libre (point sans suite), sauf la pointe qui touche (<see cref="_anchoredTip"/>).
    /// </summary>
    private float[] TipFactors()
    {
        var factor = new float[_points.Count];
        var parent = new int[_points.Count];
        var hasChild = new bool[_points.Count];
        System.Array.Fill(factor, 1f);
        System.Array.Fill(parent, -1);
        foreach (var (a, b) in _segments)
        {
            parent[b] = a;
            hasChild[a] = true;
        }
        for (int i = 0; i < _points.Count; i++)
        {
            if (hasChild[i] || i == _anchoredTip || parent[i] < 0)
                continue;
            int p = i;
            for (int k = 0; k <= TipTaperPoints && p >= 0; k++, p = parent[p])
                factor[p] = Mathf.Min(factor[p], (float)k / TipTaperPoints);
        }
        return factor;
    }

    /// <summary>
    /// Catmull-Rom uniforme : insère <paramref name="perSegment"/> - 1 points entre chaque paire de points, en passant
    /// par tous les points d'origine (premier et dernier compris). Les voisins manquants aux extrémités sont clampés.
    /// </summary>
    private static List<Vector3> Smooth(List<Vector3> pts, int perSegment)
    {
        if (pts.Count < 2 || perSegment < 2)
            return new List<Vector3>(pts);
        var result = new List<Vector3>((pts.Count - 1) * perSegment + 1);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
            result.Add(p1);
            for (int k = 1; k < perSegment; k++)
            {
                float t = (float)k / perSegment;
                float t2 = t * t, t3 = t2 * t;
                result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        result.Add(pts[^1]);
        return result;
    }

    /// <summary>Transmittance entre la caméra et le point, à travers les nuages.</summary>
    private static float Visibility(Vector3 cam, Vector3 p, CloudField field)
    {
        Vector3 d = p - cam;
        float step = d.Length() / VisibilitySamples;
        float depth = 0f;
        for (int i = 0; i < VisibilitySamples; i++)
            depth += field.GetDensity(cam + d * ((i + 0.5f) / VisibilitySamples)) * step;
        return Mathf.Exp(-depth * CloudExtinction);
    }

    /// <summary>Découpe le segment en 2^levels morceaux, chaque milieu décalé perpendiculairement.</summary>
    private static List<Vector3> Subdivide(Vector3 from, Vector3 to, int levels, RandomNumberGenerator rng)
    {
        var points = new List<Vector3> { from, to };
        for (int level = 0; level < levels; level++)
        {
            var next = new List<Vector3>(points.Count * 2);
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                float length = a.DistanceTo(b);
                next.Add(a);
                next.Add((a + b) * 0.5f + Perpendicular(b - a, rng) * (rng.RandfRange(-1f, 1f) * Roughness * length));
            }
            next.Add(points[^1]);
            points = next;
        }
        return points;
    }

    /// <summary>Direction voisine : lacet jusqu'à ±yaw degrés, tangage limité à ±pitch (le canal reste quasi horizontal).</summary>
    private static Vector3 Wiggle(Vector3 dir, RandomNumberGenerator rng, float yaw, float pitch)
    {
        Vector3 d = dir.Rotated(Vector3.Up, Mathf.DegToRad(rng.RandfRange(-yaw, yaw)));
        Vector3 h = new Vector3(d.X, 0f, d.Z);
        h = h.LengthSquared() > 1e-6f ? h.Normalized() : Vector3.Right;
        float p = Mathf.DegToRad(rng.RandfRange(-pitch, pitch));
        return (h * Mathf.Cos(p) + Vector3.Up * Mathf.Sin(p)).Normalized();
    }

    private static Vector3 Perpendicular(Vector3 dir, RandomNumberGenerator rng)
    {
        Vector3 d = dir.Normalized();
        Vector3 r = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)).Normalized();
        Vector3 p = r - d * r.Dot(d);
        return p.LengthSquared() > 1e-6f ? p.Normalized() : Vector3.Right;
    }
}
