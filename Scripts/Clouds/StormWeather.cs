using Bomba.Core;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Météo d'orage autour du cumulonimbus géant (<see cref="CloudField"/>) :
/// - pluie (<see cref="RainAt"/>) sous l'orage et dans son cœur, de la mer jusqu'au sommet ;
/// - grêle (<see cref="HailAt"/>) dans le cœur orageux, au-dessus de <see cref="HailAltitude"/> ;
/// - dépression (<see cref="GetWindContribution"/>, source de <see cref="Wind"/>) : vent convergent et tournant qui forcit
///   en s'approchant, avec des rafales. Il agit sur la physique des véhicules : on peut tenir sur place face au vent.
/// Sans géant (<see cref="CloudField.GiantEnabled"/> faux), tout est nul.
/// </summary>
public partial class StormWeather : Node, IWindSource
{
    public static StormWeather Instance { get; private set; }

    [ExportGroup("Pluie et grêle")]
    /// <summary>Rayon (× rayon de l'orage) en deçà duquel il pleut à plein ; nul au-delà du rayon.</summary>
    [Export] public float RainCoreRatio { get; set; } = 0.6f;
    /// <summary>Altitude (m) au-dessus de laquelle la pluie devient grêle dans le cœur orageux.</summary>
    [Export] public float HailAltitude { get; set; } = 3000f;
    /// <summary>Intensité d'orage (canal Storm) à partir de laquelle il grêle.</summary>
    [Export] public float HailStormThreshold { get; set; } = 0.3f;

    [ExportGroup("Dépression")]
    /// <summary>Vent maximal (m/s), atteint à <see cref="WindPeakRadii"/>.</summary>
    [Export] public float MaxStormWind { get; set; } = 35f;
    /// <summary>Distance (× rayon de l'orage) où le vent commence à se lever.</summary>
    [Export] public float WindReachRadii { get; set; } = 1.3f;
    /// <summary>Distance (× rayon de l'orage) où le vent est maximal.</summary>
    [Export] public float WindPeakRadii { get; set; } = 0.5f;
    /// <summary>Part convergente du vent (vers l'orage) ; le reste tourne autour.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float InflowShare { get; set; } = 0.5f;
    /// <summary>Force des rafales (× vent local).</summary>
    [Export] public float GustStrength { get; set; } = 0.6f;
    [Export] public float GustFrequency { get; set; } = 0.25f;
    /// <summary>Taille (m) des rafales.</summary>
    [Export] public float GustScale { get; set; } = 600f;

    /// <summary>Un orage existe (géant actif et calculé).</summary>
    public bool Active { get; private set; }
    /// <summary>Centre (X, Z), rayon de base, base et sommet (m) de l'orage.</summary>
    public Vector2 Center { get; private set; }
    public float Radius { get; private set; }
    public float Base { get; private set; }
    public float Top { get; private set; }

    private FastNoiseLite _noise;
    private float _time;
    private bool _registered;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (_registered)
            Wind.Instance?.Unregister(this);
        _registered = false;
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        _noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, Seed = 97 };
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;
        RefreshStorm();
        if (!_registered && Wind.Instance != null)
        {
            Wind.Instance.Register(this);
            _registered = true;
        }
    }

    private void RefreshStorm()
    {
        CloudField field = CloudField.Instance;
        Active = false;
        if (field == null || !field.IsBaked || !field.GiantEnabled)
            return;
        foreach (var t in field.Towers)
        {
            if (!t.Storm)
                continue;
            Center = new Vector2(t.Position.X, t.Position.Z);
            Radius = t.Radius;
            Base = field.LayerBottom;
            Top = t.Top;
            Active = true;
            return;
        }
    }

    private float HorizontalDistance(Vector3 p) => new Vector2(p.X - Center.X, p.Z - Center.Y).Length();

    /// <summary>Intensité de pluie (0..1) au point.</summary>
    public float RainAt(Vector3 p)
    {
        if (!Active)
            return 0f;
        float footprint = Mathf.SmoothStep(Radius, RainCoreRatio * Radius, HorizontalDistance(p));
        if (footprint <= 0f)
            return 0f;
        float below = 1f - Mathf.SmoothStep(Top * 0.9f, Top, p.Y);
        float storm = CloudField.Instance?.Sample(p).Storm ?? 0f;
        return Mathf.Clamp(footprint * below * (0.7f + 0.3f * storm), 0f, 1f);
    }

    /// <summary>Intensité de grêle (0..1) au point : cœur orageux, en altitude.</summary>
    public float HailAt(Vector3 p)
    {
        if (!Active || p.Y < HailAltitude - 500f)
            return 0f;
        float storm = CloudField.Instance?.Sample(p).Storm ?? 0f;
        return Mathf.SmoothStep(HailStormThreshold, HailStormThreshold + 0.3f, storm)
             * Mathf.SmoothStep(HailAltitude - 500f, HailAltitude + 500f, p.Y);
    }

    /// <summary>Part du vent d'orage (0..1) au point : 0 loin, 1 au maximum.</summary>
    public float StormWind01(Vector3 p) =>
        Active ? Mathf.SmoothStep(WindReachRadii * Radius, WindPeakRadii * Radius, HorizontalDistance(p)) : 0f;

    /// <summary>Agitation des rafales (0..1) au point : pluie agitée, grincements.</summary>
    public float Turbulence01(Vector3 p)
    {
        float s = StormWind01(p);
        return s <= 0f ? 0f : Mathf.Clamp(s * (0.5f + Mathf.Abs(Gust(p, 3))), 0f, 1f);
    }

    public Vector3 GetWindContribution(Vector3 p)
    {
        float s = StormWind01(p);
        if (s <= 0f)
            return Vector3.Zero;
        Vector2 rel = new(p.X - Center.X, p.Z - Center.Y);
        float d = rel.Length();
        if (d < 1f)
            return Vector3.Zero;

        Vector2 inward = -rel / d;
        Vector2 around = new(-inward.Y, inward.X);
        Vector2 dir = (inward * InflowShare + around * (1f - InflowShare)).Normalized();
        float speed = MaxStormWind * s;
        Vector3 wind = new Vector3(dir.X, 0f, dir.Y) * speed * (1f + 0.5f * GustStrength * Gust(p, 0));
        wind += new Vector3(Gust(p, 1), 0.4f * Gust(p, 2), Gust(p, 4)) * speed * GustStrength * 0.5f;
        return wind;
    }

    /// <summary>Bruit de rafale (≈ -1..1), canal c, variable dans l'espace et le temps.</summary>
    private float Gust(Vector3 p, int c) =>
        _noise.GetNoise3D(p.X / GustScale + c * 31.7f, p.Y / GustScale + _time * GustFrequency, p.Z / GustScale - c * 17.3f);
}
