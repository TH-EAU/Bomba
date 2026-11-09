using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Bomba.Core;
using Godot;

namespace Bomba.Clouds;

/// <summary>Échantillon du champ de nuages en un point.</summary>
public struct CloudSample
{
	/// <summary>Distance signée (m) à la surface du nuage. Négative à l'intérieur.</summary>
	public float Sdf;
	/// <summary>Densité 0..1.</summary>
	public float Density;
	/// <summary>Intensité orageuse 0..1.</summary>
	public float Storm;
	/// <summary>Ascendance 0..1 (multipliée par <see cref="CloudField.UpdraftSpeed"/>).</summary>
	public float Updraft;

	/// <summary>Union de deux échantillons (deux grilles superposées).</summary>
	public static CloudSample Union(CloudSample a, CloudSample b) => new()
	{
		Sdf = Mathf.Min(a.Sdf, b.Sdf),
		Density = Mathf.Max(a.Density, b.Density),
		Storm = Mathf.Max(a.Storm, b.Storm),
		Updraft = Mathf.Max(a.Updraft, b.Updraft),
	};
}

/// <summary>
/// Source de vérité unique des nuages : calculé une fois sur CPU, il alimente à la fois le rendu
/// (textures 3D RGBA16F : SDF, densité, orage, ascendance) et le gameplay (échantillonnage CPU).
/// Ce que le joueur voit est donc exactement ce qui le cache.
///
/// Deux grilles superposées, qui se répètent horizontalement et dérivent avec le vent :
/// - <b>locale</b> (période <see cref="TileSize"/>, fine) : mer de nuages, petits cumulus, rares congestus ;
/// - <b>géante</b> (période <see cref="GiantTileSize"/>, grossière, jusqu'à <see cref="GiantMaxAltitude"/>) :
///   un cumulonimbus géant en sablier par période, ses cumulus satellites et la nappe soulevée à son pied.
/// Le champ est l'union des deux (SDF min, densité max).
/// Méthode : SDF précalculé (cf. uhawk, « Rendering volumetric clouds using signed distance fields »).
/// </summary>
public partial class CloudField : Node, IWindSource
{
	public static CloudField Instance { get; private set; }

	public const int SizeX = 192, SizeY = 96, SizeZ = 192;
	public const int GiantSizeX = 256, GiantSizeY = 80, GiantSizeZ = 256;

	/// <summary>Émis quand le calcul est terminé (les données et les textures sont prêtes).</summary>
	[Signal] public delegate void BakedEventHandler();

	[Export] public int Seed { get; set; } = 7;

	[ExportGroup("Volume local")]
	/// <summary>Période horizontale de répétition de la grille locale (m).</summary>
	[Export] public float TileSize { get; set; } = 12288f;
	[Export] public float MinAltitude { get; set; } = 250f;
	[Export] public float MaxAltitude { get; set; } = 4600f;
	/// <summary>
	/// Maillage vertical non uniforme : altitude = min + hauteur × v^k (v ∈ [0,1]).
	/// k &gt; 1 donne des cellules fines en bas (couche basse détaillée) et larges en altitude.
	/// </summary>
	[Export] public float VerticalExponent { get; set; } = 1.8f;

	[ExportGroup("Volume géant")]
	/// <summary>Période de la grille géante = TileSize × ce facteur (≈ 49 km : un cumulonimbus par période).</summary>
	[Export] public int GiantTileFactor { get; set; } = 4;
	[Export] public float GiantMaxAltitude { get; set; } = 14000f;
	[Export] public float GiantVerticalExponent { get; set; } = 2.2f;

	[ExportGroup("Couche")]
	[Export] public float LayerBottom { get; set; } = 320f;
	[Export] public float LayerTop { get; set; } = 470f;
	/// <summary>Seuil de couverture : plus il est haut, plus la couche est trouée.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float LayerCoverage { get; set; } = 0.42f;
	/// <summary>Bosses du dessus de la nappe (m) : relief vu d'au-dessus.</summary>
	[Export] public float LayerBumpHeight { get; set; } = 150f;
	/// <summary>Soulèvement de la nappe au pied des congestus (m).</summary>
	[Export] public float SkirtHeight { get; set; } = 130f;
	/// <summary>Soulèvement de la nappe au pied d'un cumulonimbus (m).</summary>
	[Export] public float GiantSkirtHeight { get; set; } = 700f;

	[ExportGroup("Petits cumulus")]
	[Export] public int CumulusCount { get; set; } = 30;
	[Export] public float CumulusRadiusMin { get; set; } = 140f;
	[Export] public float CumulusRadiusMax { get; set; } = 320f;
	[Export] public float CumulusTopMin { get; set; } = 550f;
	[Export] public float CumulusTopMax { get; set; } = 850f;

	[ExportGroup("Cumulus congestus")]
	/// <summary>Congestus par période locale (12 km) : rares.</summary>
	[Export] public int CongestusCount { get; set; } = 2;
	[Export] public float CongestusRadiusMin { get; set; } = 300f;
	[Export] public float CongestusRadiusMax { get; set; } = 500f;
	[Export] public float CongestusTopMin { get; set; } = 1100f;
	[Export] public float CongestusTopMax { get; set; } = 1900f;
	[Export] public int CongestusSatellites { get; set; } = 3;

	[ExportGroup("Cumulonimbus (orages)")]
	/// <summary>
	/// Active le cumulonimbus géant (seule source des orages et des éclairs). Désactivé : la grille géante
	/// n'est ni calculée ni rendue (chargement plus court d'environ 2 s). Lu au calcul des nuages, au lancement.
	/// </summary>
	[Export] public bool GiantEnabled { get; set; } = true;
	/// <summary>
	/// Position du centre du cumulonimbus (X, Z, en mètres, monde ; le joueur apparaît vers l'origine).
	/// Il n'y en a qu'un dans tout le monde. Sa base a un rayon de <see cref="CumulonimbusRadiusMin"/> à
	/// <see cref="CumulonimbusRadiusMax"/> : à moins de ~11 km de l'origine, le joueur apparaît dans l'orage.
	/// </summary>
	[Export] public Vector2 GiantPosition { get; set; } = new(-8700f, -11600f);
	/// <summary>Intensité du rideau de pluie sous le cumulonimbus (0 : aucun).</summary>
	[Export] public float RainCurtain { get; set; } = 1f;
	/// <summary>Rayon du rideau de pluie (× rayon du cumulonimbus).</summary>
	[Export] public float RainCurtainRadius { get; set; } = 0.6f;
	/// <summary>Rayon de l'œil (× rayon du cumulonimbus) : colonne vide et calme au centre, du pied au sommet.</summary>
	[Export] public float EyeRadiusRatio { get; set; } = 0.15f;
	/// <summary>Douceur du bord de l'œil (part du rayon de l'œil).</summary>
	[Export(PropertyHint.Range, "0.05,0.8,0.01")] public float EyeSoftness { get; set; } = 0.25f;

	[ExportGroup("Ombre des nuages")]
	/// <summary>Distance (m) explorée vers le soleil pour l'ombre portée sur l'engin (soleil rasant).</summary>
	[Export] public float ShadowMaxDistance { get; set; } = 6000f;
	/// <summary>Extinction (1/m à densité 1) du soleil à travers les nuages, pour l'engin.</summary>
	[Export] public float ShadowExtinction { get; set; } = 0.004f;

	[ExportGroup("Mouvement du bruit")]
	/// <summary>Vitesse de dérive du bruit (périodes de forme par seconde) : les nuages bouillonnent.</summary>
	[Export] public float NoiseDriftSpeed { get; set; } = 0.012f;
	/// <summary>Montée relative du bruit (convection) : le motif monte dans les nuages.</summary>
	[Export] public float NoiseRise { get; set; } = 1.5f;
	[Export] public float CumulonimbusRadiusMin { get; set; } = 8000f;
	[Export] public float CumulonimbusRadiusMax { get; set; } = 11000f;
	[Export] public float CumulonimbusTopMin { get; set; } = 10000f;
	[Export] public float CumulonimbusTopMax { get; set; } = 13000f;
	/// <summary>Élargissement de l'enclume au sommet (× rayon).</summary>
	[Export] public float AnvilSpread { get; set; } = 0.8f;
	/// <summary>Rayon du fût à la taille du sablier (× rayon).</summary>
	[Export(PropertyHint.Range, "0.2,1,0.01")] public float WaistRatio { get; set; } = 0.5f;
	/// <summary>Cumulus bourgeonnants satellites au pied de chaque cumulonimbus.</summary>
	[Export] public int CumulonimbusSatellites { get; set; } = 14;

	/// <summary>Rayon (m) autour de l'origine sans colonne locale au démarrage (zone d'apparition).</summary>
	[Export] public float SpawnClearRadius { get; set; } = 1400f;

	[ExportGroup("LOD du bruit")]
	/// <summary>Distance (m) au-delà de laquelle le détail fin n'est plus calculé (estompé à partir du quart).</summary>
	[Export] public float LodFineDistance { get; set; } = 6000f;
	/// <summary>Distance (m) au-delà de laquelle les bourgeons ne sont plus calculés (formes grossières seules).</summary>
	[Export] public float LodShapeDistance { get; set; } = 20000f;

	[ExportGroup("Détail")]
	[Export(PropertyHint.Range, "0,1,0.01")] public float Erosion { get; set; } = 0.5f;

	[ExportGroup("Rendu et gameplay")]
	/// <summary>Coefficient d'extinction (1/m) à densité 1. 0,12 ≈ 8 m de visibilité au cœur.</summary>
	[Export] public float Extinction { get; set; } = 0.12f;
	/// <summary>Densité au-delà de laquelle on est « dans » le nuage (surface du SDF).</summary>
	[Export] public float DensityThreshold { get; set; } = 0.04f;
	/// <summary>Vitesse de dérive horizontale des nuages (m/s, X et Z).</summary>
	[Export] public Vector2 DriftVelocity { get; set; } = new(2.5f, 1f);
	/// <summary>Vitesse verticale (m/s) d'une ascendance à intensité 1.</summary>
	[Export] public float UpdraftSpeed { get; set; } = 12f;

	[ExportGroup("Apparence")]
	/// <summary>Lumière ambiante reçue par le haut des nuages (ciel).</summary>
	[Export] public Color AmbientTop { get; set; } = new(0.24f, 0.26f, 0.52f);
	/// <summary>Lumière ambiante reçue par le bas des nuages (mer sombre).</summary>
	[Export] public Color AmbientBottom { get; set; } = new(0.035f, 0.035f, 0.1f);
	[Export] public Color FogColor { get; set; } = new(0.78f, 0.66f, 0.88f);
	[Export] public float FogDensity { get; set; } = 0.000015f;
	/// <summary>Multiplicateur de l'intensité du soleil (lumière directionnelle de la scène).</summary>
	[Export] public float SunIntensityScale { get; set; } = 1.8f;
	/// <summary>Distance (m) en deçà de laquelle les nuages s'effacent devant la caméra (on voit son engin).</summary>
	[Export] public float NearFadeDistance { get; set; } = 18f;
	[Export] public float DetailScale { get; set; } = 900f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float DetailStrength { get; set; } = 1.0f;

	[ExportGroup("Qualité")]
	[Export] public int MaxSteps { get; set; } = 128;
	[Export] public float StepInside { get; set; } = 6f;
	/// <summary>Distance (m) à laquelle le pas intérieur a doublé (les nuages lointains sont échantillonnés plus grossièrement).</summary>
	[Export] public float StepGrowthDistance { get; set; } = 700f;
	[Export] public float MaxDistance { get; set; } = 60000f;
	/// <summary>0 = normal ; voir CLAUDE.md pour les modes de debug.</summary>
	[Export] public int DebugMode { get; set; }
	/// <summary>Accumulation temporelle (moins de grain, détail stable).</summary>
	[Export] public bool Temporal { get; set; } = true;
	/// <summary>Poids de l'image courante dans l'accumulation (plus petit = plus lisse, plus de traînées).</summary>
	[Export(PropertyHint.Range, "0.02,1,0.01")] public float TemporalBlend { get; set; } = 0.12f;

	/// <summary>Effet de rendu attaché à la caméra active.</summary>
	public CloudRenderer Renderer { get; private set; }

	/// <summary>Décalage horizontal courant (m) dû à la dérive (partagé par les deux grilles et le shader).</summary>
	public Vector2 Offset { get; private set; }

	public bool IsBaked { get; private set; }
	public float GiantTileSize => TileSize * GiantTileFactor;

	/// <summary>Grille locale (nappe, cumulus, congestus).</summary>
	public CloudGrid Local { get; private set; }
	/// <summary>Grille géante (cumulonimbus).</summary>
	public CloudGrid Giant { get; private set; }

	public float CellXZ => TileSize / SizeX;
	/// <summary>Hauteur maximale d'une cellule locale.</summary>
	public float CellY => (MaxAltitude - MinAltitude) / SizeY * VerticalExponent;

	/// <summary>Position et date (s) du dernier éclair.</summary>
	public Vector3 LightningPosition { get; private set; }
	public double LightningTime { get; private set; } = -1000.0;

	/// <summary>Intensité courante du flash d'éclair (0..1), avec scintillement.</summary>
	public float LightningFlash
	{
		get
		{
			double age = Time.GetTicksMsec() / 1000.0 - LightningTime;
			if (age < 0 || age > 0.6) return 0f;
			float decay = Mathf.Exp(-(float)age * 7f);
			float flicker = 0.6f + 0.4f * Mathf.Sin((float)age * 90f);
			return Mathf.Clamp(decay * flicker, 0f, 1f);
		}
	}

	private enum TowerKind { Cumulus, Congestus, Cumulonimbus }

	private struct Tower
	{
		public float X, Z, Radius, Top;
		public TowerKind Kind;
		/// <summary>Lobes d'une tour non orageuse : x, y = décalage horizontal (m), z = altitude du centre (m), w = rayon (m).</summary>
		public Vector4[] Lobes;
		/// <summary>Rayon horizontal d'influence des lobes (m).</summary>
		public float Reach;
		/// <summary>Penchant du cumulus (décalage horizontal par mètre de hauteur).</summary>
		public Vector2 Lean;
		public bool Storm => Kind == TowerKind.Cumulonimbus;
		/// <summary>Grande colonne qui soulève la nappe à son pied.</summary>
		public bool Big => Kind != TowerKind.Cumulus;
	}

	private readonly List<Tower> _localTowers = new();
	private readonly List<Tower> _giantTowers = new();
	private Camera3D _attachedCamera;
	private DirectionalLight3D _sun;
	private ImageTexture3D _localTexture, _giantTexture;
	private bool _printFps;
	private float _fpsTimer;

	/// <summary>Colonnes (position monde à la dérive courante, rayon, sommet, orageuse). Pour le debug.</summary>
	public IEnumerable<(Vector3 Position, float Radius, float Top, bool Storm)> Towers
	{
		get
		{
			// Grille géante sans répétition : position monde = origine du domaine + position + dérive.
			foreach (Tower t in _giantTowers)
				yield return (new Vector3(Giant.Origin.X + t.X + Offset.X, LayerBottom, Giant.Origin.Y + t.Z + Offset.Y),
					t.Radius, t.Top, t.Storm);
			foreach (Tower t in _localTowers)
				yield return Nearest(t, TileSize);
		}
	}

	private (Vector3, float, float, bool) Nearest(Tower t, float tile)
	{
		float x = t.X + Offset.X, z = t.Z + Offset.Y;
		// Copie de la tour la plus proche de l'origine (le motif se répète).
		x -= Mathf.Round(x / tile) * tile;
		z -= Mathf.Round(z / tile) * tile;
		return (new Vector3(x, LayerBottom, z), t.Radius, t.Top, t.Storm);
	}

	public override void _EnterTree() => Instance = this;

	public override void _ExitTree()
	{
		Wind.Instance?.Unregister(this);
		if (Instance == this)
			Instance = null;
	}

	public override void _Ready()
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--cloud-debug=") && int.TryParse(arg.Substring(14), out int mode))
				DebugMode = mode;
			if (arg == "--fps")
				_printFps = true;
		}
		Bake();
		Wind.Instance?.Register(this);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Pas de modulo : la grille géante ne se répète pas, son orage dérive donc sans saut
		// (≈ 9 km par heure de jeu, précision flottante largement suffisante).
		Offset += DriftVelocity * (float)delta;
	}

	public override void _Process(double delta)
	{
		if (Renderer == null)
			return;
		AttachToCamera();
		UpdateRenderer();

		if (_printFps)
		{
			_fpsTimer += (float)delta;
			if (_fpsTimer >= 2f)
			{
				_fpsTimer = 0f;
				GD.Print($"[FPS] {Engine.GetFramesPerSecond()} (caméra y = {GetViewport().GetCamera3D()?.GlobalPosition.Y:F0} m)");
			}
		}
	}

	private void AttachToCamera()
	{
		Camera3D cam = GetViewport()?.GetCamera3D();
		if (cam == null || cam == _attachedCamera)
			return;
		Compositor comp = cam.Compositor ?? new Compositor();
		var effects = comp.CompositorEffects;
		if (!effects.Contains(Renderer))
		{
			effects.Add(Renderer);
			comp.CompositorEffects = effects;
		}
		cam.Compositor = comp;
		_attachedCamera = cam;
	}

	private void UpdateRenderer()
	{
		// Soleil du crépuscule (statique) : direction rasante, éclaire seulement au-dessus de l'ombre de la Terre.
		CloudRenderer r = Renderer;
		r.SunDirection = Twilight.SunDirection;
		r.SunColor = Twilight.SunColor;
		r.SunIntensity = SunIntensityScale;
		r.Shadow = new Vector4(Twilight.ShadowAltitude, Mathf.Tan(Twilight.Depression), Twilight.SunAzimuth.Normalized().X, Twilight.SunAzimuth.Normalized().Y);
		r.Offset = Offset;
		r.Extinction = Extinction;
		r.NearFade = NearFadeDistance;
		r.AmbientTop = AmbientTop;
		r.AmbientBottom = AmbientBottom;
		r.FogColor = FogColor;
		r.FogDensity = FogDensity;
		r.DetailScale = DetailScale;
		r.DetailStrength = DetailStrength;
		r.MaxSteps = MaxSteps;
		r.StepInside = StepInside;
		r.MaxDistance = MaxDistance;
		r.StepGrowthDistance = StepGrowthDistance;
		r.LodFineDistance = LodFineDistance;
		r.LodShapeDistance = LodShapeDistance;
		r.TemporalEnabled = Temporal;
		r.TemporalBlend = TemporalBlend;
		r.DebugMode = DebugMode;
		r.Time = Time.GetTicksMsec() / 1000f;
		float flash = LightningFlash;
		r.Lightning = new Vector4(LightningPosition.X, LightningPosition.Y, LightningPosition.Z, flash);
		r.Rain = RainCurtainParams();
		r.StormEye = new Vector4(GiantEnabled && _giantTowers.Count > 0 ? _giantTowers[0].Radius * EyeRadiusRatio : 0f, NoiseRise, 0f, 0f);
		r.DetailSpeed = NoiseDriftSpeed;
	}

	/// <summary>Masque de l'œil du cumulonimbus à une position du monde (1 sans géant).</summary>
	public float GiantEyeMaskAt(Vector3 p)
	{
		if (!GiantEnabled || _giantTowers.Count == 0 || !_giantTowers[0].Storm)
			return 1f;
		Tower t = _giantTowers[0];
		float dx = p.X - (Giant.Origin.X + t.X + Offset.X), dz = p.Z - (Giant.Origin.Y + t.Z + Offset.Y);
		return EyeMask(Mathf.Sqrt(dx * dx + dz * dz), t.Radius);
	}

	/// <summary>Masque de l'œil (0 au centre, 1 hors de l'œil), selon la distance horizontale au centre de l'orage.</summary>
	public float EyeMask(float distance, float stormRadius)
	{
		float r = EyeRadiusRatio * stormRadius;
		return r <= 0f ? 1f : SmoothStep(r * (1f - EyeSoftness), r * (1f + EyeSoftness), distance);
	}

	/// <summary>
	/// Part du soleil (0..1) qui atteint le point à travers les nuages : marche vers le soleil (rasant), pas
	/// croissants (le nuage proche compte le plus). Sert à l'ombre portée des nuages sur l'engin (TwilightSun).
	/// </summary>
	public float SunTransmittance(Vector3 pos)
	{
		if (!IsBaked)
			return 1f;
		Vector3 dir = Twilight.SunDirection;
		const int steps = 24;
		float depth = 0f, prev = 0f;
		for (int i = 1; i <= steps; i++)
		{
			float u = (float)i / steps;
			float dist = u * u * ShadowMaxDistance;
			depth += Sample(pos + dir * dist).Density * (dist - prev);
			prev = dist;
		}
		return Mathf.Exp(-depth * ShadowExtinction);
	}

	/// <summary>Paramètres du rideau de pluie pour le rendu : centre (X, Z), rayon, intensité.</summary>
	private Vector4 RainCurtainParams()
	{
		if (!GiantEnabled || _giantTowers.Count == 0 || !_giantTowers[0].Storm)
			return Vector4.Zero;
		Tower t = _giantTowers[0];
		return new Vector4(Giant.Origin.X + t.X + Offset.X, Giant.Origin.Y + t.Z + Offset.Y, t.Radius * RainCurtainRadius, RainCurtain);
	}

	// ------------------------------------------------------------------ API gameplay

	/// <summary>Échantillonne le champ (union des deux grilles, répétition horizontale, dérive incluse).</summary>
	public CloudSample Sample(Vector3 worldPos)
	{
		if (!IsBaked)
			return new CloudSample { Sdf = 1e6f };
		CloudSample local = Local.Sample(worldPos, Offset);
		// L'œil traverse aussi la nappe locale (qui se répète) : masquée ici, à la position réelle de l'œil.
		float eye = GiantEyeMaskAt(worldPos);
		local.Density *= eye;
		local.Storm *= eye;
		return CloudSample.Union(local, Giant.Sample(worldPos, Offset));
	}

	/// <summary>Densité 0..1 au point (0 hors nuage).</summary>
	public float GetDensity(Vector3 worldPos) => Sample(worldPos).Density;

	/// <summary>
	/// Transmittance (0..1) du nuage le long du segment : 1 = vue dégagée, ≈ 0 = complètement masqué.
	/// Utilise le SDF pour sauter le vide (même principe que le rendu).
	/// </summary>
	public float GetTransmittance(Vector3 from, Vector3 to, float step = 4f)
	{
		Vector3 d = to - from;
		float length = d.Length();
		if (length < 0.01f || !IsBaked)
			return 1f;
		d /= length;

		float t = 0f, tau = 0f;
		for (int i = 0; i < 1024 && t < length; i++)
		{
			CloudSample s = Sample(from + d * t);
			if (s.Sdf > step)
			{
				t += s.Sdf;
				continue;
			}
			float h = Mathf.Min(step, length - t);
			tau += s.Density * Extinction * h;
			if (tau > 8f)
				return 0f;
			t += h;
		}
		return Mathf.Exp(-tau);
	}

	/// <summary>Contribution au vent : ascendances sous et dans les colonnes.</summary>
	public Vector3 GetWindContribution(Vector3 globalPosition)
	{
		float up = Sample(globalPosition).Updraft;
		return up > 0.001f ? Vector3.Up * (up * UpdraftSpeed) : Vector3.Zero;
	}

	/// <summary>
	/// Renseigne les uniforms nuages d'un matériau (cloud_tex, cloud_volume, giant_tex, giant_volume,
	/// cloud_offset, cloud_extinction). À rappeler chaque frame : la dérive change.
	/// Voir Shaders/Effects/light_beam.gdshader.
	/// </summary>
	public void ApplyCloudUniforms(ShaderMaterial material)
	{
		if (!IsBaked || material == null)
			return;
		_localTexture ??= Local.CreateTexture3D();
		_giantTexture ??= Giant.CreateTexture3D();
		material.SetShaderParameter("cloud_tex", _localTexture);
		material.SetShaderParameter("cloud_volume", new Vector4(TileSize, MinAltitude, MaxAltitude, VerticalExponent));
		material.SetShaderParameter("giant_tex", _giantTexture);
		material.SetShaderParameter("giant_volume", new Vector4(GiantTileSize, MinAltitude, GiantMaxAltitude, GiantVerticalExponent));
		material.SetShaderParameter("giant_origin", Giant.Origin);
		material.SetShaderParameter("cloud_offset", Offset);
		material.SetShaderParameter("cloud_extinction", Extinction);
	}

	/// <summary>
	/// Émis à chaque éclair. <paramref name="position"/> = centre du flash (dans le nuage).
	/// Si <paramref name="hasTarget"/>, la foudre frappe <paramref name="strikeTarget"/> (ex. le joueur) ;
	/// sinon c'est un éclair décoratif (intra-nuage ou vers le bas).
	/// </summary>
	[Signal] public delegate void LightningTriggeredEventHandler(Vector3 position, Vector3 strikeTarget, bool hasTarget);

	/// <summary>Déclenche un éclair décoratif (flash visible dans le rendu des nuages).</summary>
	public void TriggerLightning(Vector3 worldPos) => TriggerLightning(worldPos, Vector3.Zero, false);

	/// <summary>Déclenche un éclair ; avec cible, la foudre frappe ce point.</summary>
	public void TriggerLightning(Vector3 worldPos, Vector3 strikeTarget, bool hasTarget)
	{
		LightningPosition = worldPos;
		LightningTime = Time.GetTicksMsec() / 1000.0;
		EmitSignal(SignalName.LightningTriggered, worldPos, strikeTarget, hasTarget);
	}

	/// <summary>
	/// Point d'éclair décoratif dans un cœur orageux proche de <paramref name="around"/> : le cumulonimbus et, si
	/// <paramref name="congestus"/>, les congestus locaux (copie la plus proche, dérive comprise). Viser les tours
	/// plutôt que chercher au hasard : un éclair à chaque tentative dès qu'un orage est à portée.
	/// </summary>
	public bool TryRandomLightningPoint(Vector3 around, float radius, RandomNumberGenerator rng, bool congestus, out Vector3 point)
	{
		point = Vector3.Zero;
		var candidates = new List<Vector3>();
		// Cumulonimbus (grille géante, un seul exemplaire) : cœur orageux.
		foreach (Tower t in _giantTowers)
		{
			if (!t.Storm)
				continue;
			float gx = Giant.Origin.X + t.X + Offset.X, gz = Giant.Origin.Y + t.Z + Offset.Y;
			if ((gx - around.X) * (gx - around.X) + (gz - around.Z) * (gz - around.Z) > radius * radius)
				continue;
			Vector2 j = Vector2.Right.Rotated(rng.Randf() * Mathf.Tau) * rng.RandfRange(EyeRadiusRatio * 1.3f * t.Radius, 0.3f * t.Radius);
			candidates.Add(new Vector3(gx + j.X, Mathf.Lerp(LayerBottom, t.Top, rng.RandfRange(0.15f, 0.7f)), gz + j.Y));
		}
		foreach (Tower t in _localTowers)
		{
			if (!congestus || t.Kind != TowerKind.Congestus)
				continue;
			float dx = t.X + Offset.X - around.X, dz = t.Z + Offset.Y - around.Z;
			dx -= Mathf.Round(dx / TileSize) * TileSize;
			dz -= Mathf.Round(dz / TileSize) * TileSize;
			if (dx * dx + dz * dz > radius * radius)
				continue;
			Vector2 jitter = Vector2.Right.Rotated(rng.Randf() * Mathf.Tau) * rng.RandfRange(0f, 0.3f * t.Radius);
			float y = Mathf.Lerp(LayerBottom, t.Top, rng.RandfRange(0.3f, 0.7f));
			candidates.Add(new Vector3(around.X + dx + jitter.X, y, around.Z + dz + jitter.Y));
		}
		if (candidates.Count == 0)
			return false;
		point = candidates[rng.RandiRange(0, candidates.Count - 1)];
		return true;
	}

	// ------------------------------------------------------------------ Calcul

	/// <summary>Calcule les deux grilles (densité, orage, ascendance, SDF, texture). ~4 s.</summary>
	public void Bake()
	{
		var sw = Stopwatch.StartNew();
		Local = new CloudGrid(SizeX, SizeY, SizeZ, TileSize, MinAltitude, MaxAltitude, VerticalExponent);
		Giant = GiantEnabled
			? new CloudGrid(GiantSizeX, GiantSizeY, GiantSizeZ, GiantTileSize, MinAltitude, GiantMaxAltitude, GiantVerticalExponent)
			: new CloudGrid(2, 2, 2, GiantTileSize, MinAltitude, GiantMaxAltitude, GiantVerticalExponent);
		_localTexture = _giantTexture = null;

		var rng = new RandomNumberGenerator { Seed = (ulong)Seed * 7919UL + 13UL };
		if (GiantEnabled)
			PlaceGiantTowers(rng);
		else
			DisableGiant();
		PlaceLocalTowers(rng);

		float[] coverage = BuildCoverage();
		Parallel.For(0, SizeZ, k => BakeLocalSlice(k, coverage));
		if (GiantEnabled)
			Parallel.For(0, GiantSizeZ, BakeGiantSlice);
		long tDensity = sw.ElapsedMilliseconds;

		Local.Finish(DensityThreshold);
		Giant.Finish(DensityThreshold);
		IsBaked = true;

		Renderer ??= new CloudRenderer();
		Renderer.CloudData = Local.TextureData;
		Renderer.CloudSize = new Vector3I(SizeX, SizeY, SizeZ);
		Renderer.TileSize = TileSize;
		Renderer.MinAltitude = MinAltitude;
		Renderer.MaxAltitude = MaxAltitude;
		Renderer.VerticalExponent = VerticalExponent;
		Renderer.GiantData = Giant.TextureData;
		Renderer.GiantSize = new Vector3I(Giant.SizeX, Giant.SizeY, Giant.SizeZ);
		Renderer.GiantTileSize = GiantTileSize;
		Renderer.GiantMaxAltitude = GiantMaxAltitude;
		Renderer.GiantVerticalExponent = GiantVerticalExponent;
		Renderer.GiantOrigin = Giant.Origin;

		GD.Print($"[CloudField] calcul {sw.ElapsedMilliseconds} ms (densité {tDensity} ms), " +
				 $"local : {_localTowers.FindAll(t => t.Kind == TowerKind.Cumulus).Count} cumulus, " +
				 $"{_localTowers.FindAll(t => t.Kind == TowerKind.Congestus).Count} congestus / {TileSize / 1000f:F1} km ; " +
				 (GiantEnabled
					 ? $"géant : 1 cumulonimbus (rayon {_giantTowers[0].Radius / 1000f:F1} km, sommet {_giantTowers[0].Top / 1000f:F1} km, centre à {(Giant.Origin + new Vector2(GiantTileSize, GiantTileSize) * 0.5f).Length() / 1000f:F1} km du spawn), {_giantTowers.Count - 1} satellites"
					 : "géant : désactivé"));
		EmitSignal(SignalName.Baked);
	}

	/// <summary>Grille géante neutralisée : domaine repoussé très loin, jamais échantillonnée.</summary>
	private void DisableGiant()
	{
		_giantTowers.Clear();
		Giant.Repeat = false;
		Giant.Origin = new Vector2(1e7f, 1e7f);
	}

	/// <summary>
	/// Un seul cumulonimbus, au centre du domaine géant (qui ne se répète pas). Le domaine est placé
	/// dans le monde pour que ce centre soit en <see cref="GiantPosition"/>.
	/// </summary>
	private void PlaceGiantTowers(RandomNumberGenerator rng)
	{
		_giantTowers.Clear();
		float tile = GiantTileSize;
		float half = tile * 0.5f;
		float r = rng.RandfRange(CumulonimbusRadiusMin, CumulonimbusRadiusMax);
		_giantTowers.Add(new Tower
		{
			X = half, Z = half, Radius = r, Top = rng.RandfRange(CumulonimbusTopMin, CumulonimbusTopMax),
			Kind = TowerKind.Cumulonimbus,
		});

		Giant.Repeat = false;
		Giant.Origin = GiantPosition - new Vector2(half, half);

		// Satellites : amas de cumulus bourgeonnants (de 1,5 à 4,5 km de haut) au pied du cumulonimbus.
		for (int s = 0; s < CumulonimbusSatellites; s++)
		{
			float angle = rng.RandfRange(0f, Mathf.Tau);
			float dist = r * rng.RandfRange(0.85f, 1.5f);
			var sat = new Tower
			{
				X = half + Mathf.Cos(angle) * dist,
				Z = half + Mathf.Sin(angle) * dist,
				Radius = r * rng.RandfRange(0.08f, 0.17f),
				Top = rng.RandfRange(1500f, 4500f),
				Kind = TowerKind.Cumulus,
			};
			BuildLobes(ref sat, rng);
			_giantTowers.Add(sat);
		}
	}

	private void PlaceLocalTowers(RandomNumberGenerator rng)
	{
		_localTowers.Clear();
		AddLocalTowers(rng, CongestusCount, CongestusRadiusMin, CongestusRadiusMax, CongestusTopMin, CongestusTopMax, TowerKind.Congestus);

		// Satellites autour des congestus.
		int bigCount = _localTowers.Count;
		for (int b = 0; b < bigCount; b++)
		{
			Tower big = _localTowers[b];
			for (int s = 0; s < CongestusSatellites; s++)
			{
				float angle = rng.RandfRange(0f, Mathf.Tau);
				float dist = big.Radius * rng.RandfRange(0.8f, 1.7f);
				var sat = new Tower
				{
					X = Mathf.PosMod(big.X + Mathf.Cos(angle) * dist, TileSize),
					Z = Mathf.PosMod(big.Z + Mathf.Sin(angle) * dist, TileSize),
					Radius = big.Radius * rng.RandfRange(0.3f, 0.5f),
					Top = rng.RandfRange(600f, 950f),
					Kind = TowerKind.Cumulus,
				};
				BuildLobes(ref sat, rng);
				_localTowers.Add(sat);
			}
		}

		AddLocalTowers(rng, CumulusCount, CumulusRadiusMin, CumulusRadiusMax, CumulusTopMin, CumulusTopMax, TowerKind.Cumulus);
	}

	private void AddLocalTowers(RandomNumberGenerator rng, int count, float rMin, float rMax, float topMin, float topMax, TowerKind kind)
	{
		int added = 0, guard = 0;
		while (added < count && guard++ < 4000)
		{
			float x = rng.RandfRange(0f, TileSize), z = rng.RandfRange(0f, TileSize);
			float r = rng.RandfRange(rMin, rMax);
			if (WrappedDistance(x, z, 0f, 0f, TileSize) < SpawnClearRadius + r)
				continue;
			bool tooClose = false;
			foreach (Tower o in _localTowers)
			{
				// Les petits cumulus peuvent se grouper ; les colonnes restent espacées.
				float factor = kind == TowerKind.Cumulus && o.Kind == TowerKind.Cumulus ? 0.6f : 0.9f;
				if (WrappedDistance(x, z, o.X, o.Z, TileSize) < (r + o.Radius) * factor) { tooClose = true; break; }
			}
			if (tooClose)
				continue;
			var tower = new Tower { X = x, Z = z, Radius = r, Top = rng.RandfRange(topMin, topMax), Kind = kind };
			if (!tower.Storm)
				BuildLobes(ref tower, rng);
			_localTowers.Add(tower);
			added++;
		}
	}

	private float[] BuildCoverage()
	{
		var cov = new float[SizeX * SizeZ];
		for (int k = 0; k < SizeZ; k++)
		for (int i = 0; i < SizeX; i++)
		{
			float u = (i + 0.5f) / SizeX, w = (k + 0.5f) / SizeZ;
			// Plaques de la mer de nuages : 1 à 3 km.
			cov[i + SizeX * k] = CloudNoise.PerlinFbm(u * 6f, 0.37f, w * 6f, 6, 0, 6, 5, (uint)Seed);
		}
		return cov;
	}

	/// <summary>Grille locale : nappe (bosselée, soulevée au pied des congestus), cumulus et congestus.</summary>
	private void BakeLocalSlice(int k, float[] coverage)
	{
		CloudGrid g = Local;
		float tile = TileSize;
		float z = (k + 0.5f) * g.CellXZ;
		int bumpPeriod = Mathf.RoundToInt(tile / 384f);
		var near = new List<int>(16);

		for (int i = 0; i < g.SizeX; i++)
		{
			float x = (i + 0.5f) * g.CellXZ;
			float c = coverage[i + SizeX * k];

			near.Clear();
			float skirt = 0f, skirtLift = 0f;
			for (int ti = 0; ti < _localTowers.Count; ti++)
			{
				Tower t = _localTowers[ti];
				float d = WrappedDistance(x, z, t.X, t.Z, tile);
				if (d >= Mathf.Max(t.Reach, t.Radius * (t.Big ? 2.6f : 1.3f) + 2f * g.CellXZ))
					continue;
				near.Add(ti);
				if (t.Big)
				{
					float sk = 1f - SmoothStep(t.Radius * 0.9f, t.Radius * 2.5f, d);
					skirt = Mathf.Max(skirt, sk);
					skirtLift = Mathf.Max(skirtLift, sk * SkirtHeight);
				}
			}

			// Nappe : couverture 2D, sommet bosselé (relief vu d'au-dessus), soulevée au pied des colonnes.
			float layerCov = Mathf.Max(SmoothStep(LayerCoverage - 0.08f, LayerCoverage + 0.12f, c), skirt);
			float bumps = SmoothStep(0.35f, 0.95f, CloudNoise.Worley(x / 384f, 0.37f, z / 384f, bumpPeriod, 0, bumpPeriod, (uint)Seed + 71u))
						  + 0.5f * SmoothStep(0.4f, 0.95f, CloudNoise.Worley(x / 192f, 0.61f, z / 192f, bumpPeriod * 2, 0, bumpPeriod * 2, (uint)Seed + 72u));
			float layerTop = LayerTop + (c - 0.5f) * 180f + bumps * LayerBumpHeight * 0.7f + skirtLift;

			for (int j = 0; j < g.SizeY; j++)
			{
				float y = g.CellCenterY(j);
				float layer = layerCov * SmoothStep(LayerBottom, LayerBottom + 30f, y)
							  * (1f - SmoothStep(layerTop - 70f, layerTop, y));
				WriteVoxel(g, i, j, k, x, y, z, layer, _localTowers, near, 1f);
			}
		}
	}

	/// <summary>Grille géante : cumulonimbus en sablier, satellites et nappe soulevée à leur pied.</summary>
	private void BakeGiantSlice(int k)
	{
		CloudGrid g = Giant;
		float tile = GiantTileSize;
		float z = (k + 0.5f) * g.CellXZ;
		int bumpPeriod = Mathf.RoundToInt(tile / 1536f);
		var near = new List<int>(16);

		for (int i = 0; i < g.SizeX; i++)
		{
			float x = (i + 0.5f) * g.CellXZ;
			near.Clear();
			float skirt = 0f, eyeHole = 1f;
			for (int ti = 0; ti < _giantTowers.Count; ti++)
			{
				Tower t = _giantTowers[ti];
				float d = WrappedDistance(x, z, t.X, t.Z, tile);
				float reach = Mathf.Max(t.Reach, t.Radius * (t.Storm ? Mathf.Max(1f + AnvilSpread, 1.9f) : 1.3f) + 2f * g.CellXZ);
				if (d >= reach)
					continue;
				near.Add(ti);
				if (t.Storm)
				{
					// Jupe limitée à ~1,25 R : au-delà, la mer de nuages reste normale (zone de départ comprise).
					skirt = Mathf.Max(skirt, 1f - SmoothStep(t.Radius * 0.85f, t.Radius * 1.25f, d));
					eyeHole = Mathf.Min(eyeHole, EyeMask(d, t.Radius));   // l'œil perce aussi la jupe
				}
			}
			if (near.Count == 0)
				continue;

			// Pied de l'orage : nappe continue, épaisse et bosselée, qui se soulève vers le fût.
			float bumps = SmoothStep(0.35f, 0.95f, CloudNoise.Worley(x / 1536f, 0.37f, z / 1536f, bumpPeriod, 0, bumpPeriod, (uint)Seed + 81u));
			float layerTop = LayerTop + skirt * (GiantSkirtHeight + bumps * 300f);

			for (int j = 0; j < g.SizeY; j++)
			{
				float y = g.CellCenterY(j);
				float layer = skirt * SmoothStep(LayerBottom, LayerBottom + 60f, y) * (1f - SmoothStep(layerTop - 120f, layerTop, y)) * eyeHole;
				WriteVoxel(g, i, j, k, x, y, z, layer, _giantTowers, near, 6f);
			}
		}
	}

	/// <summary>Densité, orage et ascendance d'un voxel : union de la nappe et des colonnes proches, puis érosion.</summary>
	/// <param name="noiseScale">Échelle des bruits de déformation et d'érosion (1 = grille locale).</param>
	private void WriteVoxel(CloudGrid g, int i, int j, int k, float x, float y, float z, float layer,
		List<Tower> towers, List<int> near, float noiseScale)
	{
		float tile = g.TileSize;
		float tower = 0f, storm = 0f, updraft = 0f;
		if (near.Count > 0)
		{
			float wl = 512f * noiseScale;
			int warpPeriod = Mathf.RoundToInt(tile / wl);
			float warp;
			if (noiseScale > 1f)
			{
				// Géant (domaine sans répétition) : trois octaves à rapports de fréquence irrationnels et décalées.
				// Deux octaves sur un même réseau (×2) dessinaient un quadrillage de bosses sur les grandes façades
				// et des anneaux réguliers dans l'œil.
				// Chaque octave est aussi tournée autour de la verticale (27°, 61°, 103°) : la grille carrée du Perlin,
				// alignée sur les axes, dessinait sinon des carrés étagés sur l'enclume (losanges vus d'en haut).
				// Les coordonnées sont décalées dans le positif (pas de repli de période).
				(float x1, float z1) = Rot(x, z, 0.4712f, 0.8910f);
				(float x2, float z2) = Rot(x, z, 0.8746f, 0.4848f);
				(float x3, float z3) = Rot(x, z, 0.9744f, -0.2250f);
				warp = CloudNoise.Perlin(x1 / wl + 20.31f, y / (430f * noiseScale) + 0.17f, z1 / wl + 20.73f, 64, 0, 64, (uint)Seed + 55u)
					 + 0.55f * CloudNoise.Perlin(x2 * 2.13f / wl + 45.1f, y / (190f * noiseScale) + 2.3f, z2 * 2.13f / wl + 49.7f, 128, 0, 128, (uint)Seed + 56u)
					 + 0.3f * CloudNoise.Perlin(x3 * 4.71f / wl + 91.9f, y / (97f * noiseScale) + 7.7f, z3 * 4.71f / wl + 93.3f, 256, 0, 256, (uint)Seed + 57u);
			}
			else
				warp = CloudNoise.Perlin(x / wl, y / (400f * noiseScale), z / wl, warpPeriod, 0, warpPeriod, (uint)Seed + 55u)
					 + 0.5f * CloudNoise.Perlin(x * 2f / wl, y / (200f * noiseScale), z * 2f / wl, warpPeriod * 2, 0, warpPeriod * 2, (uint)Seed + 56u);
			foreach (int ti in near)
			{
				Tower t = towers[ti];
				float d = WrappedDistance(x, z, t.X, t.Z, tile);
				Vector2 delta = WrappedDelta(x, z, t.X, t.Z, tile);
				tower = Mathf.Max(tower, TowerBody(t, d, delta, y, warp, ref storm, ref updraft));
			}
		}

		int idx = g.Index(i, j, k);
		g.Updraft[idx] = Mathf.Clamp(updraft, 0f, 1f);

		float shape = Mathf.Max(layer, tower);
		if (shape <= 0.001f)
			return;

		// Érosion Perlin-Worley (échelle du kilomètre en local ; le détail fin est fait au rendu).
		float el = 256f * noiseScale;
		int worleyPeriod = Mathf.RoundToInt(tile / el);
		int perlinPeriod = Mathf.RoundToInt(tile / (el * 2f));
		float worley, perlin;
		if (noiseScale > 1f)
		{
			// Géant : érosion tournée hors des axes (et inclinée). Sur l'enclume presque plate, la hauteur du dessus
			// suit ce bruit : aligné sur les axes, il dessinait des marches carrées (losanges vus d'en haut).
			(float ex, float ez) = Rot(x, z, 0.6157f, 0.7880f);   // 38°
			float ey = y + 0.3f * ez;
			worley = CloudNoise.WorleyFbm(ex / el + 40.3f, ey / el + 3.1f, ez / el + 40.7f, 128, 0, 128, 3, (uint)Seed + 11u);
			(float px, float pz) = Rot(x, z, -0.4226f, 0.9063f);  // -25°
			perlin = CloudNoise.PerlinFbm(px / (el * 2f) + 20.2f, (y - 0.25f * px) / (el * 2f) + 1.7f, pz / (el * 2f) + 20.9f, 64, 0, 64, 3, (uint)Seed + 23u);
		}
		else
		{
			worley = CloudNoise.WorleyFbm(x / el, y / el, z / el, worleyPeriod, 0, worleyPeriod, 3, (uint)Seed + 11u);
			perlin = CloudNoise.PerlinFbm(x / (el * 2f), y / (el * 2f), z / (el * 2f), perlinPeriod, 0, perlinPeriod, 3, (uint)Seed + 23u);
		}
		float n = 0.65f * worley + 0.35f * perlin;
		float lo = (1f - n) * Erosion;
		float density = Mathf.Clamp((shape - lo) / Mathf.Max(1f - lo, 0.05f), 0f, 1f);

		g.Density[idx] = density;
		g.Storm[idx] = density > 0f ? storm : 0f;
	}

	/// <summary>Densité de forme d'une colonne à la distance horizontale d et à l'altitude y.</summary>
	private float TowerBody(Tower t, float d, Vector2 delta, float y, float warp, ref float storm, ref float updraft)
	{
		float height = t.Top - LayerBottom;
		float h = (y - LayerBottom) / height;

		// Ascendance : sous la colonne et dans sa moitié basse (plus forte sous les orages).
		if (t.Big)
		{
			float up = (1f - SmoothStep(0.3f * t.Radius, 1.15f * t.Radius, d))
					   * (1f - SmoothStep(0.4f, 0.7f, h)) * (t.Storm ? 1f : 0.7f);
			updraft = Mathf.Max(updraft, up);
		}

		if (h < -0.05f || h > 1.15f)
			return 0f;

		float dw = d * (1f + 0.45f * warp);
		if (t.Storm)
		{
			// Sablier : base large (amas bourgeonnant), fût étranglé, enclume évasée au dessous concave.
			float waist = Mathf.Lerp(1f, WaistRatio, SmoothStep(0.1f, 0.4f, h));
			float flare = SmoothStep(0.6f, 0.9f, h);
			float radius = t.Radius * Mathf.Lerp(waist, 1f + AnvilSpread, flare * flare);
			float rr = Mathf.Clamp(dw / radius, 0f, 1f);
			// Sommet plat de l'enclume, avec un dôme qui dépasse au centre (sommet protubérant).
			float topLocal = 1f - 0.03f * rr * rr + 0.05f * (1f - SmoothStep(0f, 0.3f, rr));
			float cap = 1f - SmoothStep(topLocal - 0.04f, topLocal, h);

			// Œil : colonne vide et calme au centre, sur toute la hauteur.
			float eye = EyeMask(dw, t.Radius);
			float core = (1f - SmoothStep(0.25f * t.Radius, 0.8f * t.Radius, dw))
						 * SmoothStep(0.02f, 0.12f, h) * (1f - SmoothStep(0.8f, 0.95f, h));
			storm = Mathf.Max(storm, core * eye);
			return (1f - SmoothStep(0.5f * radius, 1.05f * radius, dw)) * cap * SmoothStep(-0.05f, 0.03f, h) * eye;
		}
		else
		{
			// Amas de lobes soudés : noyau continu (base → 0,75 H, suit le penchant) + lobes bourgeonnants,
			// unis en douceur (1 − Π(1 − b)) pour éviter les boules détachées (« bonhomme de neige »).
			float keep = 1f;
			float hc = Mathf.Clamp(h, 0f, 0.75f) / 0.75f;
			Vector2 axis = t.Lean * (Mathf.Clamp(h, 0f, 1f) * height);
			float coreR = t.Radius * Mathf.Lerp(0.55f, 0.3f, hc);
			float coreD = (delta - axis).Length() * (1f + 0.35f * warp);
			float core = (1f - SmoothStep(0.5f * coreR, coreR, coreD)) * (1f - SmoothStep(0.65f, 0.8f, h));
			keep *= 1f - core;
			foreach (Vector4 lobe in t.Lobes)
			{
				float dx = delta.X - lobe.X, dz = delta.Y - lobe.Y;
				float dy = (y - lobe.Z) / 0.9f;
				float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * (1f + 0.35f * warp);
				keep *= 1f - (1f - SmoothStep(0.5f * lobe.W, lobe.W, dist));
			}
			return (1f - keep) * SmoothStep(-0.05f, 0.04f, h);
		}
	}

	// ------------------------------------------------------------------ Utilitaires

	/// <summary>Rotation de (x, z) autour de la verticale (sinus, cosinus de l'angle).</summary>
	private static (float, float) Rot(float x, float z, float s, float c) => (c * x - s * z, s * x + c * z);

	private static float WrappedDistance(float x0, float z0, float x1, float z1, float tile)
	{
		float dx = Mathf.Abs(x0 - x1), dz = Mathf.Abs(z0 - z1);
		dx = Mathf.Min(dx, tile - dx);
		dz = Mathf.Min(dz, tile - dz);
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>Amas de lobes d'une tour non orageuse : lobe de base large, lobe sommital petit, lobes intermédiaires concentrés vers le bas.</summary>
	private void BuildLobes(ref Tower t, RandomNumberGenerator rng)
	{
		float height = t.Top - LayerBottom;
		int count = t.Kind == TowerKind.Cumulus ? rng.RandiRange(5, 8) : rng.RandiRange(8, 13);
		float leanAngle = rng.RandfRange(0f, Mathf.Tau);
		Vector2 lean = new Vector2(Mathf.Cos(leanAngle), Mathf.Sin(leanAngle)) * rng.RandfRange(0f, 0.25f);

		var lobes = new Vector4[count];
		float reach = 0f;
		for (int i = 0; i < count; i++)
		{
			Vector4 lobe;
			if (i == 0)
			{
				lobe = new Vector4(0f, 0f, LayerBottom + 0.08f * height, 0.85f * t.Radius);
			}
			else if (i == count - 1)
			{
				Vector2 top = lean * (0.9f * height);
				lobe = new Vector4(top.X, top.Y, LayerBottom + 0.9f * height, 0.3f * t.Radius);
			}
			else
			{
				// Hauteurs stratifiées : chaque lobe dans sa tranche de [0,1 ; 0,85] (pas de trou vertical).
				float slot = (i - 1 + rng.RandfRange(0f, 1f)) / Mathf.Max(count - 2, 1);
				float f = 0.1f + 0.75f * slot;
				float r = t.Radius * Mathf.Lerp(1.0f, 0.45f, f) * rng.RandfRange(0.8f, 1.2f);
				float angle = rng.RandfRange(0f, Mathf.Tau);
				float spread = t.Radius * (1f - f) * rng.RandfRange(0f, 0.5f);
				Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * spread + lean * (f * height);
				lobe = new Vector4(offset.X, offset.Y, LayerBottom + f * height, r);
			}
			lobes[i] = lobe;
			reach = Mathf.Max(reach, new Vector2(lobe.X, lobe.Y).Length() + lobe.W);
		}
		t.Lobes = lobes;
		t.Lean = lean;
		t.Reach = Mathf.Ceil(reach);
	}

	/// <summary>Écart (x0 − x1, z0 − z1) ramené dans [−tile/2, tile/2] sur chaque axe.</summary>
	private static Vector2 WrappedDelta(float x0, float z0, float x1, float z1, float tile)
	{
		float dx = x0 - x1, dz = z0 - z1;
		dx -= tile * Mathf.Round(dx / tile);
		dz -= tile * Mathf.Round(dz / tile);
		return new Vector2(dx, dz);
	}

	private static float SmoothStep(float a, float b, float x)
	{
		float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
		return t * t * (3f - 2f * t);
	}
}
