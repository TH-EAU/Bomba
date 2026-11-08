using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Gouttes d'eau sur l'écran dans les nuages. Le mouillage suit la densité du nuage sous le véhicule
/// (<see cref="CloudEffects.Density"/>) ; la vitesse les chasse vers les bords. Le rendu est fait par
/// <c>Shaders/Effects/screen_droplets.gdshader</c>. Le nœud est sous le HUD (couche 0).
/// </summary>
public partial class ScreenDroplets : CanvasLayer, IVehicleTracker
{
    [ExportGroup("Mouillage")]
    /// <summary>Couverture gagnée par seconde à densité 1.</summary>
    [Export] public float WetRate { get; set; } = 0.9f;
    /// <summary>Couverture perdue par seconde hors nuage.</summary>
    [Export] public float DryRate { get; set; } = 0.12f;

    [ExportGroup("Flux d'air")]
    /// <summary>Vitesse (m/s) à partir de laquelle le flux commence à chasser les gouttes.</summary>
    [Export] public float ClearSpeedStart { get; set; } = 25f;
    /// <summary>Vitesse (m/s) à partir de laquelle le flux chasse à plein régime.</summary>
    [Export] public float ClearSpeedFull { get; set; } = 70f;
    /// <summary>Couverture retirée par seconde à vitesse maximale.</summary>
    [Export] public float ClearRate { get; set; } = 0.8f;
    /// <summary>Multiplicateur du flux pendant le nitro.</summary>
    [Export] public float NitroClearBoost { get; set; } = 2f;

    [ExportGroup("Debug")]
    /// <summary>Si ≥ 0, remplace la couverture calculée (tests). Négatif : désactivé.</summary>
    [Export] public float ForceCoverage { get; set; } = -1f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    private const string ShaderPath = "res://Shaders/Effects/screen_droplets.gdshader";
    private const float InCloudDensity = 0.02f;
    private const float VisibleCoverage = 0.001f;

    private ColorRect _rect;
    private ShaderMaterial _material;
    private CloudEffects _cloud;
    private float _coverage;
    private float _streak;
    private float _flowTime;

    public override void _Ready()
    {
        Layer = 0;
        AddToGroup(IVehicleTracker.GroupName);

        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float density = GetDensity();
        bool hasTarget = IsInstanceValid(Target);
        float speed = hasTarget ? Target.Speed : 0f;
        bool nitro = hasTarget && Target.NitroActive;

        float s = Mathf.SmoothStep(ClearSpeedStart, ClearSpeedFull, speed);

        _coverage += density * WetRate * dt;
        if (density < InCloudDensity)
            _coverage -= DryRate * dt;
        _coverage -= s * ClearRate * dt * (nitro ? NitroClearBoost : 1f);
        _coverage = Mathf.Clamp(_coverage, 0f, 1f);

        _streak = Mathf.Lerp(_streak, s, 1f - Mathf.Exp(-4f * dt));
        _flowTime += dt * (0.2f + 2.5f * _streak);

        float shown = ForceCoverage >= 0f ? ForceCoverage : _coverage;
        _rect.Visible = shown >= VisibleCoverage;
        _material.SetShaderParameter("coverage", shown);
        _material.SetShaderParameter("streak", _streak);
        _material.SetShaderParameter("flow_time", _flowTime);
    }

    private float GetDensity()
    {
        if (!IsInstanceValid(_cloud))
            _cloud = FindCloudEffects();
        return IsInstanceValid(_cloud) ? _cloud.Density : 0f;
    }

    private CloudEffects FindCloudEffects()
    {
        foreach (Node n in GetTree().GetNodesInGroup(IVehicleTracker.GroupName))
            if (n is CloudEffects c)
                return c;
        return null;
    }
}
