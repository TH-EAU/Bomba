using System.Collections.Generic;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Trait de foudre ramifié : un tracé principal et quelques branches, en géométrie opaque et émissive.
/// Le trait scintille puis s'éteint, et se libère seul. Les coordonnées passées à <see cref="Setup"/> sont globales.
/// </summary>
public partial class LightningBolt : Node3D
{
    private const float Life = 0.35f;
    private const float FadeStart = 0.2f;
    private const float EmissionEnergy = 8f;
    private const float MainRadius = 3f;
    private const float BranchRadius = 1.5f;
    private const float Roughness = 0.25f;
    private const int MainLevels = 5;
    private const int BranchLevels = 3;
    private static readonly Color BoltColor = new(0.85f, 0.9f, 1f);

    private StandardMaterial3D _material;
    private float _age;

    public override void _Ready() => TopLevel = true;

    public void Setup(Vector3 from, Vector3 to, RandomNumberGenerator rng)
    {
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = BoltColor,
            EmissionEnabled = true,
            Emission = BoltColor,
            EmissionEnergyMultiplier = EmissionEnergy,
        };
        var mainMesh = new CylinderMesh { TopRadius = MainRadius, BottomRadius = MainRadius, Height = 1f, RadialSegments = 4, Rings = 1 };
        var branchMesh = new CylinderMesh { TopRadius = BranchRadius, BottomRadius = BranchRadius, Height = 1f, RadialSegments = 4, Rings = 1 };

        List<Vector3> trunk = Subdivide(from, to, MainLevels, rng);
        for (int i = 0; i + 1 < trunk.Count; i++)
            AddSegment(trunk[i], trunk[i + 1], mainMesh);

        Vector3 mainDir = (to - from).Normalized();
        float totalLength = from.DistanceTo(to);
        int lastIndex = trunk.Count - 1;
        int branchCount = rng.RandiRange(2, 4);
        for (int b = 0; b < branchCount; b++)
        {
            // Ni dans le premier quart, ni dans le dernier quart du tracé principal.
            Vector3 start = trunk[rng.RandiRange(lastIndex / 4, lastIndex * 3 / 4)];
            Vector3 axis = Perpendicular(mainDir, rng);
            Vector3 dir = mainDir.Rotated(axis, Mathf.DegToRad(rng.RandfRange(25f, 50f)));
            float length = totalLength * rng.RandfRange(0.15f, 0.35f);

            List<Vector3> branch = Subdivide(start, start + dir * length, BranchLevels, rng);
            for (int i = 0; i + 1 < branch.Count; i++)
                AddSegment(branch[i], branch[i + 1], branchMesh);
        }
    }

    public override void _Process(double delta)
    {
        if (_material == null)
            return;

        _age += (float)delta;
        if (_age >= Life)
        {
            QueueFree();
            return;
        }

        if (_age >= FadeStart)
        {
            Visible = true;
            _material.EmissionEnergyMultiplier = EmissionEnergy * (1f - (_age - FadeStart) / (Life - FadeStart));
            return;
        }

        // Scintillement : visible 0,06 s, éteint 0,04 s, visible 0,1 s.
        Visible = _age < 0.06f || _age >= 0.1f;
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
                Vector3 mid = (a + b) * 0.5f + Perpendicular(b - a, rng) * (rng.RandfRange(-1f, 1f) * Roughness * length);
                next.Add(a);
                next.Add(mid);
            }
            next.Add(points[^1]);
            points = next;
        }
        return points;
    }

    /// <summary>Segment orienté de A vers B : mesh de hauteur 1 étiré sur la longueur de A à B.</summary>
    private void AddSegment(Vector3 a, Vector3 b, CylinderMesh mesh)
    {
        Vector3 delta = b - a;
        float length = delta.Length();
        if (length < 0.01f)
            return;

        Vector3 y = delta / length;
        Vector3 x = (Mathf.Abs(y.Y) > 0.99f ? Vector3.Right : Vector3.Up).Cross(y).Normalized();
        Vector3 z = x.Cross(y);
        AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(x, y * length, z), (a + b) * 0.5f),
        });
    }

    private static Vector3 Perpendicular(Vector3 dir, RandomNumberGenerator rng)
    {
        Vector3 d = dir.Normalized();
        Vector3 r = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)).Normalized();
        Vector3 p = r - d * r.Dot(d);
        return p.LengthSquared() > 1e-6f ? p.Normalized() : Vector3.Right;
    }
}
