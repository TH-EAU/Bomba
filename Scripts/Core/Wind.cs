using System.Collections.Generic;
using Godot;

namespace Bomba.Core;

/// <summary>Source de vent locale (ex. zone de rafale). S'enregistre auprès de <see cref="Wind"/>.</summary>
public interface IWindSource
{
    /// <summary>Contribution (m/s, globale) de cette source au point donné. Vector3.Zero si hors zone.</summary>
    Vector3 GetWindContribution(Vector3 globalPosition);
}

/// <summary>
/// Autoload "Wind" : vent global + somme des sources locales enregistrées.
/// Utiliser <see cref="GetWindAt"/> (statique, sûr même si l'autoload est absent).
/// </summary>
public partial class Wind : Node
{
    public static Wind Instance { get; private set; }

    /// <summary>Vent de base (m/s, global) : force et direction moyennes.</summary>
    [Export] public Vector3 BaseWind { get; set; } = new(10f, 0f, 0f);

    /// <summary>Amplitude des rafales (part de la force, 0 = constant).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Variation { get; set; } = 0.35f;

    /// <summary>Fréquence des rafales (Hz, bruit de Perlin).</summary>
    [Export] public float VariationFrequency { get; set; } = 0.15f;

    /// <summary>Le vent tourne lentement autour de sa direction moyenne, de ± cet angle (degrés).</summary>
    [Export] public float DirectionWanderDeg { get; set; } = 30f;

    /// <summary>Fréquence de ce changement de direction (Hz).</summary>
    [Export] public float DirectionWanderFrequency { get; set; } = 0.01f;

    private readonly List<IWindSource> _sources = new();
    private float _time;
    private readonly FastNoiseLite _noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, Seed = 31 };

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _PhysicsProcess(double delta) => _time += (float)delta;

    public void Register(IWindSource source)
    {
        if (!_sources.Contains(source))
            _sources.Add(source);
    }

    public void Unregister(IWindSource source) => _sources.Remove(source);

    /// <summary>Vent global courant, sans les sources locales.</summary>
    public Vector3 CurrentBaseWind
    {
        get
        {
            float gust = 1f + Variation * _noise.GetNoise1D(_time * VariationFrequency);
            float turn = Mathf.DegToRad(DirectionWanderDeg) * _noise.GetNoise1D(1000f + _time * DirectionWanderFrequency);
            return BaseWind.Rotated(Vector3.Up, turn) * gust;
        }
    }

    /// <summary>Vent total (m/s, global) au point donné.</summary>
    public static Vector3 GetWindAt(Vector3 globalPosition)
    {
        Wind w = Instance;
        if (w == null)
            return Vector3.Zero;

        Vector3 total = w.CurrentBaseWind;
        foreach (IWindSource s in w._sources)
            total += s.GetWindContribution(globalPosition);
        return total;
    }
}
