using Bomba.Core;
using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>
/// Surface aérodynamique (coque, aile, gouvernail). Sa normale est son axe +Y local,
/// son axe avant est -Z local. Portance proportionnelle au carré de la vitesse avant,
/// soufflée par l'hélice : elle s'effondre moteurs coupés.
/// </summary>
[GlobalClass, Tool]
public partial class AeroSurface : VehicleModule
{
    /// <summary>Portance par (m/s)² (N).</summary>
    [Export] public float LiftCoefficient { get; set; } = 0.15f;

    /// <summary>Portance maximale (N).</summary>
    [Export] public float MaxLift { get; set; } = 600f;

    /// <summary>Part de la portance conservée moteurs coupés.</summary>
    [Export] public float UnpoweredLiftFactor { get; set; } = 0.15f;

    /// <summary>Traînée de plaque par (m/s)² (N), surface vue de face.</summary>
    [Export] public float DragCoefficient { get; set; } = 0.02f;

    /// <summary>0 = surface portante pure ; &gt; 0 = gouvernail (N par (m/s)²).</summary>
    [Export] public float ControlStrength { get; set; } = 0f;

    /// <summary>Vitesse avant minimale (m/s) pour que le gouvernail agisse.</summary>
    [Export] public float MinControlSpeed { get; set; } = 3f;

    public override void _Ready()
    {
        base._Ready();
        if (Channel == InputChannel.Throttle)
            Channel = InputChannel.Always;
    }

    protected override void Simulate(float delta, float command)
    {
        Vector3 air = GetPointVelocity() - Wind.GetWindAt(GlobalPosition);
        Vector3 normal = GlobalBasis.Y.Normalized();
        Vector3 fwd = -GlobalBasis.Z.Normalized();

        float s = Mathf.Max(0f, air.Dot(fwd));

        float blow = Vehicle != null
            ? Mathf.Lerp(UnpoweredLiftFactor, 1f, Vehicle.PowerRatio)
            : UnpoweredLiftFactor;
        Vector3 lift = normal * Mathf.Min(LiftCoefficient * s * s, MaxLift) * blow;

        Vector3 drag = Vector3.Zero;
        float speed = air.Length();
        if (speed > 0.01f)
        {
            float exposure = 0.1f + 0.9f * Mathf.Abs(normal.Dot(air / speed));
            drag = -air * speed * DragCoefficient * exposure;
        }

        Vector3 control = Vector3.Zero;
        if (ControlStrength > 0f && Vehicle != null && s > MinControlSpeed)
        {
            Vector3 torqueAxis = Vehicle.GetDesiredTorqueAxis();
            Vector3 lever = GlobalPosition - Vehicle.GlobalCenterOfMass;
            Vector3 wanted = torqueAxis.Cross(lever);
            if (wanted.LengthSquared() > 1e-6f)
            {
                control = normal * normal.Dot(wanted.Normalized())
                    * Mathf.Min(torqueAxis.Length(), 1f)
                    * ControlStrength * s * s * Vehicle.Authority;
            }
        }

        ApplyForceAtSelf(lift + drag + control);
    }
}
