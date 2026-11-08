using System.Collections.Generic;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// Dessine, en debug, les vecteurs de force de chaque module, la vitesse et le centre de masse
/// du véhicule suivi. Le dessin utilise un <see cref="ImmediateMesh"/> en coordonnées globales.
/// </summary>
public partial class ForceGizmos : Node3D, IVehicleTracker
{
    /// <summary>Longueur (m) dessinée pour une force égale au poids du véhicule.</summary>
    [Export] public float MetersPerWeight { get; set; } = 4f;

    /// <summary>Véhicule dont on dessine les forces. Peut être changé à l'exécution, ou valoir null.</summary>
    public Vehicle Target { get; set; }

    private ImmediateMesh _mesh;
    private StandardMaterial3D _material;
    private readonly List<(Vector3 point, Color color)> _vertices = new();

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        GlobalTransform = Transform3D.Identity;

        _mesh = new ImmediateMesh();
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };

        var instance = new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(instance);
    }

    public override void _Process(double delta)
    {
        _mesh.ClearSurfaces();
        _vertices.Clear();

        if (!DebugSettings.ShowDebug || !IsInstanceValid(Target))
            return;

        Vehicle v = Target;
        float scale = MetersPerWeight / (v.Mass * 9.8f);

        foreach (VehicleModule m in v.Modules)
        {
            Vector3 origin = m.GlobalPosition;
            Color color = ColorFor(m.GetType().Name);
            AddLine(origin, origin + m.LastForce * scale, color);
            AddCross(origin, 0.3f, color);
        }

        AddLine(v.GlobalCenterOfMass, v.GlobalCenterOfMass + v.LinearVelocity * 0.2f, Colors.Blue);
        AddCross(v.GlobalCenterOfMass, 0.6f, Colors.Red);

        // Un ImmediateMesh vide plante : on ne démarre une surface que s'il y a des sommets.
        if (_vertices.Count == 0)
            return;

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
        foreach ((Vector3 point, Color color) in _vertices)
        {
            _mesh.SurfaceSetColor(color);
            _mesh.SurfaceAddVertex(point);
        }
        _mesh.SurfaceEnd();
    }

    private void AddLine(Vector3 from, Vector3 to, Color color)
    {
        _vertices.Add((from, color));
        _vertices.Add((to, color));
    }

    /// <summary>Petite croix à trois axes, de <paramref name="size"/> mètres de long chacune.</summary>
    private void AddCross(Vector3 center, float size, Color color)
    {
        float h = size * 0.5f;
        AddLine(center - Vector3.Right * h, center + Vector3.Right * h, color);
        AddLine(center - Vector3.Up * h, center + Vector3.Up * h, color);
        AddLine(center - Vector3.Back * h, center + Vector3.Back * h, color);
    }

    private static Color ColorFor(string moduleType) => moduleType switch
    {
        "Thruster" => new Color(1f, 0.5f, 0f),
        "Propeller" => Colors.Yellow,
        "Balloon" => Colors.Cyan,
        "Sail" => Colors.White,
        "AeroSurface" => Colors.Green,
        _ => Colors.Magenta,
    };
}
