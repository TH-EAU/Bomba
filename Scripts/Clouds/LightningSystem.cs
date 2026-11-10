using System.Linq;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Met en scène les éclairs émis par <see cref="CloudField"/> : canaux propagés (<see cref="LightningBolt"/>),
/// flash de lumière et tonnerre (enregistrements par distance, fondus entre eux).
/// Un éclair avec cible frappe cette cible ; sans cible, il peut descendre vers le bas (trait décoratif).
/// </summary>
public partial class LightningSystem : Node3D
{
    /// <summary>Probabilité qu'un éclair décoratif ait un trait visible ; sinon, flash dans le nuage seul.</summary>
    [Export] public float BoltChance { get; set; } = 0.6f;
    /// <summary>Probabilité qu'un éclair décoratif parcoure aussi le nuage (ramifications le long de sa frange).</summary>
    [Export] public float SpiderChance { get; set; } = 0.85f;
    [Export] public float MinDownLength { get; set; } = 250f;
    /// <summary>Probabilité qu'un éclair sorti du nuage frappe la mer (nuage-sol).</summary>
    [Export] public float GroundStrikeChance { get; set; } = 0.6f;
    /// <summary>Altitude de la mer (m), cible des éclairs nuage-sol.</summary>
    [Export] public float SeaLevel { get; set; } = -60f;
    [Export] public float MaxDownLength { get; set; } = 6000f;
    [Export] public float FlashEnergy { get; set; } = 16f;
    [Export] public float FlashRange { get; set; } = 8000f;

    [ExportGroup("Rendu")]
    /// <summary>Luminosité HDR du canal. Au-dessus de 8 (seuil du glow de l'environnement) le fait rayonner : c'est le halo.</summary>
    [Export] public float BoltEnergy { get; set; } = 20f;
    /// <summary>Demi-largeur du tronc (m) ; les branches sont plus fines.</summary>
    [Export] public float BoltWidth { get; set; } = 2.5f;
    /// <summary>Demi-largeur minimale à l'écran (pixels) : le canal reste visible de loin.</summary>
    [Export] public float BoltMinPixels { get; set; } = 1f;
    [Export] public Color BoltColor { get; set; } = new(0.75f, 0.45f, 1f);
    /// <summary>Demi-largeur du tronc (m) d'un éclair qui frappe l'engin : il finit à quelques mètres de la caméra.</summary>
    [Export] public float StrikeBoltWidth { get; set; } = 0.35f;

    [ExportGroup("Tonnerre")]
    /// <summary>
    /// Enregistrements du tonnerre à différentes distances, croissantes. Chacun contient déjà le retard de
    /// propagation, l'atténuation des aigus et la réverbération. Vide : sons par défaut (Resources/Sounds/Thunder).
    /// </summary>
    [Export] public AudioStream[] ThunderStreams { get; set; } = System.Array.Empty<AudioStream>();
    /// <summary>Distance (m) de chaque enregistrement de <see cref="ThunderStreams"/>, dans le même ordre.</summary>
    [Export] public float[] ThunderDistances { get; set; } = System.Array.Empty<float>();
    [Export] public float ThunderVolumeDb { get; set; } = 0f;
    [Export] public string ThunderBus { get; set; } = "Master";

    private const int MaxActiveBolts = 6;
    private const float DownStep = 40f;
    private const float CloudExitDensity = 0.02f;
    private const float FlashDuration = 0.3f;
    private static readonly Color FlashColor = new(0.7f, 0.5f, 1f);

    private readonly RandomNumberGenerator _rng = new();
    private CloudField _field;

    public override void _Ready()
    {
        _rng.Randomize();
        // Tonnerre par défaut : Thunder1 enregistré à 0, 2, 6, 10 et 20 km.
        if (ThunderStreams.Length == 0)
        {
            ThunderDistances = new[] { 0f, 2000f, 6000f, 10000f, 20000f };
            ThunderStreams = new[] { "Thunder1", "Thunder1-2km", "Thunder1-6km", "Thunder1-10km", "Thunder1-20km" }
                .Select(name => GD.Load<AudioStream>($"res://Resources/Sounds/Thunder/{name}.wav")).ToArray();
        }
        TryConnectToField();
    }

    public override void _Process(double delta)
    {
        if (_testBolts)
        {
            TryConnectToField();
            UpdateTestBolts((float)delta);
            return;
        }
        if (TryConnectToField())
            SetProcess(false);
    }

    // Test : -- --test-bolts fait tomber un éclair devant la caméra toutes les 0,8 s, à 400 m, 1,5 km puis 4 km.
    private bool _testBolts = OS.GetCmdlineUserArgs().Contains("--test-bolts");
    private float _testClock;
    private int _testIndex;
    private static readonly float[] TestDistances = { 400f, 1500f, 4000f };

    private void UpdateTestBolts(float delta)
    {
        _testClock -= delta;
        Camera3D cam = GetViewport()?.GetCamera3D();
        if (_testClock > 0f || cam == null)
            return;
        _testClock = 0.8f;
        float d = TestDistances[_testIndex++ % TestDistances.Length];
        Vector3 fwd = -cam.GlobalBasis.Z with { Y = 0f };
        Vector3 ground = cam.GlobalPosition + fwd.Normalized() * d;
        Vector3 from = ground with { Y = cam.GlobalPosition.Y + 0.4f * d };
        GD.Print($"[Foudre test] éclair à {d} m");
        AddBolt(from, ground with { Y = SeaLevel });
    }

    public override void _ExitTree()
    {
        if (_field != null && IsInstanceValid(_field))
            _field.LightningTriggered -= OnLightningTriggered;
        if (_field != null && IsInstanceValid(_field))
            _field.StrikeTriggered -= OnStrikeTriggered;
        _field = null;
    }

    private bool TryConnectToField()
    {
        if (_field != null)
            return true;
        if (CloudField.Instance == null)
            return false;
        _field = CloudField.Instance;
        _field.LightningTriggered += OnLightningTriggered;
        _field.StrikeTriggered += OnStrikeTriggered;
        return true;
    }

    private void OnLightningTriggered(Vector3 position, Vector3 target, bool hasTarget)
    {
        if (hasTarget)
            AddBolt(position, target);
        else
        {
            if (_rng.Randf() < SpiderChance)
                AddSpider(position);
            if (_rng.Randf() < BoltChance)
                AddBolt(position, DownwardTarget(position));
        }

        AddFlash(position);
        ScheduleThunder(position);
    }

    /// <summary>
    /// Frappe sur l'engin : le traceur part tout de suite ; flash, lueur du nuage et tonnerre attendent l'arc en retour,
    /// au point d'impact à cet instant (la cible a bougé).
    /// </summary>
    private void OnStrikeTriggered(Vector3 from, Node3D target, Vector3 targetLocal, float leaderTime)
    {
        if (target == null || !IsInstanceValid(target))
            return;
        var bolt = new LightningBolt();
        AddChild(bolt);
        bolt.SetLook(BoltEnergy, StrikeBoltWidth, BoltMinPixels, BoltColor);
        bolt.SetupStrike(from, target, targetLocal, leaderTime, _rng);

        Vector3 fallback = target.GlobalTransform * targetLocal;
        GetTree().CreateTimer(leaderTime, false, true).Timeout += () =>
        {
            Vector3 impact = IsInstanceValid(target) ? target.GlobalTransform * targetLocal : fallback;
            AddFlash(impact);
            _field?.FlashCloud(impact);
            ScheduleThunder(impact);
        };
    }

    /// <summary>
    /// Tonnerre : les deux enregistrements qui encadrent la distance de l'éclair sont joués ensemble, en fondu
    /// enchaîné à puissance constante (à 8 km : moitié 6 km, moitié 10 km). Les fichiers contiennent déjà le retard, l'atténuation
    /// des aigus et la réverbération : rien n'est ajouté ici, sauf une baisse de volume au-delà du plus lointain.
    /// Le son est placé à l'éclair (direction), sans atténuation 3D.
    /// </summary>
    private void ScheduleThunder(Vector3 position)
    {
        int n = Mathf.Min(ThunderStreams.Length, ThunderDistances.Length);
        Camera3D cam = GetViewport()?.GetCamera3D();
        if (n == 0 || cam == null)
            return;

        float distance = cam.GlobalPosition.DistanceTo(position);
        int hi = 0;
        while (hi < n && ThunderDistances[hi] < distance)
            hi++;
        if (hi == 0)
            PlayThunder(ThunderStreams[0], position, 1f);
        else if (hi == n)
        {
            // Au-delà du plus lointain : −6 dB par doublement de distance.
            float far = ThunderDistances[n - 1];
            PlayThunder(ThunderStreams[n - 1], position, far / Mathf.Max(distance, far));
        }
        else
        {
            float t = (distance - ThunderDistances[hi - 1]) / Mathf.Max(ThunderDistances[hi] - ThunderDistances[hi - 1], 1f);
            PlayThunder(ThunderStreams[hi - 1], position, Mathf.Sqrt(1f - t));   // puissance constante
            PlayThunder(ThunderStreams[hi], position, Mathf.Sqrt(t));
        }
    }

    private void PlayThunder(AudioStream stream, Vector3 position, float gain)
    {
        if (stream == null || gain < 0.01f)
            return;
        var player = new AudioStreamPlayer3D
        {
            Stream = stream,
            Bus = ThunderBus,
            VolumeDb = ThunderVolumeDb + Mathf.LinearToDb(gain),
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,   // distance déjà dans le fichier
            MaxDistance = 0f,
            AttenuationFilterDb = 0f,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        AddChild(player);
        player.GlobalPosition = position;
        player.Finished += player.QueueFree;
        player.Play();
    }

    private void AddSpider(Vector3 from)
    {
        if (GetChildren().OfType<LightningBolt>().Count() >= MaxActiveBolts)
            return;

        var bolt = new LightningBolt();
        AddChild(bolt);
        bolt.SetLook(BoltEnergy, BoltWidth, BoltMinPixels, BoltColor);
        bolt.SetupSpider(from, _field, _rng);
    }

    private void AddBolt(Vector3 from, Vector3 to)
    {
        if (GetChildren().OfType<LightningBolt>().Count() >= MaxActiveBolts)
            return;

        var bolt = new LightningBolt();
        AddChild(bolt);
        bolt.SetLook(BoltEnergy, BoltWidth, BoltMinPixels, BoltColor);
        bolt.Setup(from, to, _rng, to.Y <= SeaLevel + 1f);
    }

    private void AddFlash(Vector3 position)
    {
        var light = new OmniLight3D
        {
            LightColor = FlashColor,
            LightEnergy = FlashEnergy,
            OmniRange = FlashRange,
            ShadowEnabled = false,
        };
        AddChild(light);
        light.GlobalPosition = position;

        Tween tween = CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, FlashDuration);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    /// <summary>
    /// Point d'arrivée d'un éclair qui descend : on cherche la sortie du nuage (jusqu'à MaxDownLength sous le flash).
    /// Sous la base, l'éclair frappe souvent la mer (nuage-sol) ; sinon il s'arrête un peu sous la base.
    /// </summary>
    private Vector3 DownwardTarget(Vector3 origin)
    {
        float exit = -1f;
        for (float d = 0f; d < MaxDownLength; d += DownStep)
        {
            if (_field.Sample(origin + Vector3.Down * d).Density <= CloudExitDensity)
            {
                exit = d;
                break;
            }
        }

        float length;
        if (exit < 0f)
            length = _rng.RandfRange(MinDownLength, MaxDownLength * 0.2f);   // reste dans le nuage (flash interne)
        else if (_rng.Randf() < GroundStrikeChance)
            length = Mathf.Max(origin.Y - SeaLevel, exit + 40f);          // nuage-sol : jusqu'à la mer
        else
            length = exit + _rng.RandfRange(40f, 160f);

        Vector2 drift = Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau) * _rng.RandfRange(0f, 0.15f * length);
        return origin + Vector3.Down * length + new Vector3(drift.X, 0f, drift.Y);
    }
}
