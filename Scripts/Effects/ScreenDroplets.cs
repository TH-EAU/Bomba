using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Gouttes d'eau sur l'écran dans les nuages, pilotées par une carte de rails (<see cref="DropMap"/>).
/// Le mouillage suit la densité du nuage sous le véhicule (<see cref="CloudEffects.Density"/>) ; les gouttes
/// restent quasi immobiles sur leurs rails ; seul le nitro les fait glisser vers l'extérieur,
/// les étire et vide les rails. Rendu : <c>Shaders/Effects/screen_droplets.gdshader</c>.
/// Le nœud est sous le HUD (couche 0).
/// </summary>
public partial class ScreenDroplets : CanvasLayer, IVehicleTracker
{
    [ExportGroup("Mouillage")]
    /// <summary>Couverture gagnée par seconde à densité 1.</summary>
    [Export] public float WetRate { get; set; } = 0.9f;
    /// <summary>Couverture perdue par seconde hors nuage.</summary>
    [Export] public float DryRate { get; set; } = 0.12f;
    /// <summary>Couverture gagnée par seconde à intensité de pluie 1 (sous l'orage, <see cref="StormWeather.RainAt"/>).</summary>
    [Export] public float RainWetRate { get; set; } = 2.0f;

    [ExportGroup("Écoulement")]
    /// <summary>Glisse hors nitro : les gouttes restent quasi immobiles, à peine une reptation.</summary>
    [Export] public float BaseFlow { get; set; } = 0.02f;
    /// <summary>Glisse ajoutée pendant le nitro.</summary>
    [Export] public float NitroFlow { get; set; } = 2.4f;
    /// <summary>Couverture retirée par seconde pendant le nitro.</summary>
    [Export] public float ClearRate { get; set; } = 0.8f;

    [ExportGroup("Renouvellement")]
    /// <summary>Cycles de vie par seconde à pleine vitesse : une goutte vit environ 1 / RenewRate s.</summary>
    [Export] public float RenewRate { get; set; } = 0.3f;
    /// <summary>Vitesse (m/s) sous laquelle les gouttes ne se renouvellent plus.</summary>
    [Export] public float RenewSpeedStart { get; set; } = 5f;
    /// <summary>Vitesse (m/s) de renouvellement maximal.</summary>
    [Export] public float RenewSpeedFull { get; set; } = 40f;

    [ExportGroup("Carte des rails")]
    /// <summary>Carte des rails (blanc = chemin des gouttes, épais = perles). Vide : carte par défaut.</summary>
    [Export] public Texture2D DropMap { get; set; }

    [ExportGroup("Debug")]
    /// <summary>Si ≥ 0, remplace la couverture calculée (tests). Négatif : désactivé.</summary>
    [Export] public float ForceCoverage { get; set; } = -1f;
    /// <summary>Si ≥ 0, remplace la poussée calculée (tests d'écoulement).</summary>
    [Export] public float ForceStreak { get; set; } = -1f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    private const string ShaderPath = "res://Shaders/Effects/screen_droplets.gdshader";
    private const string DefaultMapPath = "res://Resources/Textures/waterDropMap.jpg";
    private const float InCloudDensity = 0.02f;
    private const float VisibleCoverage = 0.001f;

    private ColorRect _rect;
    private ShaderMaterial _material;
    private CloudEffects _cloud;
    private float _coverage;
    private float _streak;
    private float _flowTime;
    private float _renewTime;

    public override void _Ready()
    {
        Layer = 0;
        AddToGroup(IVehicleTracker.GroupName);

        _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        ImageTexture rails = DropRailMap.Get(DropMap ?? GD.Load<Texture2D>(DefaultMapPath));
        _material.SetShaderParameter("rail_map", rails);
        _material.SetShaderParameter("rail_ids", rails);
        _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float density = GetDensity();
        bool nitro = IsInstanceValid(Target) && Target.NitroActive;

        // Seul le nitro chasse les gouttes ; sinon elles restent en place et sèchent hors nuage.
        float push = nitro ? 1f : 0f;
        if (ForceStreak >= 0f)
            push = ForceStreak;

        _coverage += density * WetRate * dt;
        if (StormWeather.Instance != null && GetViewport()?.GetCamera3D() is Camera3D cam)
            _coverage += StormWeather.Instance.RainAt(cam.GlobalPosition) * RainWetRate * dt;
        if (density < InCloudDensity)
            _coverage -= DryRate * dt;
        _coverage -= push * ClearRate * dt;
        _coverage = Mathf.Clamp(_coverage, 0f, 1f);

        // Le nitro fait filer et étire les gouttes.
        _streak = Mathf.Lerp(_streak, Mathf.Clamp(push, 0f, 1f), 1f - Mathf.Exp(-4f * dt));
        _flowTime += dt * (BaseFlow + NitroFlow * _streak);

        // En mouvement, de nouvelles gouttes frappent la vitre pendant que d'autres disparaissent.
        float speed = IsInstanceValid(Target) ? Target.Speed : 0f;
        _renewTime += dt * RenewRate * Mathf.SmoothStep(RenewSpeedStart, RenewSpeedFull, speed);

        float shown = ForceCoverage >= 0f ? ForceCoverage : _coverage;
        _rect.Visible = shown >= VisibleCoverage;
        _material.SetShaderParameter("coverage", shown);
        _material.SetShaderParameter("streak", _streak);
        _material.SetShaderParameter("flow_time", _flowTime);
        _material.SetShaderParameter("renew_time", _renewTime);
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
