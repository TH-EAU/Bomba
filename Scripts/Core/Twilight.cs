using Godot;

namespace Bomba.Core;

/// <summary>
/// Crépuscule statique : le soleil est déjà couché de <see cref="SunDepressionDeg"/> degrés.
/// L'ombre de la Terre est modélisée par un plan incliné : un point est au soleil s'il est au-dessus de
/// <see cref="ShadowHeightAt"/> (≈ <see cref="ShadowAltitude"/> à l'origine, plus bas vers le soleil).
/// Le ciel, les nuages et la lumière de la scène lisent tous ces mêmes valeurs.
/// Courbure fictive : rayon R tel que l'horizon s'abaisse de δ exactement à l'altitude d'ombre,
/// d'où l'élévation apparente du soleil <see cref="ApparentSunElevation"/> (0 au seuil, positive au-dessus).
/// </summary>
public static class Twilight
{
	/// <summary>Abaissement du soleil sous l'horizon (degrés).</summary>
	public static float SunDepressionDeg = 25.2f;

	/// <summary>Altitude (m) de l'ombre de la Terre au-dessus de l'origine.</summary>
	public static float ShadowAltitude = 1300f;

	/// <summary>Direction horizontale (X, Z) vers le soleil : devant le joueur au départ (-Z).</summary>
	public static Vector2 SunAzimuth = new(0f, -1f);

	/// <summary>Couleur du soleil rasant (golden hour rose).</summary>
	public static Color SunColor = new(1f, 0.62f, 0.5f);

	public static float Depression => Mathf.DegToRad(SunDepressionDeg);

	/// <summary>Rayon de courbure fictif (m) : l'horizon s'abaisse de δ à l'altitude d'ombre.</summary>
	public static float EarthRadius => 2f * ShadowAltitude / (Depression * Depression);

	/// <summary>Altitude de l'ombre au point (plus basse vers le soleil).</summary>
	public static float ShadowHeightAt(Vector3 p) =>
		ShadowAltitude - (p.X * SunAzimuth.X + p.Z * SunAzimuth.Y) * Mathf.Tan(Depression);

	/// <summary>Part d'ensoleillement 0..1 au point (transition douce de ±60 m).</summary>
	public static float SunLit(Vector3 p)
	{
		float t = Mathf.Clamp((p.Y - ShadowHeightAt(p) + 60f) / 120f, 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	/// <summary>Élévation apparente (rad) du soleil vu depuis l'altitude y (abaissement de l'horizon compris).</summary>
	public static float ApparentSunElevation(float y) =>
		Mathf.Sqrt(2f * Mathf.Max(y, 0f) / EarthRadius) - Depression;

	/// <summary>Direction vers le soleil pour l'éclairage : rasante (≈ 1°), azimut <see cref="SunAzimuth"/>.</summary>
	public static Vector3 SunDirection
	{
		get
		{
			Vector2 a = SunAzimuth.Normalized();
			float e = Mathf.DegToRad(1f);
			return new Vector3(a.X * Mathf.Cos(e), Mathf.Sin(e), a.Y * Mathf.Cos(e)).Normalized();
		}
	}
}
