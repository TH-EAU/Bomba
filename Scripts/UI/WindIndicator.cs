using Godot;

namespace Bomba.UI;

/// <summary>
/// Indicateur de vent relatif à l'engin : un cercle (le nez en haut) et une flèche qui montre où le vent pousse.
/// La flèche s'allonge avec la force (pleine longueur à <see cref="FullScaleSpeed"/>).
/// </summary>
public partial class WindIndicator : Control
{
    /// <summary>Vent horizontal dans le repère de l'engin (m/s) : X vers la droite, Y vers l'avant.</summary>
    public Vector2 RelativeWind { get; set; }

    /// <summary>Force (m/s) pour laquelle la flèche touche le cercle.</summary>
    [Export] public float FullScaleSpeed { get; set; } = 35f;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(56f, 56f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f - 3f;
        var faint = new Color(1f, 1f, 1f, 0.35f);
        DrawArc(c, r, 0f, Mathf.Tau, 40, faint, 1.5f, true);
        // Repère du nez de l'engin.
        DrawLine(c + new Vector2(0f, -r), c + new Vector2(0f, -r + 6f), faint, 2f, true);

        float speed = RelativeWind.Length();
        if (speed < 0.3f)
        {
            DrawCircle(c, 2.5f, faint);
            return;
        }
        // Écran : Y vers le bas ; le vent vers l'avant (Y > 0) monte à l'écran.
        Vector2 dir = new Vector2(RelativeWind.X, -RelativeWind.Y) / speed;
        float len = r * Mathf.Clamp(speed / FullScaleSpeed, 0.25f, 1f);
        Vector2 tail = c - dir * len;
        Vector2 head = c + dir * len;
        var col = new Color(0.75f, 0.9f, 1f, 0.95f);
        DrawLine(tail, head, col, 2.5f, true);
        Vector2 side = new(-dir.Y, dir.X);
        DrawPolygon(new[] { head + dir * 2f, head - dir * 8f + side * 5f, head - dir * 8f - side * 5f }, new[] { col });
    }
}
