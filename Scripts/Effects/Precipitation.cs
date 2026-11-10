using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Pluie et grêle autour de la caméra, dans la zone de l'orage (<see cref="StormWeather"/>).
/// Deux couches de <see cref="GpuParticles3D"/> en coordonnées monde : les particules vivent dans une boîte qui suit
/// la caméra (<c>Shaders/Effects/precip_process.gdshader</c>) et sont dessinées en traits étirés selon la vitesse
/// relative à la caméra, éclairés (<c>Shaders/Effects/precip_draw.gdshader</c>).
/// Les particules sont permanentes : l'intensité ne fait qu'en révéler une partie (apparition progressive dans le shader)
/// et masque la couche hors de la pluie (<c>Visible</c>), sans émission ni arrêt.
/// Script outil : les couches sont construites sous "_Generated", sans propriétaire. La logique ne tourne que hors éditeur.
/// </summary>
[Tool]
public partial class Precipitation : Node3D, IVehicleTracker
{
    private const string GeneratedName = "_Generated";
    private const string ProcessShaderPath = "res://Shaders/Effects/precip_process.gdshader";
    private const string DrawShaderPath = "res://Shaders/Effects/precip_draw.gdshader";
    private const string DefaultRainNormalPath = "res://Resources/Textures/WaterDropNormal.png";
    /// <summary>Durée de vie (s) : les particules ne sont jamais recyclées, la boîte les replie autour de la caméra.</summary>
    private const double ParticleLifetime = 1000.0;
    private static readonly Aabb AlwaysVisible = new(new Vector3(-1e5f, -1e5f, -1e5f), new Vector3(2e5f, 2e5f, 2e5f));

    [ExportGroup("Pluie")]
    /// <summary>Nombre de particules de pluie à intensité 1.</summary>
    [Export] public int RainAmount { get; set; } = 4000;
    /// <summary>Taille (m) de la boîte de pluie, centrée sur la caméra.</summary>
    [Export] public Vector3 RainBox { get; set; } = new(60f, 40f, 60f);
    /// <summary>Vitesse de chute (m/s), en plus du vent.</summary>
    [Export] public float RainFallSpeed { get; set; } = 9f;
    /// <summary>Longueur du trait (s) : vitesse relative × ce temps.</summary>
    [Export] public float RainStreakTime { get; set; } = 0.05f;
    /// <summary>Largeur du trait (m), avec un minimum en pixels (<see cref="MinPixels"/>).</summary>
    [Export] public float RainWidth { get; set; } = 0.02f;
    [Export] public Color RainColor { get; set; } = new(0.62f, 0.78f, 0.95f);
    [Export] public float RainOpacity { get; set; } = 0.75f;
    /// <summary>Relief des gouttes (normal map d'une traînée, tête en bas). Vide : WaterDropNormal.png. Sert aux reflets et à la réfraction.</summary>
    [Export] public Texture2D RainNormalMap { get; set; }
    /// <summary>Force du relief de la normal map.</summary>
    [Export(PropertyHint.Range, "0,3,0.05")] public float RainNormalStrength { get; set; } = 1f;
    /// <summary>Décalage max (pixels) de la scène vue à travers une goutte.</summary>
    [Export] public float RainRefractionPixels { get; set; } = 6f;
    /// <summary>Part de la scène réfractée dans la couleur des gouttes (0 = rendu d'origine, sans réfraction).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RainRefraction { get; set; } = 0.7f;

    [ExportGroup("Grêle")]
    /// <summary>Nombre de grêlons à intensité 1.</summary>
    [Export] public int HailAmount { get; set; } = 800;
    /// <summary>Taille (m) de la boîte de grêle, centrée sur la caméra.</summary>
    [Export] public Vector3 HailBox { get; set; } = new(40f, 30f, 40f);
    /// <summary>Vitesse de chute (m/s), en plus du vent.</summary>
    [Export] public float HailFallSpeed { get; set; } = 25f;
    /// <summary>Longueur du trait (s) : vitesse relative × ce temps.</summary>
    [Export] public float HailStreakTime { get; set; } = 0.015f;
    /// <summary>Largeur du trait (m), avec un minimum en pixels (<see cref="MinPixels"/>).</summary>
    [Export] public float HailWidth { get; set; } = 0.06f;
    [Export] public Color HailColor { get; set; } = new(0.88f, 0.94f, 1f);
    [Export] public float HailOpacity { get; set; } = 0.85f;

    [ExportGroup("Réglage")]
    /// <summary>Vitesse (1/s) de suivi de l'intensité cible.</summary>
    [Export] public float IntensitySharpness { get; set; } = 1.5f;
    /// <summary>Agitation (m/s) des particules à turbulence 1.</summary>
    [Export] public float TurbulenceScale { get; set; } = 12f;
    /// <summary>Largeur minimale des traits (pixels).</summary>
    [Export] public float MinPixels { get; set; } = 1f;

    /// <summary>Non utilisé : la précipitation suit la caméra, pas le véhicule.</summary>
    public Vehicle Target { get; set; }

    /// <summary>Une couche (pluie ou grêle) et ses matériaux.</summary>
    private sealed class Layer
    {
        public GpuParticles3D Particles;
        public ShaderMaterial Process;
        public ShaderMaterial Draw;
        public float Intensity;
    }

    private Node3D _generated;
    private Layer _rain;
    private Layer _hail;
    private float _time;
    private Vector3 _prevCamera;
    private bool _hasPrevCamera;
    // Test : -- --rain force une pluie pleine, hors orage.
    private readonly bool _forceRain = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--rain") >= 0;

    public override void _Ready()
    {
        BuildGenerated();
        if (Engine.IsEditorHint())
            return;
        AddToGroup(IVehicleTracker.GroupName);
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            return;

        float dt = (float)delta;
        _time += dt;
        Camera3D cam = GetViewport()?.GetCamera3D();
        if (cam == null)
        {
            _rain.Particles.Visible = false;
            _hail.Particles.Visible = false;
            _hasPrevCamera = false;
            return;
        }

        Vector3 camPos = cam.GlobalPosition;
        Vector3 camVel = _hasPrevCamera && dt > 0f ? (camPos - _prevCamera) / dt : Vector3.Zero;
        _prevCamera = camPos;
        _hasPrevCamera = true;

        StormWeather storm = StormWeather.Instance;
        float rainTarget = _forceRain ? 1f : storm != null ? storm.RainAt(camPos) : 0f;
        float hailTarget = storm != null ? storm.HailAt(camPos) : 0f;
        float turbulence = (storm != null ? storm.Turbulence01(camPos) : 0f) * TurbulenceScale;
        float smoothing = 1f - Mathf.Exp(-IntensitySharpness * dt);
        _rain.Intensity = Mathf.Lerp(_rain.Intensity, rainTarget, smoothing);
        _hail.Intensity = Mathf.Lerp(_hail.Intensity, hailTarget, smoothing);

        Vector3 wind = Wind.GetWindAt(camPos);
        UpdateLayer(_rain, camPos, camVel, wind, turbulence, RainBox, RainFallSpeed, RainStreakTime, RainWidth, RainColor, RainOpacity);
        UpdateLayer(_hail, camPos, camVel, wind, turbulence, HailBox, HailFallSpeed, HailStreakTime, HailWidth, HailColor, HailOpacity);
    }

    /// <summary>Reconstruit les couches sous "_Generated" (sans propriétaire : jamais enregistrées dans la scène).</summary>
    private void BuildGenerated()
    {
        Node old = GetNodeOrNull(GeneratedName);
        if (old != null)
        {
            RemoveChild(old);
            old.QueueFree();
        }

        _generated = new Node3D { Name = GeneratedName };
        AddChild(_generated);
        _rain = CreateLayer("Pluie", RainAmount);
        _hail = CreateLayer("Grele", HailAmount);

        // Relief et réfraction : la pluie seulement, la grêle garde son cylindre.
        _rain.Draw.SetShaderParameter("use_drop_normal", true);
        _rain.Draw.SetShaderParameter("drop_normal", RainNormalMap ?? GD.Load<Texture2D>(DefaultRainNormalPath));
    }

    private Layer CreateLayer(string name, int amount)
    {
        var process = new ShaderMaterial { Shader = GD.Load<Shader>(ProcessShaderPath) };
        var draw = new ShaderMaterial { Shader = GD.Load<Shader>(DrawShaderPath) };
        var particles = new GpuParticles3D
        {
            Name = name,
            Amount = amount,
            AmountRatio = 1f,
            LocalCoords = false,
            Lifetime = ParticleLifetime,
            Explosiveness = 1f,
            OneShot = false,
            FixedFps = 0,
            Interpolate = false,
            Emitting = true,
            // Couche 1 : visible des caméras ; couche 2 : éclairée par le soleil (comme les véhicules).
            Layers = 1 | 2,
            VisibilityAabb = AlwaysVisible,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = draw },
        };
        _generated.AddChild(particles);
        return new Layer { Particles = particles, Process = process, Draw = draw };
    }

    private void UpdateLayer(Layer l, Vector3 camPos, Vector3 camVel, Vector3 wind, float turbulence,
        Vector3 box, float fallSpeed, float streak, float width, Color color, float opacity)
    {
        // Les particules sont permanentes : l'intensité ne fait que les révéler (dans precip_draw) et masque la couche.
        l.Particles.Visible = l.Intensity > 0.001f;

        l.Process.SetShaderParameter("camera_pos", camPos);
        l.Process.SetShaderParameter("box_size", box);
        l.Process.SetShaderParameter("base_velocity", wind + Vector3.Down * fallSpeed);
        l.Process.SetShaderParameter("turbulence", turbulence);
        l.Process.SetShaderParameter("time_s", _time);

        l.Draw.SetShaderParameter("intensity", l.Intensity);
        l.Draw.SetShaderParameter("camera_velocity", camVel);
        l.Draw.SetShaderParameter("streak_time", streak);
        l.Draw.SetShaderParameter("width", width);
        l.Draw.SetShaderParameter("min_px", MinPixels);
        l.Draw.SetShaderParameter("color", color);
        l.Draw.SetShaderParameter("opacity", opacity);
        if (l == _rain)
        {
            l.Draw.SetShaderParameter("normal_strength", RainNormalStrength);
            l.Draw.SetShaderParameter("refraction_px", RainRefractionPixels);
            l.Draw.SetShaderParameter("refraction", RainRefraction);
        }
    }
}
