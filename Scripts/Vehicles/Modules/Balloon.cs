using Bomba.Core;
using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>Mode du ballon : gaz (poussée constante) ou montgolfière (chaleur pilotable).</summary>
public enum BalloonMode
{
	/// <summary>Ballon à gaz : poussée constante à pleine puissance.</summary>
	Gas,
	/// <summary>Montgolfière : poussée proportionnelle à la chaleur, qui évolue avec inertie.</summary>
	HotAir,
}

/// <summary>
/// Ballon (gaz ou montgolfière). Applique une poussée d'Archimède vers le haut GLOBAL,
/// à sa position, et une traînée linéaire par rapport à l'air.
/// Fonctionne sans <see cref="Vehicle"/> (ex. mine flottante).
/// </summary>
[GlobalClass, Tool]
public partial class Balloon : VehicleModule
{
	/// <summary>Mode du ballon.</summary>
	[Export] public BalloonMode Mode { get; set; } = BalloonMode.Gas;

	/// <summary>Poussée (N) à chaleur 1.</summary>
	[Export] public float LiftForce { get; set; } = 200f;

	/// <summary>Montgolfière : chaleur au repos (0..1).</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float BaseHeat { get; set; } = 0.6f;

	/// <summary>Montgolfière : secondes pour passer de 0 à 1 de chaleur (inertie).</summary>
	[Export] public float HeatChangeTime { get; set; } = 2.5f;

	/// <summary>Traînée linéaire par rapport à l'air (N par m/s).</summary>
	[Export] public float AirDrag { get; set; } = 6f;

	/// <summary>Rayon de l'enveloppe (m).</summary>
	[Export] public float Radius { get; set; } = 2.5f;

	/// <summary>Couleur de l'enveloppe.</summary>
	[Export] public Color FabricColor { get; set; } = new Color(0.75f, 0.65f, 0.45f);

	/// <summary>Crée un visuel simple au démarrage.</summary>
	[Export] public bool CreateDefaultVisual { get; set; } = true;

	/// <summary>Chaleur actuelle (0..1). Toujours 1 en mode Gas.</summary>
	public float Heat { get; private set; }

	private StandardMaterial3D _burnerMaterial;

	public override void _Ready()
	{
		base._Ready();

		if (Mode == BalloonMode.Gas && Channel == InputChannel.Throttle)
			Channel = InputChannel.Always;

		Heat = Mode == BalloonMode.Gas ? 1f : BaseHeat;

		if (CreateDefaultVisual)
			BuildVisual();
	}

	public override void _Process(double delta)
	{
		if (_burnerMaterial != null)
			_burnerMaterial.EmissionEnergyMultiplier = Heat * 3f;
	}

	protected override void Simulate(float delta, float command)
	{
		if (Mode == BalloonMode.Gas)
		{
			Heat = 1f;
		}
		else
		{
			float target = ModuleMath.MapCommand(command, BaseHeat);
			Heat = Mathf.MoveToward(Heat, target, delta / Mathf.Max(HeatChangeTime, 0.001f));
		}

		Vector3 lift = Vector3.Up * LiftForce * Heat;

		Vector3 airVel = GetPointVelocity() - Wind.GetWindAt(GlobalPosition);
		Vector3 drag = -airVel * AirDrag;

		ApplyForceAtSelf(lift + drag);
	}

	private void BuildVisual()
	{
		Visual.AddChild(new MeshInstance3D
		{
			Name = "Envelope",
			Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2.2f },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = FabricColor,
				Roughness = 0.9f,
			},
		});

		if (Mode != BalloonMode.HotAir)
			return;

		_burnerMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.5f, 0.1f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.5f, 0.1f),
			EmissionEnergyMultiplier = 0f,
		};

		Visual.AddChild(new MeshInstance3D
		{
			Name = "Burner",
			Position = new Vector3(0f, -Radius * 1.1f - 0.3f, 0f),
			Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f },
			MaterialOverride = _burnerMaterial,
		});
	}
}
