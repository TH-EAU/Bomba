using Bomba.Vehicles;
using Godot;

namespace Bomba.Core;

/// <summary>
/// Soleil rasant du crépuscule. N'éclaire que les véhicules (couche de rendu 2, voir Vehicle) :
/// la mer et les îlots sont dans l'ombre de la Terre et ne reçoivent que la lumière du ciel.
/// Son énergie suit l'ensoleillement du véhicule suivi (<see cref="Twilight.SunLit"/>).
/// </summary>
[Tool]
public partial class TwilightSun : DirectionalLight3D, IVehicleTracker
{
    public const uint VehicleLayer = 1u << 1;

    [Export] public float Energy { get; set; } = 1.4f;

    public Vehicle Target { get; set; }

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);
        LightCullMask = VehicleLayer;
        LightColor = Twilight.SunColor;
        ShadowEnabled = true;
        DirectionalShadowMaxDistance = 300f;
        // La lumière part de -Z local : on regarde à l'opposé du soleil.
        Vector3 toSun = Twilight.SunDirection;
        Basis = Basis.LookingAt(-toSun, Vector3.Up);
        LightEnergy = 0f;
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            return;
        float lit = IsInstanceValid(Target) ? Twilight.SunLit(Target.GlobalPosition) : 0f;
        LightEnergy = Energy * lit;
        Visible = lit > 0.001f;
    }
}
