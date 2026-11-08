using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>
/// Propulseur à gimbal. Pousse vers -Z local (avant), avec un débattement de gimbal
/// piloté par le <see cref="Vehicle"/> porteur quand il existe.
/// Réglages dans <see cref="ThrusterProfile"/>.
/// </summary>
[GlobalClass, Tool]
public partial class Thruster : VehicleModule
{
    /// <summary>Réglages du moteur. Si absent, un profil par défaut est créé.</summary>
    [Export] public ThrusterProfile Profile { get; set; }

    /// <summary>Oriente la poussée selon les commandes du véhicule (si le corps est un Vehicle).</summary>
    [Export] public bool UseGimbal { get; set; } = true;

    /// <summary>Crée en code le corps, la flamme et la fumée.</summary>
    [Export] public bool CreateDefaultVisual { get; set; } = true;

    /// <summary>Niveau de régime, 0..1 (monte et descend selon SpoolUpTime / SpoolDownTime).</summary>
    public float SpoolLevel { get; private set; }

    /// <summary>Chaleur accumulée, 0..1.</summary>
    public float Heat { get; private set; }

    /// <summary>Vrai tant que le moteur est en surchauffe (hystérésis à 0,4).</summary>
    public bool Overheated { get; private set; }

    /// <summary>Carburant restant, 0..1.</summary>
    public float Fuel { get; private set; } = 1f;

    /// <summary>Poussée maximale fournie (N).</summary>
    public override float MaxPower => Profile?.MaxThrust ?? 0f;

    /// <summary>Poussée actuelle (N), sans le multiplicateur de nitro.</summary>
    public override float CurrentPower => SpoolLevel * MaxPower;

    /// <summary>Hauteur du cylindre de flamme avant mise à l'échelle par le régime.</summary>
    private const float FlameMeshHeight = 1f;

    /// <summary>Demi-longueur du corps du propulseur (corps de 1,2 m).</summary>
    private const float BodyHalfLength = 0.6f;

    /// <summary>Niveau de carburant à atteindre pour redémarrer après une panne sèche.</summary>
    private const float FuelRestartLevel = 0.3f;

    private bool _fuelLockout;
    private MeshInstance3D _flame;
    private CpuParticles3D _smoke;

    public override void _Ready()
    {
        base._Ready();

        if (Profile == null)
        {
            Profile = new ThrusterProfile();
            GD.PushWarning($"{Name}: aucun ThrusterProfile, profil par défaut utilisé.");
        }

        if (CreateDefaultVisual)
            CreateVisual();
    }

    public override void _Process(double delta)
    {
        if (_flame == null || _smoke == null)
            return;

        float nitroFactor = Vehicle?.NitroActive == true ? 1.8f : 1f;
        float length = SpoolLevel * nitroFactor;

        _flame.Visible = SpoolLevel >= 0.02f;
        _flame.Scale = new Vector3(1f, length, 1f);
        _flame.Position = new Vector3(0f, 0f, BodyHalfLength + length * FlameMeshHeight * 0.5f);

        _smoke.Emitting = SpoolLevel > 0.05f;
    }

    protected override void Simulate(float delta, float command)
    {
        ThrusterProfile p = Profile;

        float target = Mathf.Clamp(command, 0f, 1f);

        // Réservoir vide : moteur verrouillé jusqu'à une recharge suffisante (évite le bégaiement).
        if (p.FuelSeconds > 0f)
        {
            if (Fuel <= 0f)
                _fuelLockout = true;
            else if (_fuelLockout && Fuel >= FuelRestartLevel)
                _fuelLockout = false;
            if (_fuelLockout)
                target = 0f;
        }

        float duration = target > SpoolLevel ? p.SpoolUpTime : p.SpoolDownTime;
        SpoolLevel = duration > 0f
            ? Mathf.MoveToward(SpoolLevel, target, delta / duration)
            : target;

        Heat = Mathf.Clamp(Heat + SpoolLevel * p.HeatPerSecond * delta - p.CoolPerSecond * delta, 0f, 1f);
        if (Heat >= 1f)
            Overheated = true;
        if (Overheated && Heat <= 0.4f)
            Overheated = false;

        if (p.FuelSeconds > 0f)
        {
            Fuel -= SpoolLevel * delta / p.FuelSeconds;
            if (target <= 0f)
                Fuel += p.FuelRegenPerSecond * delta;
            Fuel = Mathf.Clamp(Fuel, 0f, 1f);
        }

        float thrust = SpoolLevel * p.MaxThrust
            * (Overheated ? p.OverheatThrustFactor : 1f)
            * (Vehicle?.ThrustMultiplier ?? 1f);

        Vector3 dir = -GlobalBasis.Z.Normalized();
        if (UseGimbal && Vehicle != null)
            dir = Vehicle.GetGimbalDirection(dir, GlobalPosition, Mathf.DegToRad(p.GimbalDegrees));

        if (thrust > 0f)
            ApplyForceAtSelf(dir * thrust);
    }

    private void CreateVisual()
    {
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.3f, 0.27f),
            Metallic = 0.6f,
        };
        Visual.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.35f, Height = 1.2f, Material = bodyMaterial },
            Rotation = new Vector3(Mathf.DegToRad(90f), 0f, 0f),
        });

        // Le cylindre a son axe en Y ; la rotation X de 90° le place sur Z, pointe vers l'arrière.
        var flameMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Profile.FlameColor,
            EmissionEnabled = true,
            Emission = Profile.FlameColor,
        };
        _flame = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.3f, Height = FlameMeshHeight, Material = flameMaterial },
            Rotation = new Vector3(Mathf.DegToRad(90f), 0f, 0f),
        };
        Visual.AddChild(_flame);

        var smokeMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
        };
        _smoke = new CpuParticles3D
        {
            LocalCoords = false,
            Position = new Vector3(0f, 0f, BodyHalfLength),
            Direction = new Vector3(0f, 0f, 1f),
            Spread = 12f,
            Amount = 24,
            Lifetime = 0.8f,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 2.5f,
            Color = Profile.ExhaustColor,
            Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f, Material = smokeMaterial },
            Emitting = false,
        };
        Visual.AddChild(_smoke);
    }
}
