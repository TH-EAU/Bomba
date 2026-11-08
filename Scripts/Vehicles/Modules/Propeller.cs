using Bomba.Core;
using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>
/// Hélice ou rotor. Pousse selon son -Z local ; la poussée chute avec la vitesse axiale
/// (vitesse de l'air qui traverse l'hélice) jusqu'à 0 à <see cref="MaxEffectiveSpeed"/>.
/// </summary>
[GlobalClass, Tool]
public partial class Propeller : VehicleModule
{
    /// <summary>Poussée maximale à plein régime et à l'arrêt de l'air (N).</summary>
    [Export] public float MaxThrust { get; set; } = 300f;

    /// <summary>Vitesse axiale (m/s) à laquelle la poussée tombe à 0.</summary>
    [Export] public float MaxEffectiveSpeed { get; set; } = 45f;

    /// <summary>Temps (s) pour passer de 0 à 100 % de régime, et inversement.</summary>
    [Export] public float SpoolTime { get; set; } = 0.6f;

    /// <summary>Régime au repos (0..1), voir <see cref="ModuleMath.MapCommand"/>.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BaseLevel { get; set; } = 0f;

    /// <summary>Rayon des pales (m), utilisé pour le visuel.</summary>
    [Export] public float BladeRadius { get; set; } = 1.2f;

    /// <summary>Crée un rotor visuel simple au démarrage.</summary>
    [Export] public bool CreateDefaultVisual { get; set; } = true;

    /// <summary>Régime actuel (0..1).</summary>
    public float SpinLevel { get; private set; }

    /// <summary>Poussée maximale du module.</summary>
    public override float MaxPower => MaxThrust;

    /// <summary>Poussée actuelle du module.</summary>
    public override float CurrentPower => SpinLevel * MaxThrust;

    private Node3D _rotor;

    public override void _Ready()
    {
        base._Ready();

        if (CreateDefaultVisual)
            BuildVisual();
    }

    public override void _Process(double delta)
    {
        if (_rotor != null)
            _rotor.RotateZ(SpinLevel * 25f * (float)delta);
    }

    protected override void Simulate(float delta, float command)
    {
        float target = ModuleMath.MapCommand(command, BaseLevel);
        SpinLevel = Mathf.MoveToward(SpinLevel, target, delta / Mathf.Max(SpoolTime, 0.001f));

        Vector3 dir = -GlobalBasis.Z.Normalized();
        float axial = (GetPointVelocity() - Wind.GetWindAt(GlobalPosition)).Dot(dir);
        float efficiency = Mathf.Clamp(1f - axial / Mathf.Max(MaxEffectiveSpeed, 0.001f), 0f, 1f);
        float thrust = MaxThrust * SpinLevel * efficiency;

        if (thrust > 0f)
            ApplyForceAtSelf(dir * thrust);
    }

    private void BuildVisual()
    {
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.15f) };

        _rotor = new Node3D { Name = "Rotor" };
        Visual.AddChild(_rotor);

        _rotor.AddChild(new MeshInstance3D
        {
            Name = "BladeX",
            Mesh = new BoxMesh { Size = new Vector3(2f * BladeRadius, 0.25f, 0.05f) },
            MaterialOverride = wood,
        });

        _rotor.AddChild(new MeshInstance3D
        {
            Name = "BladeY",
            Mesh = new BoxMesh { Size = new Vector3(0.25f, 2f * BladeRadius, 0.05f) },
            MaterialOverride = wood,
        });

        _rotor.AddChild(new MeshInstance3D
        {
            Name = "Hub",
            Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f },
            MaterialOverride = wood,
        });
    }
}
