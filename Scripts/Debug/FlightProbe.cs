using System.Collections.Generic;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// Sonde de vol pour le réglage : pilote chaque véhicule de GameRoot avec un scénario
/// scripté et imprime la télémétrie, puis quitte. Sans affichage :
/// <c>godot --headless --path . res://Scenes/Debug/FlightProbe.tscn</c>
/// S'exécute après le PlayerVehicleController et écrase donc ses commandes.
/// </summary>
public partial class FlightProbe : Node
{
    private record struct Phase(string Name, float Duration, VehicleInput Input);

    /// <summary>Intervalle d'impression de la télémétrie (s).</summary>
    [Export] public float LogInterval { get; set; } = 0.5f;

    private readonly List<Phase> _phases = new()
    {
        new("repos", 1.5f, new VehicleInput()),
        new("plein gaz", 3f, new VehicleInput { Throttle = 1f }),
        // Roulis = vitesse (joystick) : on roule, puis on lâche (l'inclinaison est tenue, retour à plat après 1,5 s).
        new("roulis D", 0.5f, new VehicleInput { Throttle = 1f, Roll = 1f }),
        new("lâché incliné", 3.5f, new VehicleInput { Throttle = 1f }),
        new("roulis D", 0.75f, new VehicleInput { Throttle = 1f, Roll = 1f }),
        new("incliné + tirer", 3f, new VehicleInput { Throttle = 1f, Pitch = 1f }),
        new("neutre", 2f, new VehicleInput { Throttle = 1f }),
        new("roulis D", 0.4f, new VehicleInput { Throttle = 1f, Roll = 1f }),
        new("incliné + piquer", 2f, new VehicleInput { Throttle = 1f, Pitch = -0.3f }),
        new("neutre", 1.5f, new VehicleInput { Throttle = 1f }),
        new("looping", 8f, new VehicleInput { Throttle = 1f, Pitch = 1f }),
        new("neutre", 2.5f, new VehicleInput { Throttle = 1f }),
        new("sur le dos", 1.6f, new VehicleInput { Throttle = 1f, Roll = 1f }),
        new("lâché", 4f, new VehicleInput { Throttle = 1f }),
        new("frein", 2f, new VehicleInput { Throttle = 1f, Brake = 1f }),
        new("nitro", 1.5f, new VehicleInput { Throttle = 1f, Nitro = true }),
        new("coupé", 3f, new VehicleInput()),
        new("vertical+", 3f, new VehicleInput { Vertical = 1f }),
    };

    private int _vehicleIndex = -1;
    private int _phaseIndex;
    private float _phaseTime;
    private float _logTime;
    private float _startDelay = 0.3f;
    private float _lastHeading;

    public override void _Ready() => ProcessPhysicsPriority = -50;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        GameRoot root = GameRoot.Instance;
        if (root == null)
            return;

        if (_startDelay > 0f)
        {
            _startDelay -= dt;
            return;
        }

        if (_vehicleIndex < 0 || _phaseIndex >= _phases.Count)
        {
            _vehicleIndex++;
            if (_vehicleIndex >= root.VehicleScenes.Count)
            {
                GD.Print("=== Sonde terminée ===");
                GetTree().Quit();
                return;
            }
            root.SpawnVehicle(_vehicleIndex);
            _phaseIndex = 0;
            _phaseTime = 0f;
            _logTime = 0f;
            GD.Print($"\n=== {root.CurrentVehicle?.DisplayName} ===");
            GD.Print("phase           t     alt    vit   avant  vz     état        pwr  aut  nitro tangage incl.  cap  cap/s");
            return;
        }

        Vehicle v = root.CurrentVehicle;
        if (v == null || !IsInstanceValid(v))
            return;

        Phase phase = _phases[_phaseIndex];
        v.CurrentInput = phase.Input;

        _phaseTime += dt;
        _logTime += dt;
        if (_logTime >= LogInterval)
        {
            _logTime = 0f;
            Log(phase.Name, v);
        }

        if (_phaseTime >= phase.Duration)
        {
            _phaseIndex++;
            _phaseTime = 0f;
        }
    }

    private void Log(string phase, Vehicle v)
    {
        Vector3 fwd = v.Forward;
        float pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f)));
        float roll = Mathf.RadToDeg(v.Bank);
        float heading = Mathf.RadToDeg(Mathf.Atan2(fwd.X, -fwd.Z));
        float turnRate = Mathf.Wrap(heading - _lastHeading, -180f, 180f) / LogInterval;
        _lastHeading = heading;
        GD.Print(string.Format(
            "{0,-14} {1,5:F1} {2,6:F1} {3,6:F1} {4,6:F1} {5,6:F1}  {6,-10} {7,4:F2} {8,4:F2} {9,4:F2} {10,6:F0} {11,6:F0} {12,5:F0} {13,5:F0}",
            phase, _phaseTime, v.GlobalPosition.Y, v.Speed, v.ForwardSpeed, v.LinearVelocity.Y,
            v.State, v.PowerRatio, v.Authority, v.NitroGauge, pitch, roll, heading, turnRate));
    }
}
