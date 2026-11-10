using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Effects;

/// <summary>
/// Gouttes d'eau sur l'écran, simulées (<see cref="DropletSim"/>, modèle de L. Bebber) :
/// - la pluie et le nuage déposent des gouttes rondes et une buée de gouttelettes, d'autant plus vite qu'on vole vite ;
/// - les gouttes fusionnent en se touchant, grossissent, puis coulent selon la gravité vue par la caméra (elles suivent le roulis) ;
/// - une accélération (nitro) souffle depuis le point de fuite : tout coule vers l'extérieur ;
/// - une goutte qui coule laisse une traîne et efface la buée sur son passage.
/// Rendu : les gouttes sont dessinées en hauteurs d'eau dans une carte (SubViewport, <c>droplet_sprite.gdshader</c>),
/// puis <c>screen_droplets.gdshader</c> réfracte la scène à travers. Le nœud est sous le HUD (couche 0).
/// </summary>
public partial class ScreenDroplets : CanvasLayer, IVehicleTracker
{
    [ExportGroup("Mouillage")]
    /// <summary>Gouttes par seconde sous une pluie d'intensité 1 (<see cref="StormWeather.RainAt"/>), à <see cref="SpeedRef"/>.</summary>
    [Export] public float RainRate { get; set; } = 18f;
    /// <summary>Gouttelettes de buée par seconde à densité de nuage 1 (<see cref="CloudEffects.Density"/>), à <see cref="SpeedRef"/>.</summary>
    [Export] public float MistRate { get; set; } = 150f;
    /// <summary>Vitesse (m/s) de référence des taux : à l'arrêt, 40 % ; deux fois plus vite, 2,4 fois plus de gouttes.</summary>
    [Export] public float SpeedRef { get; set; } = 40f;
    /// <summary>Croissance de la buée dans le nuage (px/s de rayon à densité 1) : elle finit par former des gouttes.</summary>
    [Export] public float MistGrow { get; set; } = 0.25f;
    /// <summary>Séchage hors pluie et hors nuage (px/s de rayon).</summary>
    [Export] public float DryRate { get; set; } = 0.6f;
    /// <summary>Montée du voile de buée par seconde à densité de nuage 1 (0..1).</summary>
    [Export] public float FogRate { get; set; } = 0.2f;
    /// <summary>Disparition du voile de buée par seconde hors nuage.</summary>
    [Export] public float FogDryRate { get; set; } = 0.1f;
    /// <summary>Bruine sous la pluie : petites gouttelettes par seconde à pluie 1, à <see cref="SpeedRef"/>.</summary>
    [Export] public float DrizzleRate { get; set; } = 250f;
    /// <summary>Flou de la vitre mouillée sous une pluie d'intensité 1 (0..1).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RainFog { get; set; } = 0.8f;
    /// <summary>Reformation de la buée dans les sillons (part par seconde).</summary>
    [Export] public float FogRegrow { get; set; } = 0.15f;

    [ExportGroup("Gouttes")]
    [Export] public float DropMinRadius { get; set; } = 9f;
    [Export] public float DropMaxRadius { get; set; } = 38f;
    [Export] public float MistMinRadius { get; set; } = 1.5f;
    [Export] public float MistMaxRadius { get; set; } = 4.5f;
    [Export] public int MaxDrops { get; set; } = 900;
    [Export] public int MaxMist { get; set; } = 3000;
    /// <summary>Durée (s) du rassemblement d'une goutte écrasée à l'impact : disque plat, puis goutte bombée qui peut couler.</summary>
    [Export] public float GatherTime { get; set; } = 0.5f;
    /// <summary>Largeur de la goutte écrasée à l'impact (part de son rayon final).</summary>
    [Export] public float SplashSpread { get; set; } = 1.8f;

    [ExportGroup("Écoulement")]
    /// <summary>Rayon (px, écran de 1080 de haut) d'une goutte qui se met à couler sous la seule gravité, écran vertical.</summary>
    [Export] public float StickRadius { get; set; } = 18f;
    /// <summary>Vitesse de coulure (px/s) d'une goutte tirée deux fois plus fort que son adhérence.</summary>
    [Export] public float SlideSpeed { get; set; } = 220f;
    /// <summary>Ondulation de la trajectoire (rad).</summary>
    [Export] public float Meander { get; set; } = 0.35f;
    /// <summary>Souffle radial (en g) par m/s² d'accélération vers l'avant.</summary>
    [Export] public float AirflowPerAccel { get; set; } = 0.15f;
    /// <summary>Vitesse (m/s) au-delà de laquelle l'air pousse les gouttes vers l'extérieur même sans accélérer.</summary>
    [Export] public float AirflowSpeedStart { get; set; } = 70f;
    /// <summary>Souffle radial (en g) par (m/s)² au-delà de <see cref="AirflowSpeedStart"/>.</summary>
    [Export] public float AirflowPerSpeed2 { get; set; } = 0.0006f;

    [ExportGroup("Debug")]
    /// <summary>Si ≥ 0, impose l'intensité de pluie (tests). Négatif : désactivé.</summary>
    [Export] public float ForceRain { get; set; } = -1f;
    /// <summary>Si ≥ 0, impose l'accélération vers l'avant (m/s², tests d'écoulement).</summary>
    [Export] public float ForceAccel { get; set; } = -1f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    private const string CompositeShaderPath = "res://Shaders/Effects/screen_droplets.gdshader";
    private const string SpriteShaderPath = "res://Shaders/Effects/droplet_sprite.gdshader";
    private const string WipeShaderPath = "res://Shaders/Effects/droplet_wipe.gdshader";
    private const int WipeDownscale = 4;
    private const int FloatsPerInstance = 12;   // Transform2D (8) + couleur (4)

    private readonly DropletSim _sim = new();
    private SubViewport _map;
    private MultiMesh _multi;
    private float[] _buffer = System.Array.Empty<float>();
    private SubViewport _wipe;
    private ColorRect _regrow;
    private MultiMesh _wipeMulti;
    private float[] _wipeBuffer = System.Array.Empty<float>();
    private float _fog;
    private Vector2 _gravity = Vector2.Down;
    private ShaderMaterial _composite;
    private ColorRect _rect;
    private CloudEffects _cloud;
    private float _prevSpeed;
    private float _accel;
    private float _clock;
    private float _testAccelAt = -1f;

    public override void _Ready()
    {
        Layer = 0;
        AddToGroup(IVehicleTracker.GroupName);

        // Tests : -- --rain force la pluie ; -- --drops-accel=T simule une accélération (nitro) de T à T + 1,5 s.
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--rain" && ForceRain < 0f)
                ForceRain = 1f;
            else if (arg.StartsWith("--drops-accel="))
                _testAccelAt = arg["--drops-accel=".Length..].ToFloat();
        }

        _map = new SubViewport
        {
            Name = "WaterMap",
            TransparentBg = true,
            UseHdr2D = true,          // hauteurs d'eau en flottants (> 1 px)
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
        };
        AddChild(_map);
        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _map.AddChild(new MultiMeshInstance2D
        {
            Multimesh = _multi,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>(SpriteShaderPath) },
        });

        // Carte d'essuyage (basse résolution, jamais effacée) : les gouttes qui coulent y tracent leur sillon dans la buée ;
        // un voile noir translucide la ramène vers 0 (la buée se reforme).
        _wipe = new SubViewport
        {
            Name = "WipeMap",
            UseHdr2D = true,          // la reformation est lente : 8 bits ne suffiraient pas
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Once,   // puis jamais : la carte garde les sillons
        };
        AddChild(_wipe);
        _regrow = new ColorRect { Color = new Color(0f, 0f, 0f, 0f) };
        _wipe.AddChild(_regrow);
        _wipeMulti = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _wipe.AddChild(new MultiMeshInstance2D
        {
            Multimesh = _wipeMulti,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>(WipeShaderPath) },
        });

        _composite = new ShaderMaterial { Shader = GD.Load<Shader>(CompositeShaderPath) };
        _composite.SetShaderParameter("water_map", _map.GetTexture());
        _composite.SetShaderParameter("wipe_map", _wipe.GetTexture());
        _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _composite, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        _clock += dt;
        Viewport vp = GetViewport();
        Camera3D cam = vp?.GetCamera3D();
        Vector2 screen = vp?.GetVisibleRect().Size ?? Vector2.Zero;
        if (cam == null || screen.X < 1f || screen.Y < 1f)
            return;

        Vector2I mapSize = new((int)screen.X, (int)screen.Y);
        if (_map.Size != mapSize)
            _map.Size = mapSize;
        Vector2I wipeSize = mapSize / WipeDownscale;
        if (_wipe.Size != wipeSize)
        {
            _wipe.Size = wipeSize;
            _regrow.Size = wipeSize;
        }
        float toRef = DropletSim.RefHeight / screen.Y;
        _sim.Width = screen.X * toRef;
        CopySettings();

        // Mouillage : pluie et nuage, multipliés par la vitesse (on fonce dans l'eau).
        float speed = IsInstanceValid(Target) ? Target.Speed : 0f;
        float rain = ForceRain >= 0f ? ForceRain
            : StormWeather.Instance?.RainAt(cam.GlobalPosition) ?? 0f;
        float density = GetDensity();
        float speedFactor = 0.4f + speed / Mathf.Max(SpeedRef, 1f);
        float wet = Mathf.Max(rain, density);
        float dry = wet > 0.02f ? 0f : DryRate;

        // Gravité vue par la caméra : le bas du monde projeté sur l'écran (y vers le bas).
        Basis b = cam.GlobalBasis;
        Vector2 gravity = new(Vector3.Down.Dot(b.X), -Vector3.Down.Dot(b.Y));

        // Souffle : accélération vers l'avant (lissée) et vitesse très élevée ; il pousse depuis le point de fuite.
        float accel = 0f;
        if (dt > 0f)
            accel = (speed - _prevSpeed) / dt;
        _prevSpeed = speed;
        _accel = Mathf.Lerp(_accel, accel, 1f - Mathf.Exp(-dt / 0.15f));
        float forwardAccel = Mathf.Max(_accel, 0f);
        if (ForceAccel >= 0f)
            forwardAccel = ForceAccel;
        if (_testAccelAt >= 0f && _clock >= _testAccelAt && _clock < _testAccelAt + 1.5f)
            forwardAccel = 20f;
        float overSpeed = Mathf.Max(speed - AirflowSpeedStart, 0f);
        float airflow = AirflowPerAccel * forwardAccel + AirflowPerSpeed2 * overSpeed * overSpeed;

        Vector2 vanish = screen * 0.5f;
        if (IsInstanceValid(Target) && speed > 1f)
        {
            Vector3 ahead = cam.GlobalPosition + Target.LinearVelocity.Normalized() * 100f;
            if (!cam.IsPositionBehind(ahead))
                vanish = cam.UnprojectPosition(ahead);
        }

        _gravity = gravity;
        _sim.Step(dt, gravity, vanish * toRef, airflow,
            RainRate * rain * speedFactor, (MistRate * density + DrizzleRate * rain) * speedFactor, MistGrow * density, dry);

        // Vitre mouillée : sous la pluie et dans le nuage, la scène derrière devient floue (nette à travers les gouttes,
        // comme dans RainEffect) ; les sillons des gouttes qui coulent la dégagent et se referment peu à peu.
        float fogTarget = Mathf.Max(density > 0.02f ? 1f : 0f, rain * RainFog);
        _fog = Mathf.MoveToward(_fog, fogTarget, (fogTarget > _fog ? FogRate : FogDryRate) * dt);
        _regrow.Color = new Color(0f, 0f, 0f, 1f - Mathf.Exp(-FogRegrow * dt));
        _composite.SetShaderParameter("fog_level", _fog);

        UpdateMesh(screen.Y / DropletSim.RefHeight);
        UpdateWipe(screen.Y / DropletSim.RefHeight / WipeDownscale);
        _rect.Visible = _sim.Count > 0 || _fog > 0.001f;
    }

    /// <summary>Sillons : chaque goutte qui coule essuie la buée sur un disque un peu plus large qu'elle.</summary>
    private void UpdateWipe(float scale)
    {
        int n = 0;
        for (int i = 0; i < _sim.Count; i++)
            if (_sim.Drops[i].Sliding)
                n++;
        if (_wipeMulti.InstanceCount < n)
        {
            _wipeMulti.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(n + 1));
            _wipeBuffer = new float[_wipeMulti.InstanceCount * FloatsPerInstance];
        }
        int k = 0;
        for (int i = 0; i < _sim.Count; i++)
        {
            ref DropletSim.Drop d = ref _sim.Drops[i];
            if (!d.Sliding)
                continue;
            float size = 2.6f * d.R * scale;
            Vector2 o = d.Pos * scale;
            _wipeBuffer[k + 0] = size; _wipeBuffer[k + 1] = 0f; _wipeBuffer[k + 2] = 0f; _wipeBuffer[k + 3] = o.X;
            _wipeBuffer[k + 4] = 0f; _wipeBuffer[k + 5] = size; _wipeBuffer[k + 6] = 0f; _wipeBuffer[k + 7] = o.Y;
            _wipeBuffer[k + 8] = 1f; _wipeBuffer[k + 9] = 1f; _wipeBuffer[k + 10] = 1f; _wipeBuffer[k + 11] = 1f;
            k += FloatsPerInstance;
        }
        _wipeMulti.VisibleInstanceCount = n;
        if (n > 0)
            RenderingServer.MultimeshSetBuffer(_wipeMulti.GetRid(), _wipeBuffer);
    }

    private void CopySettings()
    {
        _sim.DropMinRadius = DropMinRadius;
        _sim.DropMaxRadius = DropMaxRadius;
        _sim.MistMinRadius = MistMinRadius;
        _sim.MistMaxRadius = MistMaxRadius;
        _sim.MaxDrops = MaxDrops;
        _sim.MaxMist = MaxMist;
        _sim.StickRadius = StickRadius;
        _sim.SlideSpeed = SlideSpeed;
        _sim.Meander = Meander;
        _sim.GatherTime = GatherTime;
    }

    /// <summary>Une instance par goutte : position, taille, étirement dans le sens de la glisse.</summary>
    private void UpdateMesh(float scale)
    {
        int n = _sim.Count;
        if (_multi.InstanceCount < n)
        {
            _multi.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(n + 1));
            _buffer = new float[_multi.InstanceCount * FloatsPerInstance];
        }
        for (int i = 0; i < n; i++)
        {
            ref DropletSim.Drop d = ref _sim.Drops[i];
            float r = d.R * scale;
            float pop = Mathf.SmoothStep(0f, 0.03f, d.Age);   // impact : la goutte s'écrase
            float gather = _sim.Gather(d);
            r *= Mathf.Lerp(SplashSpread, 1f, gather);           // écrasée, elle couvre plus large, puis se rétracte
            // Goutte immobile : elle pend, étirée dans le sens de la gravité (×1,5 chez Bebber), une fois rassemblée.
            float g = _gravity.Length();
            Vector2 dir = g > 0.05f ? _gravity / g : Vector2.Down;
            float sag = 1f + 0.35f * Mathf.Min(g, 1f) * gather * (d.Mist ? 0.3f : 1f);
            float stretch = 0f;
            float vl = d.Vel.Length();
            if (d.Sliding && vl > 1f)
            {
                dir = d.Vel / vl;
                stretch = Mathf.Clamp(vl / Mathf.Max(SlideSpeed, 1f), 0f, 1f);
            }
            Vector2 x = dir * (2f * r * (sag + 0.6f * stretch));
            Vector2 y = new Vector2(-dir.Y, dir.X) * (2f * r * (1f - 0.1f * stretch));
            Vector2 o = d.Pos * scale;
            int k = i * FloatsPerInstance;
            _buffer[k + 0] = x.X; _buffer[k + 1] = y.X; _buffer[k + 2] = 0f; _buffer[k + 3] = o.X;
            _buffer[k + 4] = x.Y; _buffer[k + 5] = y.Y; _buffer[k + 6] = 0f; _buffer[k + 7] = o.Y;
            _buffer[k + 8] = r / 64f; _buffer[k + 9] = stretch > 0f ? stretch : -(d.Phase % 1f + 0.01f); _buffer[k + 10] = pop; _buffer[k + 11] = gather;
        }
        _multi.VisibleInstanceCount = n;
        if (n > 0)
            RenderingServer.MultimeshSetBuffer(_multi.GetRid(), _buffer);
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
