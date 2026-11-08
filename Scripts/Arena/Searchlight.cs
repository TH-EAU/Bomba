using System.Collections.Generic;
using Bomba.Clouds;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Arena;

/// <summary>
/// Projecteur détecteur : une tour sur un pilier rocheux dont le faisceau balaie le ciel.
/// Il repère le véhicule du joueur s'il est dans le cône, sans obstacle et non masqué par les nuages.
/// Le faisceau passe alors au rouge, la tête suit le joueur et <see cref="Detection.ReportSpotted"/> est appelé.
/// Script outil : le décor est construit dans l'éditeur. Les nœuds générés (sous "_Generated", sans
/// propriétaire) ne sont jamais enregistrés dans la scène. La logique ne tourne que hors éditeur.
/// </summary>
[Tool]
public partial class Searchlight : Node3D, IVehicleTracker
{
	private const string GeneratedName = "_Generated";
	private const string BeamShaderPath = "res://Shaders/Effects/light_beam.gdshader";

	/// <summary>Hauteur (m) de la tour au-dessus de l'origine, et donc de la tête.</summary>
	private const float TowerHeight = 12f;
	/// <summary>Durée (s) du retour en douceur au balayage après avoir perdu le joueur.</summary>
	private const float ReturnTime = 1.5f;
	/// <summary>Le joueur est repéré dans un cône élargi de ce facteur (ouverture visée × 1,4).</summary>
	private const float DetectionConeFactor = 1.4f;

	private static readonly Color BeamIdle = new(0.55f, 0.75f, 1.0f);
	private static readonly Color BeamAlert = new(1.0f, 0.15f, 0.1f);
	private static readonly Color RockColor = new(0.12f, 0.13f, 0.18f);
	private static readonly Color SteelColor = new(0.1f, 0.1f, 0.12f);
	private static readonly Color HazardRed = new(1.0f, 0.1f, 0.08f);

	/// <summary>Portée (m) du faisceau et de la lumière.</summary>
	[ExportGroup("Faisceau")]
	[Export] public float Range { get; set; } = 650f;
	/// <summary>Demi-angle (°) du cône.</summary>
	[Export] public float BeamAngleDegrees { get; set; } = 9f;
	/// <summary>Élévation (°) du faisceau au repos.</summary>
	[Export] public float BeamPitchDegrees { get; set; } = 18f;

	[ExportGroup("Balayage")]
	/// <summary>Amplitude (°) du balayage de part et d'autre du cap initial.</summary>
	[Export] public float SweepYawDegrees { get; set; } = 70f;
	/// <summary>Durée (s) d'un aller-retour du balayage.</summary>
	[Export] public float SweepPeriod { get; set; } = 9f;

	[ExportGroup("Détection")]
	/// <summary>Vitesse de rotation (°/s) de la tête quand elle suit le joueur.</summary>
	[Export] public float TrackSpeedDegrees { get; set; } = 35f;
	/// <summary>Délai (s) sans voir le joueur avant de reprendre le balayage.</summary>
	[Export] public float LoseDelay { get; set; } = 2.5f;
	/// <summary>Transmittance minimale (0..1) pour voir le joueur à travers les nuages.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float VisibilityThreshold { get; set; } = 0.35f;

	[ExportGroup("Décor")]
	/// <summary>Hauteur (m) du pilier rocheux sous la tour.</summary>
	[Export] public float PillarHeight { get; set; } = 90f;

	/// <summary>Véhicule du joueur, assigné par GameRoot.</summary>
	public Vehicle Target { get; set; }

	private Node3D _generated;
	private Node3D _head;
	private SpotLight3D _light;
	private ShaderMaterial _beamMaterial;
	private StaticBody3D _pillar;
	private readonly List<StandardMaterial3D> _hazardMaterials = new();

	/// <summary>Direction de visée dans le repère du nœud Searchlight.</summary>
	private Vector3 _aimLocal = Vector3.Forward;
	private double _clock;
	private double _lastSeen = double.NegativeInfinity;
	private bool _wasAlert;
	private Vector3 _returnFrom;
	private float _returnElapsed = ReturnTime;

	public override void _Ready()
	{
		AddToGroup(IVehicleTracker.GroupName);
		Rebuild();
	}

	public override void _Process(double delta)
	{
		// Le faisceau est masqué par les nuages : il lit le champ de nuages (la dérive change chaque frame).
		if (_beamMaterial != null && Bomba.Clouds.CloudField.Instance != null)
			Bomba.Clouds.CloudField.Instance.ApplyCloudUniforms(_beamMaterial);

		// Feux d'obstacle : clignotement lent, déphasé d'un feu à l'autre.
		float t = Time.GetTicksMsec() / 1000f;
		for (int i = 0; i < _hazardMaterials.Count; i++)
		{
			float phase = 0.5f + 0.5f * Mathf.Sin(t * 2.1f + i * 1.7f);
			_hazardMaterials[i].EmissionEnergyMultiplier = 0.15f + 3f * phase * phase * phase * phase;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint())
			return;

		float dt = (float)delta;
		_clock += delta;

		bool hasTarget = IsInstanceValid(Target);
		if (hasTarget && CanSee(Target))
		{
			_lastSeen = _clock;
			Detection.ReportSpotted();
		}
		bool alert = hasTarget && _clock - _lastSeen < LoseDelay;

		if (alert)
		{
			_aimLocal = Track(Target, dt);
		}
		else
		{
			if (_wasAlert)
			{
				_returnFrom = _aimLocal;
				_returnElapsed = 0f;
			}
			_returnElapsed += dt;
			_aimLocal = SweepAim();
			if (_returnElapsed < ReturnTime)
			{
				Vector3 blend = _returnFrom.Lerp(_aimLocal, Mathf.SmoothStep(0f, 1f, _returnElapsed / ReturnTime));
				if (blend.LengthSquared() > 1e-6f)
					_aimLocal = blend.Normalized();
			}
		}

		_wasAlert = alert;
		ApplyAlertColor(alert);
		AimHead();
	}

	// ------------------------------------------------------------------ Logique

	/// <summary>Vrai si le joueur est dans le cône, sans obstacle et non masqué par les nuages.</summary>
	private bool CanSee(Vehicle target)
	{
		Vector3 lamp = _head.GlobalPosition;
		Vector3 targetPos = target.GlobalPosition;
		Vector3 toTarget = targetPos - lamp;
		float distance = toTarget.Length();
		if (distance >= Range || distance < 0.01f)
			return false;

		Vector3 aim = GlobalBasis * _aimLocal;
		if (aim.AngleTo(toTarget) >= Mathf.DegToRad(BeamAngleDegrees * DetectionConeFactor))
			return false;

		var query = PhysicsRayQueryParameters3D.Create(lamp, targetPos);
		query.Exclude = new Godot.Collections.Array<Rid> { target.GetRid(), _pillar.GetRid() };
		if (GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0)
			return false;

		CloudField clouds = CloudField.Instance;
		return clouds == null || clouds.GetTransmittance(lamp, targetPos) >= VisibilityThreshold;
	}

	/// <summary>Fait tourner la visée vers le véhicule, à au plus <see cref="TrackSpeedDegrees"/> par seconde.</summary>
	private Vector3 Track(Vehicle target, float dt)
	{
		Vector3 toTarget = ToLocal(target.GlobalPosition) - _head.Position;
		if (toTarget.LengthSquared() < 1e-6f)
			return _aimLocal;
		return Slew(_aimLocal, toTarget.Normalized(), Mathf.DegToRad(TrackSpeedDegrees) * dt);
	}

	private static Vector3 Slew(Vector3 current, Vector3 desired, float maxAngle)
	{
		float angle = current.AngleTo(desired);
		if (angle <= maxAngle)
			return desired;
		Vector3 axis = current.Cross(desired);
		if (axis.LengthSquared() < 1e-8f)
			axis = Vector3.Up;
		return current.Rotated(axis.Normalized(), maxAngle).Normalized();
	}

	/// <summary>Visée du balayage : cap initial (repère du nœud) + oscillation en lacet.</summary>
	private Vector3 SweepAim()
	{
		float phase = (float)(_clock % SweepPeriod) / SweepPeriod;
		float yaw = Mathf.DegToRad(SweepYawDegrees) * Mathf.Sin(Mathf.Tau * phase);
		return AimFromAngles(yaw, Mathf.DegToRad(BeamPitchDegrees));
	}

	/// <summary>Direction de visée (-Z de la tête) pour un lacet et un tangage donnés, en radians.</summary>
	private static Vector3 AimFromAngles(float yaw, float pitch) =>
		new(-Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch));

	private void AimHead()
	{
		Vector3 up = Mathf.Abs(_aimLocal.Y) > 0.99f ? Vector3.Back : Vector3.Up;
		_head.Basis = Basis.LookingAt(_aimLocal, up);
	}

	private void ApplyAlertColor(bool alert)
	{
		Color c = alert ? BeamAlert : BeamIdle;
		_light.LightColor = c;
		_beamMaterial.SetShaderParameter("beam_color", c);
	}

	// ------------------------------------------------------------------ Construction

	/// <summary>Détruit et reconstruit tout le décor généré.</summary>
	private void Rebuild()
	{
		Node old = GetNodeOrNull(GeneratedName);
		if (old != null)
		{
			RemoveChild(old);
			old.QueueFree();
		}
		_hazardMaterials.Clear();
		_generated = new Node3D { Name = GeneratedName };
		AddChild(_generated);

		BuildPillar();
		BuildTower();
		BuildHead();

		_aimLocal = SweepAim();
		AimHead();
	}

	/// <summary>Pilier rocheux évasé vers le bas, qui descend de <see cref="PillarHeight"/> sous l'origine.</summary>
	private void BuildPillar()
	{
		var mesh = new CylinderMesh { TopRadius = 6f, BottomRadius = 15f, Height = PillarHeight };
		_pillar = new StaticBody3D { Name = "Pilier", Position = new Vector3(0f, -PillarHeight / 2f, 0f) };
		_pillar.AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = RockColor, Roughness = 0.95f },
		});
		_pillar.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape() });
		_generated.AddChild(_pillar);
	}

	/// <summary>Tour en poutrelles (quatre montants et des entretoises), avec des feux rouges en haut des montants.</summary>
	private void BuildTower()
	{
		var steel = new StandardMaterial3D { AlbedoColor = SteelColor, Metallic = 0.6f, Roughness = 0.45f };

		foreach (float x in new[] { -1.2f, 1.2f })
		foreach (float z in new[] { -1.2f, 1.2f })
		{
			AddPart(new BoxMesh { Size = new Vector3(0.35f, TowerHeight, 0.35f) }, steel,
				new Vector3(x, TowerHeight / 2f, z));
			// Chaque feu a son propre matériau, pour clignoter indépendamment.
			var hazard = new StandardMaterial3D
			{
				AlbedoColor = HazardRed,
				EmissionEnabled = true,
				Emission = HazardRed,
			};
			_hazardMaterials.Add(hazard);
			AddPart(new SphereMesh { Radius = 0.25f, Height = 0.5f }, hazard,
				new Vector3(x, TowerHeight + 0.2f, z));
		}

		for (float y = 3f; y < TowerHeight; y += 3f)
		foreach (float s in new[] { -1.2f, 1.2f })
		{
			AddPart(new BoxMesh { Size = new Vector3(2.4f, 0.2f, 0.2f) }, steel, new Vector3(0f, y, s));
			AddPart(new BoxMesh { Size = new Vector3(0.2f, 0.2f, 2.4f) }, steel, new Vector3(s, y, 0f));
		}
	}

	/// <summary>Tête orientable : tambour, lampe et cône visible. Le -Z de la tête est la direction de visée.</summary>
	private void BuildHead()
	{
		_head = new Node3D { Name = "Head", Position = new Vector3(0f, TowerHeight + 1f, 0f) };
		_generated.AddChild(_head);

		var steel = new StandardMaterial3D { AlbedoColor = SteelColor, Metallic = 0.6f, Roughness = 0.45f };
		_head.AddChild(new MeshInstance3D
		{
			Name = "Tambour",
			Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 2f },
			MaterialOverride = steel,
			Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f),
		});

		_light = new SpotLight3D
		{
			Name = "Lumiere",
			SpotRange = Range,
			SpotAngle = BeamAngleDegrees,
			LightEnergy = 8f,
			ShadowEnabled = false,
			LightColor = BeamIdle,
		};
		_head.AddChild(_light);

		_beamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(BeamShaderPath) };
		_beamMaterial.SetShaderParameter("beam_color", BeamIdle);
		_beamMaterial.SetShaderParameter("length", Range);

		// Cône : sommet étroit posé sur la lampe, base large à Range. Le cylindre est orienté +Y -> +Z (arrière de la tête).
		float baseRadius = Range * Mathf.Tan(Mathf.DegToRad(BeamAngleDegrees));
		_head.AddChild(new MeshInstance3D
		{
			Name = "Faisceau",
			Mesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = baseRadius, Height = Range },
			MaterialOverride = _beamMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = new Vector3(0f, 0f, -Range / 2f),
			Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0f),
		});
	}

	private void AddPart(PrimitiveMesh mesh, Material material, Vector3 position)
	{
		_generated.AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = material,
			Position = position,
		});
	}
}
