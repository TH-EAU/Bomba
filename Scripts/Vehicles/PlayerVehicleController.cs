using Bomba.Core;
using Bomba.UI;
using Godot;

namespace Bomba.Vehicles;

/// <summary>
/// Lit les entrées du joueur et les écrit dans <see cref="Vehicle.CurrentInput"/>.
/// S'exécute avant la physique des véhicules (priorité physique négative).
/// Avec le pilotage souris, un réticule à l'écran agit comme un gouvernail (lacet et tangage).
/// </summary>
public partial class PlayerVehicleController : Node, IVehicleTracker
{
    /// <summary>Véhicule piloté. Peut être changé à l'exécution.</summary>
    [Export] public Vehicle Target { get; set; }

    /// <summary>Inverse le tangage (style aviation : pousser = piquer).</summary>
    [Export] public bool InvertPitch { get; set; }

    [ExportGroup("Souris")]
    /// <summary>Pilotage à la souris : le réticule remplace le lacet et le tangage du clavier.</summary>
    [Export] public bool MouseSteering { get; set; } = true;
    /// <summary>Course du réticule depuis le centre de l'écran (px).</summary>
    [Export] public float MouseRadius { get; set; } = 220f;
    /// <summary>Multiplicateur du déplacement souris.</summary>
    [Export] public float MouseSensitivity { get; set; } = 1f;
    /// <summary>Zone morte autour du centre, en fraction du rayon.</summary>
    [Export] public float MouseDeadzone { get; set; } = 0.06f;
    /// <summary>Vitesse de retour au centre (1/s). 0 : le réticule reste où on le laisse.</summary>
    [Export] public float MouseRecenterSpeed { get; set; } = 0f;

    private Vector2 _mouseOffset;

    /// <summary>
    /// Position du réticule dans le disque unité (x : droite, y : bas), zone morte appliquée.
    /// La zone morte est retirée de façon continue : la sortie va de 0 à 1 sans saut.
    /// </summary>
    public Vector2 MouseSteer
    {
        get
        {
            if (MouseRadius <= 0f)
                return Vector2.Zero;
            Vector2 v = _mouseOffset / MouseRadius;
            float len = v.Length();
            float dz = Mathf.Clamp(MouseDeadzone, 0f, 0.99f);
            if (len <= 0f || len < dz)
                return Vector2.Zero;
            return v * ((len - dz) / (1f - dz) / len);
        }
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = -100;
        AddToGroup(IVehicleTracker.GroupName);

        if (MouseSteering)
            Input.MouseMode = Input.MouseModeEnum.Captured;

        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        layer.AddChild(new SteeringReticle { Controller = this });
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed(GameActions.ReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            return;
        }

        if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Left && Input.MouseMode != Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Captured;
            else if (button.ButtonIndex == MouseButton.Middle)
                _mouseOffset = Vector2.Zero;
            return;
        }

        if (@event is InputEventMouseMotion motion
            && MouseSteering
            && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _mouseOffset += motion.Relative * MouseSensitivity;
            _mouseOffset = _mouseOffset.LimitLength(MouseRadius);
        }
    }

    public override void _Process(double delta)
    {
        if (MouseRecenterSpeed > 0f)
            _mouseOffset *= Mathf.Exp(-MouseRecenterSpeed * (float)delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Target == null || !IsInstanceValid(Target))
            return;

        float pitch = Input.GetAxis(GameActions.PitchDown, GameActions.PitchUp);
        float yaw = Input.GetAxis(GameActions.YawLeft, GameActions.YawRight);
        if (InvertPitch)
            pitch = -pitch;

        if (MouseSteering)
        {
            Vector2 steer = MouseSteer;
            // L'Y de l'écran croît vers le bas : réticule vers le haut = nez vers le haut.
            float mousePitch = -steer.Y;
            if (InvertPitch)
                mousePitch = -mousePitch;
            yaw = Mathf.Clamp(yaw + steer.X, -1f, 1f);
            pitch = Mathf.Clamp(pitch + mousePitch, -1f, 1f);
        }

        Target.CurrentInput = new VehicleInput
        {
            Throttle = Input.GetActionStrength(GameActions.Throttle),
            Brake = Input.GetActionStrength(GameActions.Brake),
            Pitch = pitch,
            Yaw = yaw,
            Roll = Input.GetAxis(GameActions.RollLeft, GameActions.RollRight),
            Vertical = Input.GetAxis(GameActions.VerticalDown, GameActions.VerticalUp),
            Trim = Input.GetAxis(GameActions.TrimLeft, GameActions.TrimRight),
            Nitro = Input.IsActionPressed(GameActions.Nitro),
        };
    }
}
