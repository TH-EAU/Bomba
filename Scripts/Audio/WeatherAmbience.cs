using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Audio;

/// <summary>
/// Ambiance sonore météo du véhicule suivi. Six boucles tournent en continu (pluie légère et forte,
/// grêle, vent léger, moyen et fort) ; seul leur volume varie, selon ce que ressent le véhicule.
/// Pluie et vent se mélangent à puissance constante entre deux niveaux voisins (racines des gains),
/// donc le passage d'un niveau à l'autre ne creuse pas de trou sonore. Les volumes sont lissés en dB.
/// Les grincements du bateau sont des coups jetables, plus fréquents quand le vent d'orage, la turbulence
/// ou la rotation du véhicule montent (<c>stress</c>).
/// </summary>
public partial class WeatherAmbience : Node, IVehicleTracker
{
    [ExportGroup("Volumes")]
    /// <summary>Volume (dB) de la pluie à pleine intensité.</summary>
    [Export] public float RainVolumeDb { get; set; } = -4f;
    /// <summary>Volume (dB) de la grêle à pleine intensité.</summary>
    [Export] public float HailVolumeDb { get; set; } = -2f;
    /// <summary>Volume (dB) du vent à pleine intensité.</summary>
    [Export] public float WindVolumeDb { get; set; } = -6f;
    /// <summary>Volume (dB) des grincements.</summary>
    [Export] public float SqueakVolumeDb { get; set; } = -6f;
    /// <summary>Lissage exponentiel de tous les volumes (1/s).</summary>
    [Export] public float Sharpness { get; set; } = 2f;
    [Export] public string Bus { get; set; } = "Master";

    [ExportGroup("Vent")]
    /// <summary>Vitesse ressentie (m/s) où le vent léger est maximal.</summary>
    [Export] public float WindLightSpeed { get; set; } = 8f;
    /// <summary>Vitesse ressentie (m/s) où le vent moyen est maximal.</summary>
    [Export] public float WindMediumSpeed { get; set; } = 35f;
    /// <summary>Vitesse ressentie (m/s) à partir de laquelle le vent fort est maximal.</summary>
    [Export] public float WindHeavySpeed { get; set; } = 70f;
    /// <summary>Vitesse (m/s) ajoutée au ressenti quand le vent d'orage est au maximum.</summary>
    [Export] public float StormWindBoost { get; set; } = 40f;

    [ExportGroup("Grincements")]
    /// <summary>Coups de grincement par seconde à stress 1.</summary>
    [Export] public float SqueakRate { get; set; } = 0.6f;
    /// <summary>Délai minimal (s) entre deux grincements.</summary>
    [Export] public float SqueakMinInterval { get; set; } = 1.2f;
    /// <summary>Au-delà de ce stress, le grincement moyen remplace le léger une fois sur deux.</summary>
    [Export] public float SqueakMediumStress { get; set; } = 0.55f;
    /// <summary>Part de la rotation du véhicule dans le stress.</summary>
    [Export] public float SqueakSpinWeight { get; set; } = 0.3f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    private const string RainLightPath = "res://Resources/Sounds/Rain and hail/rain-light.mp3";
    private const string RainHeavyPath = "res://Resources/Sounds/Rain and hail/rain-heavy.mp3";
    private const string HailPath = "res://Resources/Sounds/Rain and hail/hail.mp3";
    private const string WindLightPath = "res://Resources/Sounds/Wind/wind-light.mp3";
    private const string WindMediumPath = "res://Resources/Sounds/Wind/wind-medium.mp3";
    private const string WindHeavyPath = "res://Resources/Sounds/Wind/wind-heavy.mp3";
    private const string SqueakLightPath = "res://Resources/Sounds/Squeak/low-boat-squeak.mp3";
    private const string SqueakMediumPath = "res://Resources/Sounds/Squeak/medium-boat-squeak.mp3";
    private const float SilentDb = -80f;
    private const float MinGain = 0.0001f;

    private AudioStreamPlayer _rainLight;
    private AudioStreamPlayer _rainHeavy;
    private AudioStreamPlayer _hail;
    private AudioStreamPlayer _windLight;
    private AudioStreamPlayer _windMedium;
    private AudioStreamPlayer _windHeavy;
    private AudioStreamMP3 _squeakLight;
    private AudioStreamMP3 _squeakMedium;
    private float _sinceSqueak;

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);

        _rainLight = AddLoop(RainLightPath);
        _rainHeavy = AddLoop(RainHeavyPath);
        _hail = AddLoop(HailPath);
        _windLight = AddLoop(WindLightPath);
        _windMedium = AddLoop(WindMediumPath);
        _windHeavy = AddLoop(WindHeavyPath);
        _squeakLight = GD.Load<AudioStreamMP3>(SqueakLightPath);
        _squeakMedium = GD.Load<AudioStreamMP3>(SqueakMediumPath);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float smooth = 1f - Mathf.Exp(-Sharpness * dt);
        bool valid = IsInstanceValid(Target);
        StormWeather storm = StormWeather.Instance;

        float rainLight = 0f, rainHeavy = 0f, hailGain = 0f;
        float windLight = 0f, windMedium = 0f, windHeavy = 0f;
        float stress = 0f;

        if (valid)
        {
            Vector3 p = Target.GlobalPosition;

            float r = storm != null ? Mathf.Clamp(storm.RainAt(p), 0f, 1f) : 0f;
            rainLight = Mathf.Sqrt(1f - r) * r;
            rainHeavy = Mathf.Sqrt(r) * r;

            hailGain = storm != null ? storm.HailAt(p) : 0f;

            float stormWind = storm != null ? storm.StormWind01(p) : 0f;
            float feltSpeed = Target.AirVelocity.Length() + stormWind * StormWindBoost;
            ComputeWindGains(feltSpeed, out windLight, out windMedium, out windHeavy);

            float turbulence = storm != null ? storm.Turbulence01(p) : 0f;
            stress = Mathf.Clamp(
                Mathf.Max(stormWind, turbulence) + SqueakSpinWeight * Target.AngularVelocity.Length() / 2f,
                0f, 1f);
        }

        Smooth(_rainLight, ToDb(rainLight, RainVolumeDb), smooth);
        Smooth(_rainHeavy, ToDb(rainHeavy, RainVolumeDb), smooth);
        Smooth(_hail, ToDb(hailGain, HailVolumeDb), smooth);
        Smooth(_windLight, ToDb(windLight, WindVolumeDb), smooth);
        Smooth(_windMedium, ToDb(windMedium, WindVolumeDb), smooth);
        Smooth(_windHeavy, ToDb(windHeavy, WindVolumeDb), smooth);

        _sinceSqueak += dt;
        if (stress > 0f && _sinceSqueak >= SqueakMinInterval && GD.Randf() < stress * SqueakRate * dt)
        {
            _sinceSqueak = 0f;
            PlaySqueak(stress);
        }
    }

    /// <summary>
    /// Gains léger, moyen et fort pour une vitesse ressentie. Sous WindLightSpeed, seul le léger
    /// monte linéairement ; entre deux niveaux, ils se partagent le gain à puissance constante.
    /// </summary>
    private void ComputeWindGains(float speed, out float light, out float medium, out float heavy)
    {
        float lightSpeed = Mathf.Max(WindLightSpeed, 0.01f);
        float mediumSpeed = Mathf.Max(WindMediumSpeed, lightSpeed + 0.01f);
        float heavySpeed = Mathf.Max(WindHeavySpeed, mediumSpeed + 0.01f);

        if (speed < lightSpeed)
        {
            light = speed / lightSpeed;
            medium = 0f;
            heavy = 0f;
        }
        else if (speed < mediumSpeed)
        {
            float t = (speed - lightSpeed) / (mediumSpeed - lightSpeed);
            light = Mathf.Sqrt(1f - t);
            medium = Mathf.Sqrt(t);
            heavy = 0f;
        }
        else if (speed < heavySpeed)
        {
            float t = (speed - mediumSpeed) / (heavySpeed - mediumSpeed);
            light = 0f;
            medium = Mathf.Sqrt(1f - t);
            heavy = Mathf.Sqrt(t);
        }
        else
        {
            light = 0f;
            medium = 0f;
            heavy = 1f;
        }
    }

    private void PlaySqueak(float stress)
    {
        bool medium = stress > SqueakMediumStress && GD.Randf() < 0.5f;
        AudioStreamMP3 source = medium ? _squeakMedium : _squeakLight;
        if (source == null)
            return;

        var stream = (AudioStreamMP3)source.Duplicate();
        stream.Loop = false;
        var player = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = SqueakVolumeDb + Mathf.LinearToDb(0.4f + 0.6f * stress),
            PitchScale = 0.9f + 0.2f * GD.Randf(),
        };
        player.Bus = Bus;
        player.Finished += player.QueueFree;
        AddChild(player);
        player.Play();
    }

    private AudioStreamPlayer AddLoop(string path)
    {
        var stream = GD.Load<AudioStreamMP3>(path);
        if (stream == null)
        {
            GD.PushError($"WeatherAmbience : son introuvable {path}");
            return null;
        }
        stream.Loop = true;
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = SilentDb };
        player.Bus = Bus;
        AddChild(player);
        player.Play();
        return player;
    }

    private static float ToDb(float gain, float layerDb) =>
        Mathf.LinearToDb(Mathf.Max(gain, MinGain)) + layerDb;

    private static void Smooth(AudioStreamPlayer player, float targetDb, float smooth)
    {
        if (player == null)
            return;
        player.VolumeDb = Mathf.Lerp(player.VolumeDb, targetDb, smooth);
    }
}
