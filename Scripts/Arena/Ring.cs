using Bomba.Vehicles;
using Godot;

namespace Bomba.Arena;

/// <summary>
/// Anneau de parcours dont l'axe de passage est le Z local. Un véhicule qui le traverse
/// le fait passer au vert et émet <see cref="Passed"/>.
/// </summary>
[Tool]
public partial class Ring : Area3D
{
    /// <summary>Émis quand un véhicule traverse l'anneau.</summary>
    [Signal] public delegate void PassedEventHandler(Ring ring);

    /// <summary>Rayon de l'anneau (m), du centre au milieu du tube.</summary>
    [Export] public float RingRadius { get; set; } = 9f;

    /// <summary>Rayon du tube (m).</summary>
    [Export] public float TubeRadius { get; set; } = 0.6f;

    /// <summary>Délai (s) avant que l'anneau revienne au jaune après un passage.</summary>
    [Export] public float ResetDelay { get; set; } = 4f;

    /// <summary>Nombre de sphères de collision formant le cadre.</summary>
    private const int FrameSegments = 12;

    private static readonly Color BrassColor = new(0.8f, 0.65f, 0.25f);
    private static readonly Color PassedColor = new(0.3f, 1f, 0.4f);

    private StandardMaterial3D _material;

    public override void _Ready()
    {
        _material = new StandardMaterial3D
        {
            AlbedoColor = BrassColor,
            EmissionEnabled = true,
            Emission = BrassColor,
            EmissionEnergyMultiplier = 0.4f,
        };

        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = RingRadius - TubeRadius,
                OuterRadius = RingRadius + TubeRadius,
            },
            MaterialOverride = _material,
            Rotation = new Vector3(Mathf.DegToRad(90f), 0f, 0f),
        });

        AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = RingRadius - TubeRadius, Height = 1.5f },
            Rotation = new Vector3(Mathf.DegToRad(90f), 0f, 0f),
        });

        var frame = new StaticBody3D();
        for (int i = 0; i < FrameSegments; i++)
        {
            float angle = i / (float)FrameSegments * Mathf.Tau;
            frame.AddChild(new CollisionShape3D
            {
                Shape = new SphereShape3D { Radius = TubeRadius },
                Position = new Vector3(RingRadius * Mathf.Cos(angle), RingRadius * Mathf.Sin(angle), 0f),
            });
        }
        AddChild(frame);

        if (!Engine.IsEditorHint())
            BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is not Vehicle)
            return;

        _material.AlbedoColor = PassedColor;
        _material.Emission = PassedColor;
        EmitSignal(SignalName.Passed, this);
        GetTree().CreateTimer(ResetDelay).Timeout += ResetColor;
    }

    private void ResetColor()
    {
        _material.AlbedoColor = BrassColor;
        _material.Emission = BrassColor;
    }
}
