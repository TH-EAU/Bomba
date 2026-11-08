using Godot;

namespace Bomba.Core;

/// <summary>Noms des actions d'entrée du jeu.</summary>
public static class GameActions
{
    public const string Throttle = "throttle";
    public const string Brake = "brake";
    public const string PitchUp = "pitch_up";
    public const string PitchDown = "pitch_down";
    public const string YawLeft = "yaw_left";
    public const string YawRight = "yaw_right";
    public const string RollLeft = "roll_left";
    public const string RollRight = "roll_right";
    public const string VerticalUp = "vertical_up";
    public const string VerticalDown = "vertical_down";
    public const string TrimLeft = "trim_left";
    public const string TrimRight = "trim_right";
    public const string Nitro = "nitro";
    public const string SwitchVehicle = "switch_vehicle";
    public const string Respawn = "respawn";
    public const string ToggleDebug = "toggle_debug";
}

/// <summary>
/// Autoload "InputBootstrap" : enregistre l'InputMap en code au démarrage.
/// Les touches clavier sont PHYSIQUES (positions QWERTY) : W/A/S/D devient Z/Q/S/D sur AZERTY.
/// </summary>
public partial class InputBootstrap : Node
{
    private const float Deadzone = 0.15f;

    public override void _EnterTree()
    {
        // Clavier : poussée et lacet sur WASD (ZQSD en AZERTY), tangage sur les flèches.
        Bind(GameActions.Throttle, Key(Godot.Key.W), Axis(JoyAxis.TriggerRight, 1f));
        Bind(GameActions.Brake, Key(Godot.Key.S), Axis(JoyAxis.TriggerLeft, 1f));
        Bind(GameActions.PitchUp, Key(Godot.Key.Up), Axis(JoyAxis.LeftY, -1f));
        Bind(GameActions.PitchDown, Key(Godot.Key.Down), Axis(JoyAxis.LeftY, 1f));
        Bind(GameActions.YawLeft, Key(Godot.Key.A), Key(Godot.Key.Left), Axis(JoyAxis.LeftX, -1f));
        Bind(GameActions.YawRight, Key(Godot.Key.D), Key(Godot.Key.Right), Axis(JoyAxis.LeftX, 1f));
        Bind(GameActions.RollLeft, Key(Godot.Key.Q), Button(JoyButton.LeftShoulder));
        Bind(GameActions.RollRight, Key(Godot.Key.E), Button(JoyButton.RightShoulder));
        Bind(GameActions.VerticalUp, Key(Godot.Key.Space), Axis(JoyAxis.RightY, -1f));
        Bind(GameActions.VerticalDown, Key(Godot.Key.Ctrl), Axis(JoyAxis.RightY, 1f));
        Bind(GameActions.TrimLeft, Key(Godot.Key.Z), Button(JoyButton.DpadLeft));
        Bind(GameActions.TrimRight, Key(Godot.Key.C), Button(JoyButton.DpadRight));
        Bind(GameActions.Nitro, Key(Godot.Key.Shift), Button(JoyButton.A));
        Bind(GameActions.SwitchVehicle, Key(Godot.Key.Tab), Button(JoyButton.Back));
        Bind(GameActions.Respawn, Key(Godot.Key.R), Button(JoyButton.Start));
        Bind(GameActions.ToggleDebug, Key(Godot.Key.F1));
    }

    private static void Bind(string action, params InputEvent[] events)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action, Deadzone);
        foreach (InputEvent e in events)
            InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };

    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Axis(JoyAxis axis, float direction) =>
        new() { Axis = axis, AxisValue = direction };
}
