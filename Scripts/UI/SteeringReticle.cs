using Bomba.Vehicles;
using Godot;

namespace Bomba.UI;

/// <summary>
/// Réticule du pilotage souris : cercle de course, croix centrale fixe et réticule mobile
/// placé à <see cref="PlayerVehicleController.MouseSteer"/>. Ne capte pas la souris.
/// </summary>
public partial class SteeringReticle : Control
{
    /// <summary>Contrôleur dont on affiche la position du réticule.</summary>
    public PlayerVehicleController Controller { get; set; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Controller == null || !Controller.MouseSteering)
            return;

        Vector2 center = GetViewportRect().Size * 0.5f;
        float radius = Controller.MouseRadius;
        Vector2 pos = center + Controller.MouseSteer * radius;

        Color course = new(1f, 1f, 1f, 0.15f);
        Color croix = new(1f, 1f, 1f, 0.4f);
        Color liaison = new(1f, 1f, 1f, 0.25f);
        Color reticule = new(1f, 1f, 1f, 0.85f);
        Color horizon = new(1f, 1f, 1f, 0.5f);

        DrawArc(center, radius, 0f, Mathf.Tau, 96, course, 1.5f, true);

        // Zone de frein : arc du bas du cercle, entre les angles où y = MouseBrakeStart.
        // Non dessinée si le frein au réticule est désactivé (MouseBrakeStart = 1).
        if (Controller.MouseBrakeStart < 0.99f)
        {
            float brakeA0 = Mathf.Asin(Controller.MouseBrakeStart);
            float brakeA1 = Mathf.Pi - brakeA0;
            float brakeAlpha = Controller.MouseBrake > 0f ? 0.85f : 0.3f;
            DrawArc(center, radius, brakeA0, brakeA1, 32, new Color(1f, 0.2f, 0.2f, brakeAlpha), 3f, true);
        }

        DrawLine(center + Vector2.Left * 3f, center + Vector2.Right * 3f, croix, 1f, true);
        DrawLine(center + Vector2.Up * 3f, center + Vector2.Down * 3f, croix, 1f, true);

        // Horizon artificiel : segment incliné de -Bank, repères fixes à ±20°.
        if (Controller.Target != null && IsInstanceValid(Controller.Target))
        {
            Vector2 dir = Vector2.Right.Rotated(-Controller.Target.Bank);
            DrawLine(center - dir * 30f, center + dir * 30f, horizon, 2f, true);
            float mark = Mathf.DegToRad(20f);
            foreach (float a in new[] { -mark, mark })
            {
                Vector2 d = Vector2.Up.Rotated(a);
                DrawLine(center + d * 60f, center + d * 68f, horizon, 2f, true);
            }
        }

        DrawLine(center, pos, liaison, 1f, true);
        DrawArc(pos, 9f, 0f, Mathf.Tau, 32, reticule, 2f, true);
        DrawCircle(pos, 1f, reticule);
    }
}
