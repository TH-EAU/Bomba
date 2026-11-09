using System.Text;
using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// HUD de vol : panneau principal (vitesse, altitude, état, jauges), détail des modules
/// avec F1, et aide des contrôles. Toute l'interface est construite en code dans <c>_Ready</c>.
/// </summary>
public partial class DebugHud : CanvasLayer, IVehicleTracker
{
    /// <summary>Véhicule affiché. Peut être changé à l'exécution, ou valoir null.</summary>
    public Vehicle Target { get; set; }

    private const string HelpText =
        "[AZERTY] Poussée Z · Frein S · Réticule souris : gauche/droite = incliner, haut = cabrer, bas = piquer · Clavier : roulis Q/D ou A/E, tangage flèches · Nitro Maj · Recentrer clic molette · Souris Échap · Véhicule Tab · Respawn R · Caméra F5 · Debug F1";

    private static readonly Color ColorPowered = new(0.35f, 0.9f, 0.4f);
    private static readonly Color ColorNitro = new(1f, 0.6f, 0.1f);
    private static readonly Color ColorBallistic = new(1f, 0.3f, 0.3f);

    private VBoxContainer _statsBox;
    private Label _nameLabel;
    private Label _speedLabel;
    private Label _altitudeLabel;
    private Label _stateLabel;
    private Label _cloudLabel;
    private Label _windLabel;
    private Bomba.UI.WindIndicator _windIndicator;
    private CloudEffects _cloudEffects;
    private ProgressBar _powerBar;
    private ProgressBar _authorityBar;
    private ProgressBar _nitroBar;
    private PanelContainer _detailPanel;
    private Label _detailLabel;

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);

        // Racine plein écran, sans capter la souris.
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        root.AddChild(margin);

        var layout = new VBoxContainer();
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddChild(layout);

        // Colonne en haut à gauche : panneau principal puis panneau détail.
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        layout.AddChild(column);

        PanelContainer mainPanel = CreatePanel();
        column.AddChild(mainPanel);
        var mainBox = new VBoxContainer();
        mainPanel.AddChild(mainBox);

        _nameLabel = CreateLabel(22);
        mainBox.AddChild(_nameLabel);

        _statsBox = new VBoxContainer();
        mainBox.AddChild(_statsBox);
        _speedLabel = CreateLabel(16);
        _statsBox.AddChild(_speedLabel);
        _altitudeLabel = CreateLabel(16);
        _statsBox.AddChild(_altitudeLabel);
        _stateLabel = CreateLabel(18);
        _statsBox.AddChild(_stateLabel);
        _cloudLabel = CreateLabel(14);
        _statsBox.AddChild(_cloudLabel);
        var windRow = new HBoxContainer();
        _statsBox.AddChild(windRow);
        _windIndicator = new Bomba.UI.WindIndicator();
        windRow.AddChild(_windIndicator);
        _windLabel = CreateLabel(14);
        _windLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        windRow.AddChild(_windLabel);
        _powerBar = CreateBar(_statsBox, "Puissance");
        _authorityBar = CreateBar(_statsBox, "Autorité");
        _nitroBar = CreateBar(_statsBox, "Nitro");

        _detailPanel = CreatePanel();
        _detailPanel.Visible = false;
        column.AddChild(_detailPanel);
        _detailLabel = CreateLabel(14);
        _detailPanel.AddChild(_detailLabel);

        // Espaceur qui pousse l'aide en bas.
        var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        layout.AddChild(spacer);

        var help = CreateLabel(13);
        help.Text = HelpText;
        help.Modulate = new Color(1f, 1f, 1f, 0.75f);
        layout.AddChild(help);
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(GameActions.ToggleDebug))
            DebugSettings.ShowDebug = !DebugSettings.ShowDebug;

        _detailPanel.Visible = DebugSettings.ShowDebug;

        if (!IsInstanceValid(Target))
        {
            _nameLabel.Text = "Aucun véhicule";
            _statsBox.Visible = false;
            _detailLabel.Text = string.Empty;
            return;
        }

        Vehicle v = Target;
        _statsBox.Visible = true;
        _nameLabel.Text = v.DisplayName;
        _speedLabel.Text = $"Vitesse : {v.Speed:F1} m/s ({v.Speed * 3.6f:F0} km/h)";
        _altitudeLabel.Text = $"Altitude : {v.GlobalPosition.Y:F1} m";

        (string stateText, Color stateColor) = v.State switch
        {
            FlightState.Powered => ("PROPULSÉ", ColorPowered),
            FlightState.Nitro => ("NITRO", ColorNitro),
            _ => ("BALISTIQUE", ColorBallistic),
        };
        _stateLabel.Text = stateText;
        _stateLabel.AddThemeColorOverride("font_color", stateColor);

        UpdateCloudLine();
        UpdateWind(v);

        _powerBar.Value = v.PowerRatio;
        _authorityBar.Value = v.Authority;
        _nitroBar.Value = v.NitroGauge;

        if (DebugSettings.ShowDebug)
            _detailLabel.Text = BuildModuleDetail(v);
    }

    /// <summary>Vent au point de l'engin (global + orage + rafales) : force, direction relative et ascendance.</summary>
    private void UpdateWind(Vehicle v)
    {
        Vector3 w = Wind.GetWindAt(v.GlobalPosition);
        Basis b = v.GlobalBasis.Orthonormalized();
        Vector3 fwd = new Vector3(-b.Z.X, 0f, -b.Z.Z);
        Vector3 right = new Vector3(b.X.X, 0f, b.X.Z);
        fwd = fwd.LengthSquared() > 1e-6f ? fwd.Normalized() : Vector3.Forward;
        right = right.LengthSquared() > 1e-6f ? right.Normalized() : Vector3.Right;
        var rel = new Vector2(w.Dot(right), w.Dot(fwd));
        _windIndicator.RelativeWind = rel;

        float h = rel.Length();
        string where = h < 0.5f ? "calme"
            : rel.Y < -0.7f * h ? "de face"
            : rel.Y > 0.7f * h ? "dans le dos"
            : rel.X > 0f ? "de travers, pousse à droite" : "de travers, pousse à gauche";
        string vertical = Mathf.Abs(w.Y) > 1f ? (w.Y > 0f ? $" · ascendance {w.Y:F0} m/s" : $" · descendance {-w.Y:F0} m/s") : "";
        _windLabel.Text = $"Vent : {h:F0} m/s ({h * 3.6f:F0} km/h)\n{where}{vertical}";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!DebugSettings.ShowDebug)
            return;
        if (e is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        Key code = key.PhysicalKeycode;
        if (code < Key.Key1 || code > Key.Key9)
            return;
        if (!IsInstanceValid(Target))
            return;

        var modules = Target.Modules;
        int index = (int)code - (int)Key.Key1;
        if (index < modules.Count)
            modules[index].Enabled = !modules[index].Enabled;
    }

    /// <summary>Ligne d'état nuage : repéré, orage, caché, dans le nuage, ascendance, moteurs coupés.</summary>
    private void UpdateCloudLine()
    {
        if (!IsInstanceValid(_cloudEffects))
            _cloudEffects = FindCloudEffects();
        if (!IsInstanceValid(_cloudEffects))
        {
            _cloudLabel.Text = string.Empty;
            return;
        }

        CloudEffects c = _cloudEffects;
        string status = string.Empty;
        Color color = new(1f, 1f, 1f);

        if (Detection.IsSpotted)
        {
            status = "REPÉRÉ !";
            color = ColorBallistic;
        }
        else if (c.Storm > 0.25f)
        {
            status = "ORAGE ⚡";
            color = new Color(0.75f, 0.45f, 1f);
        }
        else if (c.Hidden)
        {
            status = "CACHÉ (nuage)";
            color = new Color(0.6f, 0.85f, 1f);
        }
        else if (c.InCloud)
        {
            status = "Dans le nuage";
            color = new Color(0.6f, 0.6f, 0.6f);
        }

        if (c.Updraft > 1f)
            status += (status.Length > 0 ? " · " : string.Empty) + $"Ascendance +{c.Updraft:F1} m/s";
        if (c.Stalled)
            status += (status.Length > 0 ? " · " : string.Empty) + "MOTEURS COUPÉS";

        _cloudLabel.Text = status;
        _cloudLabel.AddThemeColorOverride("font_color", color);
    }

    private CloudEffects FindCloudEffects()
    {
        foreach (Node n in GetTree().GetNodesInGroup(IVehicleTracker.GroupName))
            if (n is CloudEffects c)
                return c;
        return null;
    }

    private static string BuildModuleDetail(Vehicle v)
    {
        var sb = new StringBuilder();
        var modules = v.Modules;
        for (int i = 0; i < modules.Count; i++)
        {
            VehicleModule m = modules[i];
            string status = m.Enabled ? "ON" : "OFF";
            sb.AppendLine($"{i + 1} {m.GetType().Name,-12} {m.Name,-14} {status,-3} {m.LastForce.Length(),6:F0} N");
        }
        return sb.ToString().TrimEnd();
    }

    private static PanelContainer CreatePanel()
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f) };
        style.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label CreateLabel(int fontSize)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static ProgressBar CreateBar(Node parent, string caption)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var captionLabel = CreateLabel(14);
        captionLabel.Text = caption;
        captionLabel.CustomMinimumSize = new Vector2(90f, 0f);
        row.AddChild(captionLabel);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(180f, 14f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddChild(bar);
        return bar;
    }
}
