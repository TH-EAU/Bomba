using Godot;

namespace Bomba.Core;

/// <summary>
/// Zone de rafale locale : ajoute son vent au vent global à l'intérieur de son rayon,
/// avec une décroissance linéaire vers le bord et une pulsation optionnelle.
/// </summary>
[GlobalClass, Tool]
public partial class WindZone : Node3D, IWindSource
{
    /// <summary>Vent au centre de la zone (m/s, global).</summary>
    [Export] public Vector3 WindVelocity { get; set; } = new(0f, 12f, 0f);

    /// <summary>Rayon d'effet (m).</summary>
    [Export] public float Radius { get; set; } = 40f;

    /// <summary>Période de la pulsation (s). 0 = vent constant.</summary>
    [Export] public float PulsePeriod { get; set; } = 4f;

    /// <summary>Affiche les particules et la sphère de la zone.</summary>
    [Export] public bool ShowVisual { get; set; } = true;

    /// <summary>Affiche la sphère translucide de la zone (debug).</summary>
    [Export] public bool ShowZoneSphere { get; set; }

    private float _time;
    private bool _registered;

    public override void _EnterTree() => TryRegister();

    public override void _Ready()
    {
        TryRegister();
        if (ShowVisual)
            BuildVisual();
    }

    public override void _ExitTree()
    {
        if (_registered && Wind.Instance != null)
            Wind.Instance.Unregister(this);
        _registered = false;
    }

    public override void _PhysicsProcess(double delta) => _time += (float)delta;

    /// <summary>Contribution de cette zone au vent (m/s) au point donné.</summary>
    public Vector3 GetWindContribution(Vector3 globalPosition)
    {
        float d = globalPosition.DistanceTo(GlobalPosition);
        if (d >= Radius)
            return Vector3.Zero;

        float falloff = 1f - d / Radius;
        float pulse = PulsePeriod > 0f
            ? 0.6f + 0.4f * Mathf.Sin(_time * Mathf.Tau / PulsePeriod)
            : 1f;
        return WindVelocity * falloff * pulse;
    }

    private void TryRegister()
    {
        if (_registered || Wind.Instance == null || Engine.IsEditorHint())
            return;
        Wind.Instance.Register(this);
        _registered = true;
    }

    private void BuildVisual()
    {
        float radius = Radius;
        Vector3 windVelocity = WindVelocity;
        Vector3 direction = windVelocity.LengthSquared() > 1e-6f ? windVelocity.Normalized() : Vector3.Up;

        var streakMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.35f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        AddChild(new CpuParticles3D
        {
            Name = "Rafales",
            Amount = 60,
            Lifetime = 2.0,
            LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius * 0.8f,
            Direction = direction,
            InitialVelocityMin = windVelocity.Length(),
            InitialVelocityMax = windVelocity.Length(),
            Gravity = Vector3.Zero,
            // Mesh allongé sur Y (et non 1.5 sur Z comme prévu) : AlignY oriente Y selon la vitesse.
            ParticleFlagAlignY = true,
            Mesh = new BoxMesh { Size = new Vector3(0.05f, 1.5f, 0.05f) },
            MaterialOverride = streakMaterial,
        });

        // Sphère translucide de la zone : utile au debug seulement (transparente, elle se dessinerait
        // par-dessus les nuages, rendus avant les objets transparents).
        if (!ShowZoneSphere)
            return;

        var zoneMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 0.06f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D
        {
            Name = "Zone",
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
            MaterialOverride = zoneMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }
}
