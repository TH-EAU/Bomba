using System.Collections.Generic;
using Bomba.Cameras;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Effets des nuages sur le véhicule suivi : traînée et turbulences dans le nuage, éclairs dans un
/// cœur d'orage (flash, secousse, impulsion, moteurs coupés un instant). Les densités viennent de
/// <see cref="CloudField"/> ; ce nœud ne fait que des forces sur <see cref="Target"/>.
/// </summary>
public partial class CloudEffects : Node, IVehicleTracker
{
    [ExportGroup("Perturbation")]
    /// <summary>Traînée (1/s) à densité 1.</summary>
    [Export] public float CloudDrag { get; set; } = 0.6f;
    /// <summary>Couple de turbulence maximal (rad/s²) à densité 1.</summary>
    [Export] public float TurbulenceTorque { get; set; } = 2.5f;
    /// <summary>Accélération de turbulence maximale (m/s²) à densité 1.</summary>
    [Export] public float TurbulenceForce { get; set; } = 5f;
    /// <summary>Fréquence (Hz) des turbulences.</summary>
    [Export] public float TurbulenceFrequency { get; set; } = 1.3f;

    [ExportGroup("Orage")]
    /// <summary>Intensité d'orage au-delà de laquelle les éclairs peuvent frapper le joueur.</summary>
    [Export] public float StormThreshold { get; set; } = 0.25f;
    /// <summary>Éclairs par seconde sur le joueur à orage 1.</summary>
    [Export] public float StrikeRate { get; set; } = 0.35f;
    /// <summary>Vitesse (m/s) de l'impulsion d'un éclair.</summary>
    [Export] public float StrikeImpulse { get; set; } = 9f;
    /// <summary>Vitesse angulaire (rad/s) donnée par un éclair.</summary>
    [Export] public float StrikeSpin { get; set; } = 2.5f;
    /// <summary>Durée (s) de la coupure des moteurs.</summary>
    [Export] public float StallDuration { get; set; } = 0.8f;
    /// <summary>Éclairs décoratifs par seconde autour du joueur.</summary>
    [Export] public float AmbientStrikeRate { get; set; } = 0.25f;
    /// <summary>Rayon (m) des éclairs décoratifs autour du joueur.</summary>
    [Export] public float AmbientStrikeRadius { get; set; } = 25000f;
    /// <summary>Probabilité qu'un éclair d'ambiance puisse aussi partir d'un congestus local (éclair de chaleur, sans danger).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CongestusStrikeShare { get; set; } = 0f;

    [ExportGroup("Détection")]
    /// <summary>Densité au-delà de laquelle le joueur est considéré caché.</summary>
    [Export] public float HiddenDensity { get; set; } = 0.45f;

    private const float InCloudDensity = 0.02f;
    private const float StrikeMinDistance = 40f;
    private const float StrikeMaxDistance = 120f;
    private const float StrikeShakeAmplitude = 0.8f;
    private const float StrikeShakeDuration = 0.4f;
    private const float AmbientMinAltitude = 600f;
    private const float AmbientMaxAltitude = 9000f;
    private const int AmbientCandidates = 8;
    private const float AmbientStormLimit = 0.5f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    /// <summary>Densité 0..1 sous le véhicule.</summary>
    public float Density { get; private set; }
    /// <summary>Intensité d'orage 0..1 sous le véhicule.</summary>
    public float Storm { get; private set; }
    /// <summary>Ascendance (m/s) sous le véhicule.</summary>
    public float Updraft { get; private set; }
    public bool InCloud { get; private set; }
    public bool Hidden { get; private set; }
    /// <summary>Vrai pendant une coupure de moteurs par la foudre.</summary>
    public bool Stalled => _stallVehicle != null;

    private readonly List<VehicleModule> _stalledModules = new();
    private Vehicle _stallVehicle;
    private float _stallTimeLeft;
    private float _noiseTime;
    private readonly FastNoiseLite _noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);
        _rng.Randomize();
    }

    public override void _ExitTree() => EndStall();

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _noiseTime += dt;
        _noise.Frequency = TurbulenceFrequency;

        // Coupure : elle prend fin si le véhicule change, disparaît, ou au bout du délai.
        if (_stallVehicle != null && (_stallVehicle != Target || !IsInstanceValid(_stallVehicle)))
            EndStall();
        if (_stallVehicle != null)
        {
            _stallTimeLeft -= dt;
            if (_stallTimeLeft <= 0f)
                EndStall();
        }

        CloudField field = CloudField.Instance;
        if (!IsInstanceValid(Target) || field?.IsBaked != true)
        {
            Density = Storm = Updraft = 0f;
            InCloud = Hidden = false;
            return;
        }

        Vehicle v = Target;
        Vector3 pos = v.GlobalPosition;
        CloudSample s = field.Sample(pos);
        Density = s.Density;
        Storm = s.Storm;
        Updraft = s.Updraft * field.UpdraftSpeed;
        InCloud = Density > InCloudDensity;
        Hidden = Density >= HiddenDensity;

        // Traînée proportionnelle à la vitesse.
        v.ApplyCentralForce(-v.LinearVelocity * Density * CloudDrag * v.Mass);

        if (InCloud)
            ApplyTurbulence(v);

        if (Storm > StormThreshold && _rng.Randf() < StrikeRate * Storm * dt)
            Strike(v, pos, dt);

        if (_rng.Randf() < AmbientStrikeRate * dt)
            AmbientStrikes(field, pos);
    }

    private void ApplyTurbulence(Vehicle v)
    {
        Vector3 rotational = new(Channel(0), Channel(1), Channel(2));
        Vector3 linear = new(Channel(3), Channel(4), Channel(5));
        v.ApplyAngularAcceleration(rotational * TurbulenceTorque * Density);
        v.ApplyCentralForce(linear * TurbulenceForce * Density * v.Mass);
    }

    /// <summary>Bruit de Perlin au temps courant, canal i (6 canaux décalés, dans [-1, 1]).</summary>
    private float Channel(int i) =>
        Mathf.Clamp(_noise.GetNoise2D(_noiseTime, i * 41.7f), -1f, 1f);

    private void Strike(Vehicle v, Vector3 pos, float dt)
    {
        Vector3 flash = pos + RandomUnit() * _rng.RandfRange(StrikeMinDistance, StrikeMaxDistance);
        CloudField.Instance.TriggerLightning(flash, v.GlobalPosition, true);

        // Impulsion biaisée vers le haut.
        Vector3 push = new(_rng.RandfRange(-1f, 1f), _rng.RandfRange(0.3f, 1f), _rng.RandfRange(-1f, 1f));
        v.ApplyCentralImpulse(push.Normalized() * StrikeImpulse * v.Mass);

        // Une seule frame : Δω = StrikeSpin après le pas physique.
        if (dt > 0f)
            v.ApplyAngularAcceleration(RandomUnit() * StrikeSpin / dt);

        StallEngines(v);
        ShakeCamera();
    }

    private void StallEngines(Vehicle v)
    {
        if (_stallVehicle == null)
        {
            _stallVehicle = v;
            foreach (VehicleModule m in v.Modules)
            {
                if (m.MaxPower > 0f && m.Enabled)
                {
                    m.Enabled = false;
                    _stalledModules.Add(m);
                }
            }
        }
        // Un éclair pendant la coupure prolonge la coupure.
        _stallTimeLeft = StallDuration;
    }

    private void EndStall()
    {
        foreach (VehicleModule m in _stalledModules)
            if (IsInstanceValid(m))
                m.Enabled = true;
        _stalledModules.Clear();
        _stallVehicle = null;
        _stallTimeLeft = 0f;
    }

    private void ShakeCamera()
    {
        foreach (Node n in GetTree().GetNodesInGroup(IVehicleTracker.GroupName))
        {
            if (n is ChaseCamera cam)
            {
                cam.Shake(StrikeShakeAmplitude, StrikeShakeDuration);
                break;
            }
        }
    }

    private void AmbientStrikes(CloudField field, Vector3 pos)
    {
        // Viser un cœur orageux proche (cumulonimbus, et congestus selon CongestusStrikeShare).
        if (field.TryRandomLightningPoint(pos, AmbientStrikeRadius, _rng, _rng.Randf() < CongestusStrikeShare, out Vector3 heat))
        {
            field.TriggerLightning(heat);
            return;
        }
        for (int i = 0; i < AmbientCandidates; i++)
        {
            Vector2 horizontal = Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau)
                                 * Mathf.Sqrt(_rng.Randf()) * AmbientStrikeRadius;
            var p = new Vector3(pos.X + horizontal.X,
                                _rng.RandfRange(AmbientMinAltitude, AmbientMaxAltitude),
                                pos.Z + horizontal.Y);
            if (field.Sample(p).Storm > AmbientStormLimit)
            {
                field.TriggerLightning(p);
                break;
            }
        }
    }

    private Vector3 RandomUnit() =>
        new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)).Normalized();
}
