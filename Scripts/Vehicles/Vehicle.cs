using System.Collections.Generic;
using Bomba.Core;
using Godot;

namespace Bomba.Vehicles;

/// <summary>État de vol, pour le HUD et les multiplicateurs.</summary>
public enum FlightState
{
	/// <summary>Au moins un moteur pousse : contrôle élevé.</summary>
	Powered,
	/// <summary>Nitro active : contrôle maximal.</summary>
	Nitro,
	/// <summary>Moteurs coupés : l'engin est emporté par son élan, contrôle quasi nul.</summary>
	Ballistic,
}

/// <summary>
/// Corps d'un véhicule volant. Il ne connaît pas ses moteurs : ce sont ses enfants
/// <see cref="VehicleModule"/> qui appliquent leurs forces à leur position.
///
/// Le Vehicle gère ce qui est global à l'engin :
/// - PowerRatio (part de poussée moteur utilisée) et Authority (capacité à piloter) ;
/// - traînée anisotrope « effet de quille » : l'engin va où il pointe quand il est propulsé,
///   et glisse sur son élan quand il ne l'est plus ;
/// - pilotage assisté (couple vers une vitesse angulaire cible, proportionnel à l'autorité) ;
/// - stabilisation en roulis, nitro, état de vol, respawn.
///
/// Tous les réglages sont en unités d'accélération (indépendants de la masse).
/// </summary>
public partial class Vehicle : RigidBody3D
{
	[Signal] public delegate void NitroStartedEventHandler();
	[Signal] public delegate void NitroEndedEventHandler();

	[Export] public string DisplayName { get; set; } = "Véhicule";

	/// <summary>Vitesse vers l'avant (m/s) donnée à l'apparition et au respawn (lancement).</summary>
	[Export] public float LaunchSpeed { get; set; } = 0f;

	/// <summary>Centre de masse, en local. Le placer bas rend l'engin stable (effet pendule).</summary>
	[Export] public Vector3 CenterOfMassOffset { get; set; } = Vector3.Zero;

	[ExportGroup("Autorité de contrôle")]
	/// <summary>Poids de la puissance moteur dans l'autorité.</summary>
	[Export] public float PowerWeight { get; set; } = 1.0f;
	/// <summary>Poids de la vitesse air dans l'autorité (faible : sans moteur, on contrôle peu).</summary>
	[Export] public float AirWeight { get; set; } = 0.15f;
	/// <summary>Vitesse (m/s) à laquelle la vitesse air donne sa contribution maximale.</summary>
	[Export] public float ReferenceSpeed { get; set; } = 40f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float MinAuthority { get; set; } = 0.05f;

	[ExportGroup("Pilotage assisté")]
	/// <summary>Vitesses angulaires max (rad/s) : tangage, lacet, roulis.</summary>
	[Export] public Vector3 MaxTurnRates { get; set; } = new(1.4f, 1.2f, 2.0f);
	/// <summary>Réactivité de l'asservissement en vitesse angulaire (1/s).</summary>
	[Export] public float SteerResponse { get; set; } = 6f;
	/// <summary>Force du rappel en roulis vers l'horizontale (et vers l'inclinaison de virage).</summary>
	[Export] public float RollAssist { get; set; } = 10f;
	/// <summary>Inclinaison automatique en virage (degrés) quand on tourne en lacet.</summary>
	[Export] public float AutoBankDegrees { get; set; } = 25f;
	/// <summary>Maintien d'assiette en tangage quand le joueur ne touche pas au tangage (1/s²). 0 = désactivé.</summary>
	[Export] public float PitchHold { get; set; } = 4f;
	/// <summary>Vrai : sans commande, le nez revient à l'horizontale (hélico, ballon). Faux : garde l'assiette relâchée.</summary>
	[Export] public bool PitchAutoLevel { get; set; }

	[ExportGroup("Traînée (accélérations)")]
	/// <summary>Traînée quadratique vers l'avant (1/m).</summary>
	[Export] public float ForwardDrag { get; set; } = 0.004f;
	/// <summary>Adhérence latérale propulsé / balistique (1/s).</summary>
	[Export] public float LateralGripPowered { get; set; } = 2.5f;
	[Export] public float LateralGripBallistic { get; set; } = 0.15f;
	/// <summary>Adhérence verticale propulsé / balistique (1/s).</summary>
	[Export] public float VerticalGripPowered { get; set; } = 1.5f;
	[Export] public float VerticalGripBallistic { get; set; } = 0.05f;
	/// <summary>Part de la vitesse perdue par l'adhérence qui est redirigée vers l'avant (arcade).</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float GripRedirect { get; set; } = 0.5f;
	/// <summary>Freinage linéaire de l'aérofrein (1/s).</summary>
	[Export] public float BrakeDrag { get; set; } = 1.2f;

	[ExportGroup("Sustentation arcade")]
	/// <summary>Part de la gravité compensée à pleine puissance (0 = aucune aide, physique pure).</summary>
	[Export(PropertyHint.Range, "0,1.5,0.01")] public float HoverAssist { get; set; } = 0f;

	[ExportGroup("Nitro")]
	[Export] public float NitroDrainPerSecond { get; set; } = 0.6f;
	[Export] public float NitroRegenPerSecond { get; set; } = 0.15f;
	/// <summary>Jauge minimale pour déclencher la nitro.</summary>
	[Export(PropertyHint.Range, "0,1,0.01")] public float NitroMinToStart { get; set; } = 0.25f;
	/// <summary>Gain de vitesse instantané au déclenchement (m/s).</summary>
	[Export] public float NitroImpulse { get; set; } = 14f;
	/// <summary>Accélération supplémentaire vers l'avant pendant la nitro (m/s²).</summary>
	[Export] public float NitroAcceleration { get; set; } = 18f;
	/// <summary>Multiplicateur de poussée lu par les propulseurs pendant la nitro.</summary>
	[Export] public float NitroThrustMultiplier { get; set; } = 2f;

	[ExportGroup("État")]
	/// <summary>PowerRatio sous lequel l'engin est considéré moteurs coupés.</summary>
	[Export] public float BallisticThreshold { get; set; } = 0.05f;
	/// <summary>Délai (s) moteurs coupés avant de passer en balistique.</summary>
	[Export] public float BallisticDelay { get; set; } = 0.15f;

	// --- État public (lecture) ---

	/// <summary>Commandes de la frame courante. Écrites par le contrôleur avant la physique du véhicule.</summary>
	public VehicleInput CurrentInput { get; set; }

	/// <summary>Poussée moteur actuelle / poussée moteur max, 0..1.</summary>
	public float PowerRatio { get; private set; }

	/// <summary>Capacité à piloter, 0..1. Vaut 1 pendant la nitro.</summary>
	public float Authority { get; private set; }

	public FlightState State { get; private set; } = FlightState.Ballistic;

	/// <summary>Jauge de nitro, 0..1.</summary>
	public float NitroGauge { get; private set; } = 1f;

	public bool NitroActive { get; private set; }

	/// <summary>Multiplicateur que les propulseurs appliquent à leur poussée.</summary>
	public float ThrustMultiplier => NitroActive ? NitroThrustMultiplier : 1f;

	/// <summary>Direction avant globale (-Z local).</summary>
	public Vector3 Forward => -GlobalBasis.Z.Normalized();
	public Vector3 Up => GlobalBasis.Y.Normalized();
	public Vector3 Right => GlobalBasis.X.Normalized();

	/// <summary>Vitesse par rapport au sol (m/s).</summary>
	public float Speed => LinearVelocity.Length();

	/// <summary>Vitesse selon l'axe avant (m/s), négative en marche arrière.</summary>
	public float ForwardSpeed => LinearVelocity.Dot(Forward);

	/// <summary>Vitesse par rapport à l'air (vent déduit).</summary>
	public Vector3 AirVelocity => LinearVelocity - Wind.GetWindAt(GlobalPosition);

	/// <summary>Centre de masse en coordonnées globales.</summary>
	public Vector3 GlobalCenterOfMass => GlobalTransform * CenterOfMassOffset;

	public IReadOnlyList<VehicleModule> Modules => _modules;

	private readonly List<VehicleModule> _modules = new();
	private float _ballisticTimer;
	private float _gravity = 9.8f;
	private float _heldPitch;
	private const float MaxHeldPitch = 1.05f; // ≈ 60°

	public override void _Ready()
	{
		CenterOfMassMode = CenterOfMassModeEnum.Custom;
		CenterOfMass = CenterOfMassOffset;
		_gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
		// Couche 2 : seuls les véhicules reçoivent le soleil rasant du crépuscule (TwilightSun).
		AddVehicleLayer(this);
		CanSleep = false;
		CollectModules(this);
	}

	private static void AddVehicleLayer(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is VisualInstance3D vi)
				vi.Layers |= TwilightSun.VehicleLayer;
			AddVehicleLayer(child);
		}
	}

	private void CollectModules(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is VehicleModule module)
				_modules.Add(module);
			// Ne pas descendre dans un autre corps physique (il porte ses propres modules).
			if (child is not RigidBody3D)
				CollectModules(child);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Freeze)
			return;

		float dt = (float)delta;
		VehicleInput input = CurrentInput;

		UpdatePowerRatio();
		UpdateNitro(input, dt);
		UpdateState(dt);

		float airContribution = Mathf.Clamp(AirVelocity.Length() / Mathf.Max(ReferenceSpeed, 0.01f), 0f, 1f);
		Authority = NitroActive
			? 1f
			: Mathf.Clamp(PowerRatio * PowerWeight + airContribution * AirWeight, MinAuthority, 1f);

		ApplyDrag(input);
		ApplySteering(input);

		if (NitroActive)
			ApplyCentralForce(Forward * NitroAcceleration * Mass);

		if (HoverAssist > 0f)
			ApplyCentralForce(Vector3.Up * _gravity * GravityScale * Mass * HoverAssist * PowerRatio);
	}

	private void UpdatePowerRatio()
	{
		float max = 0f, current = 0f;
		foreach (VehicleModule m in _modules)
		{
			if (!m.Enabled) continue;
			max += m.MaxPower;
			current += m.CurrentPower;
		}
		PowerRatio = max > 0f ? Mathf.Clamp(current / max, 0f, 1f) : 0f;
	}

	private void UpdateNitro(VehicleInput input, float dt)
	{
		if (NitroActive)
		{
			NitroGauge -= NitroDrainPerSecond * dt;
			if (!input.Nitro || NitroGauge <= 0f)
			{
				NitroGauge = Mathf.Max(NitroGauge, 0f);
				NitroActive = false;
				EmitSignal(SignalName.NitroEnded);
			}
		}
		else
		{
			if (input.Nitro && NitroGauge >= NitroMinToStart)
			{
				NitroActive = true;
				ApplyCentralImpulse(Forward * NitroImpulse * Mass);
				EmitSignal(SignalName.NitroStarted);
			}
			else
			{
				NitroGauge = Mathf.Min(NitroGauge + NitroRegenPerSecond * dt, 1f);
			}
		}
	}

	private void UpdateState(float dt)
	{
		if (NitroActive)
		{
			State = FlightState.Nitro;
			_ballisticTimer = 0f;
			return;
		}

		if (PowerRatio < BallisticThreshold)
		{
			_ballisticTimer += dt;
			if (_ballisticTimer >= BallisticDelay)
				State = FlightState.Ballistic;
		}
		else
		{
			_ballisticTimer = 0f;
			State = FlightState.Powered;
		}
	}

	/// <summary>
	/// Traînée anisotrope : adhérence latérale/verticale forte quand l'autorité est haute,
	/// faible en balistique. Une partie de la vitesse "accrochée" est redirigée vers l'avant.
	/// </summary>
	private void ApplyDrag(VehicleInput input)
	{
		Basis basis = GlobalBasis.Orthonormalized();
		Vector3 vLocal = basis.Inverse() * AirVelocity;

		float latGrip = Mathf.Lerp(LateralGripBallistic, LateralGripPowered, Authority);
		float vertGrip = Mathf.Lerp(VerticalGripBallistic, VerticalGripPowered, Authority);

		float ax = -vLocal.X * latGrip;
		float ay = -vLocal.Y * vertGrip;
		float az = -vLocal.Z * Mathf.Abs(vLocal.Z) * ForwardDrag - vLocal.Z * input.Brake * BrakeDrag;

		// Avant = -Z local. On ne redirige que si l'on avance.
		if (vLocal.Z < 0f && GripRedirect > 0f)
			az -= (Mathf.Abs(ax) + Mathf.Abs(ay)) * GripRedirect * Authority;

		ApplyCentralForce(basis * new Vector3(ax, ay, az) * Mass);
	}

	/// <summary>
	/// Pilotage assisté : asservit la vitesse angulaire vers une cible donnée par les commandes,
	/// avec une intensité proportionnelle à l'autorité. Sans autorité, l'engin garde sa rotation.
	/// </summary>
	private void ApplySteering(VehicleInput input)
	{
		Basis basis = GlobalBasis.Orthonormalized();
		Vector3 right = basis.X;
		Vector3 forward = -basis.Z;

		// Axes globaux : tangage nez haut = +Right, roulis à droite = +Forward,
		// lacet à droite = -Up DU MONDE (arcade : virer ne fait pas piquer quand on est incliné).
		Vector3 target = (right * input.Pitch * MaxTurnRates.X
						  + forward * input.Roll * MaxTurnRates.Z
						  - Vector3.Up * input.Yaw * MaxTurnRates.Y) * Authority;

		Vector3 accel = (target - AngularVelocity) * SteerResponse * Authority;

		// Tangage : maintien d'assiette (l'asservissement en vitesse seul laisse dériver le nez
		// sous un couple constant). Assiette maintenue plafonnée, inactif près de la verticale.
		float forwardY = forward.Y;
		float currentPitch = Mathf.Asin(Mathf.Clamp(forwardY, -1f, 1f));
		if (Mathf.Abs(input.Pitch) > 0.05f)
		{
			_heldPitch = PitchAutoLevel ? 0f : Mathf.Clamp(currentPitch, -MaxHeldPitch, MaxHeldPitch);
		}
		else if (PitchHold > 0f && Mathf.Abs(forwardY) < 0.95f)
		{
			float upright = basis.Y.Y >= 0f ? 1f : -1f;
			accel += right * ((_heldPitch - currentPitch) * PitchHold * Authority * upright);
		}

		// Roulis : rappel vers l'horizontale, ou vers l'inclinaison de virage.
		float rollFreedom = 1f - Mathf.Abs(input.Roll);
		if (RollAssist > 0f && rollFreedom > 0f)
		{
			float targetRightY = -input.Yaw * Mathf.Sin(Mathf.DegToRad(AutoBankDegrees));
			float error = right.Y - targetRightY;
			// Aile droite trop haute (error > 0) → rouler à droite (+Forward).
			accel += forward * (error * RollAssist * rollFreedom * Mathf.Lerp(0.3f, 1f, Authority));
		}

		ApplyAngularAcceleration(accel);
	}

	/// <summary>Applique une accélération angulaire globale (rad/s²), indépendante de l'inertie.</summary>
	public void ApplyAngularAcceleration(Vector3 globalAngularAcceleration)
	{
		PhysicsDirectBodyState3D state = PhysicsServer3D.BodyGetDirectState(GetRid());
		if (state == null)
			return;
		Basis invInertia = state.InverseInertiaTensor;
		if (Mathf.Abs(invInertia.Determinant()) < 1e-12f)
			return;
		ApplyTorque(invInertia.Inverse() * globalAngularAcceleration);
	}

	/// <summary>
	/// Axe de rotation global souhaité d'après les commandes (longueur 0..√3).
	/// Tangage nez haut = +Right, lacet droite = -Up du monde, roulis droite = +Forward.
	/// </summary>
	public Vector3 GetDesiredTorqueAxis()
	{
		VehicleInput i = CurrentInput;
		return Right * i.Pitch - Vector3.Up * i.Yaw + Forward * i.Roll;
	}

	/// <summary>
	/// Direction de poussée orientée (gimbal) pour un moteur placé en <paramref name="globalPosition"/>,
	/// afin que sa poussée produise le couple demandé par les commandes.
	/// Fonctionne quelle que soit la position du moteur par rapport au centre de masse.
	/// </summary>
	/// <param name="baseDirection">Direction de poussée au repos (globale, normalisée).</param>
	/// <param name="globalPosition">Position globale du moteur.</param>
	/// <param name="maxAngleRadians">Débattement max du gimbal.</param>
	public Vector3 GetGimbalDirection(Vector3 baseDirection, Vector3 globalPosition, float maxAngleRadians)
	{
		Vector3 torqueAxis = GetDesiredTorqueAxis();
		Vector3 lever = globalPosition - GlobalCenterOfMass;
		if (torqueAxis.LengthSquared() < 1e-6f || lever.LengthSquared() < 1e-4f)
			return baseDirection;

		// Pour r ⟂ T, la force (T × r) produit un couple selon T : r × (T × r) = T |r|².
		Vector3 lateral = torqueAxis.Cross(lever);
		lateral -= baseDirection * lateral.Dot(baseDirection);
		if (lateral.LengthSquared() < 1e-6f)
			return baseDirection;

		float amount = Mathf.Min(torqueAxis.Length(), 1f) * Mathf.Tan(maxAngleRadians);
		return (baseDirection + lateral.Normalized() * amount).Normalized();
	}

	/// <summary>Téléporte le véhicule, remet à zéro rotation, nitro et état, et le lance à <see cref="LaunchSpeed"/>.</summary>
	public void Respawn(Transform3D globalTransform)
	{
		Vector3 launch = -globalTransform.Basis.Z.Normalized() * LaunchSpeed;
		PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.Transform, globalTransform);
		PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.LinearVelocity, launch);
		PhysicsServer3D.BodySetState(GetRid(), PhysicsServer3D.BodyState.AngularVelocity, Vector3.Zero);
		GlobalTransform = globalTransform;
		LinearVelocity = launch;
		AngularVelocity = Vector3.Zero;
		ResetPhysicsInterpolation();
		NitroGauge = 1f;
		if (NitroActive)
		{
			NitroActive = false;
			EmitSignal(SignalName.NitroEnded);
		}
		_ballisticTimer = 0f;
		_heldPitch = 0f;
	}
}
