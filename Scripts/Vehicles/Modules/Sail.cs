using Bomba.Core;
using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>
/// Voile orientable autour de son mât (axe Y local). Au repos, sa normale est +X local.
/// Elle est poussée par le vent apparent (vent global moins vitesse du point de montage).
/// </summary>
[GlobalClass, Tool]
public partial class Sail : VehicleModule
{
    /// <summary>Coefficient de poussée par (m/s)² (N).</summary>
    [Export] public float SailCoefficient { get; set; } = 0.6f;

    /// <summary>Poussée maximale (N).</summary>
    [Export] public float MaxForce { get; set; } = 500f;

    /// <summary>Débattement maximal de la voile, de part et d'autre du repos (degrés).</summary>
    [Export] public float MaxAngleDegrees { get; set; } = 70f;

    /// <summary>Vitesse de rotation de la voile (degrés/s).</summary>
    [Export] public float TurnSpeedDegrees { get; set; } = 90f;

    /// <summary>Largeur (X) et hauteur (Y) de la toile (m).</summary>
    [Export] public Vector2 Size { get; set; } = new Vector2(4f, 5f);

    /// <summary>Crée un mât et une toile simples comme enfants.</summary>
    [Export] public bool CreateDefaultVisual { get; set; } = true;

    /// <summary>Angle courant de la voile (degrés), 0 = repos.</summary>
    public float AngleDegrees { get; private set; }

    private Basis _restBasis;

    public override void _Ready()
    {
        base._Ready();
        if (Channel == InputChannel.Throttle)
            Channel = InputChannel.Trim;

        _restBasis = Basis;

        if (CreateDefaultVisual)
            BuildVisual();
    }

    protected override void Simulate(float delta, float command)
    {
        float targetAngle = Mathf.Clamp(command, -1f, 1f) * MaxAngleDegrees;
        AngleDegrees = Mathf.MoveToward(AngleDegrees, targetAngle, TurnSpeedDegrees * delta);
        Basis = _restBasis * new Basis(Vector3.Up, Mathf.DegToRad(AngleDegrees));

        Vector3 w = Wind.GetWindAt(GlobalPosition) - GetPointVelocity();
        if (w.LengthSquared() < 0.01f)
            return;

        Vector3 n = GlobalBasis.X.Normalized();
        Vector3 wHat = w.Normalized();
        float magnitude = Mathf.Min(SailCoefficient * w.LengthSquared() * Mathf.Abs(n.Dot(wHat)), MaxForce);
        Vector3 force = n * (float)Mathf.Sign(n.Dot(wHat)) * magnitude;

        ApplyForceAtSelf(force);
    }

    private void BuildVisual()
    {
        Vector2 size = Size;

        var mastMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.15f) };
        Visual.AddChild(new MeshInstance3D
        {
            Name = "Mat",
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = size.Y + 1f },
            MaterialOverride = mastMaterial,
        });

        var canvasMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.8f, 0.65f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Visual.AddChild(new MeshInstance3D
        {
            Name = "Toile",
            Mesh = new BoxMesh { Size = new Vector3(0.05f, size.Y, size.X) },
            MaterialOverride = canvasMaterial,
            Position = new Vector3(0f, 0f, size.X / 2f),
        });
    }
}
