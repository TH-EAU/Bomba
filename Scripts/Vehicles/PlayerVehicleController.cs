using Bomba.Core;
using Godot;

namespace Bomba.Vehicles;

/// <summary>
/// Lit les entrées du joueur et les écrit dans <see cref="Vehicle.CurrentInput"/>.
/// S'exécute avant la physique des véhicules (priorité physique négative).
/// </summary>
public partial class PlayerVehicleController : Node, IVehicleTracker
{
    /// <summary>Véhicule piloté. Peut être changé à l'exécution.</summary>
    [Export] public Vehicle Target { get; set; }

    /// <summary>Inverse le tangage (style aviation : pousser = piquer).</summary>
    [Export] public bool InvertPitch { get; set; }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        AddToGroup(IVehicleTracker.GroupName);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Target == null || !IsInstanceValid(Target))
            return;

        float pitch = Input.GetAxis(GameActions.PitchDown, GameActions.PitchUp);
        if (InvertPitch)
            pitch = -pitch;

        Target.CurrentInput = new VehicleInput
        {
            Throttle = Input.GetActionStrength(GameActions.Throttle),
            Brake = Input.GetActionStrength(GameActions.Brake),
            Pitch = pitch,
            Yaw = Input.GetAxis(GameActions.YawLeft, GameActions.YawRight),
            Roll = Input.GetAxis(GameActions.RollLeft, GameActions.RollRight),
            Vertical = Input.GetAxis(GameActions.VerticalDown, GameActions.VerticalUp),
            Trim = Input.GetAxis(GameActions.TrimLeft, GameActions.TrimRight),
            Nitro = Input.IsActionPressed(GameActions.Nitro),
        };
    }
}
