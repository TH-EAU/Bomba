using Bomba.Core;
using Bomba.UI;
using Godot;

namespace Bomba.Vehicles;

/// <summary>
/// Lit les entrées du joueur et les écrit dans <see cref="Vehicle.CurrentInput"/>.
/// S'exécute avant la physique des véhicules (priorité physique négative).
/// Avec le pilotage souris, le réticule commande l'inclinaison (X), le tangage (Y, bas = piquer
/// et, tout en bas, aérofrein). Le clavier et la manette ajoutent roulis et tangage.
/// </summary>
public partial class PlayerVehicleController : Node, IVehicleTracker
{
    /// <summary>Véhicule piloté. Peut être changé à l'exécution.</summary>
    [Export] public Vehicle Target { get; set; }

    /// <summary>
    /// SENSIBILITÉ des commandes : LE réglage à toucher (0,1 = très doux, 1 = vif). Plus bas, il faut bouger la souris
    /// davantage, et les petits mouvements deviennent plus doux (courbe « expo ») ; les grands restent pleins
    /// (sur le dos, looping).
    /// </summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float Sensitivity { get; set; } = 0.5f;

    /// <summary>Inverse le tangage (style aviation : pousser = piquer).</summary>
    [Export] public bool InvertPitch { get; set; }

    [ExportGroup("Clavier")]
    /// <summary>Vitesse de roulis au clavier, en part de la vitesse maximale (même comportement que le réticule).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float KeyboardBank { get; set; } = 0.7f;

    [ExportGroup("Souris")]
    /// <summary>Pilotage à la souris : le réticule commande l'inclinaison et le tangage.</summary>
    [Export] public bool MouseSteering { get; set; } = true;
    /// <summary>Position verticale du réticule (0..1, vers le bas) à partir de laquelle l'aérofrein s'ajoute.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MouseBrakeStart { get; set; } = 1f;
    /// <summary>Course du réticule depuis le centre de l'écran (px).</summary>
    [Export] public float MouseRadius { get; set; } = 220f;
    /// <summary>Multiplicateur du déplacement souris.</summary>
    [Export] public float MouseSensitivity { get; set; } = 1f;
    /// <summary>Zone morte autour du centre, en fraction du rayon.</summary>
    [Export] public float MouseDeadzone { get; set; } = 0.06f;
    /// <summary>Zone neutre du tangage (part de la course verticale) : en ramenant le réticule à l'horizontale, on ne pique pas.</summary>
    [Export(PropertyHint.Range, "0,0.4,0.01")] public float MousePitchDeadband { get; set; } = 0.08f;
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

    /// <summary>Aérofrein demandé par le réticule (0..1), lu par le réticule à l'écran.</summary>
    public float MouseBrake { get; private set; }

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
            _mouseOffset += motion.Relative * MouseSensitivity * Sensitivity;
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
        MouseBrake = 0f;
        if (Target == null || !IsInstanceValid(Target))
            return;

        float pitch = Input.GetAxis(GameActions.PitchDown, GameActions.PitchUp);
        if (InvertPitch)
            pitch = -pitch;
        float roll = Input.GetAxis(GameActions.RollLeft, GameActions.RollRight) * KeyboardBank;
        float brake = Input.GetActionStrength(GameActions.Brake);

        if (MouseSteering)
        {
            Vector2 steer = MouseSteer;
            // L'Y de l'écran croît vers le bas : réticule vers le haut = nez vers le haut.
            float mousePitch = -steer.Y;
            // Zone neutre verticale : en revenant vers le centre, pas de piqué ni de cabré involontaire.
            mousePitch = Mathf.Abs(mousePitch) < MousePitchDeadband ? 0f : (mousePitch - Mathf.Sign(mousePitch) * MousePitchDeadband) / (1f - MousePitchDeadband);
            if (InvertPitch)
                mousePitch = -mousePitch;
            roll = Mathf.Clamp(roll + steer.X, -1f, 1f);
            pitch = Mathf.Clamp(pitch + mousePitch, -1f, 1f);
            if (steer.Y > MouseBrakeStart)
                MouseBrake = (steer.Y - MouseBrakeStart) / (1f - MouseBrakeStart);
            brake = Mathf.Max(brake, MouseBrake);
        }

        // Courbe « expo » : douce au centre, pleine au bord.
        float expo = Mathf.Clamp(1f - Sensitivity, 0f, 0.85f);
        roll = Expo(roll, expo);
        pitch = Expo(pitch, expo);

        Target.CurrentInput = new VehicleInput
        {
            Throttle = Input.GetActionStrength(GameActions.Throttle),
            Brake = brake,
            Pitch = pitch,
            Yaw = 0f,
            Roll = roll,
            Vertical = Input.GetAxis(GameActions.VerticalDown, GameActions.VerticalUp),
            Trim = Input.GetAxis(GameActions.TrimLeft, GameActions.TrimRight),
            Nitro = Input.IsActionPressed(GameActions.Nitro),
        };
    }

    private static float Expo(float x, float e) => x * (1f - e) + x * x * x * e;
}
