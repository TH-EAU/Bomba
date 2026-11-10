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

	[ExportGroup("Pilotage")]
	/// <summary>Vitesse de roulis à fond (°/s). Comme un joystick : pousser fait rouler, lâcher garde l'inclinaison.</summary>
	[Export] public float RollRateDeg { get; set; } = 75f;
	/// <summary>Inclinaison maximale (°). 180 = libre (tonneaux, vol sur le dos) ; un dirigeable la limite.</summary>
	[Export] public float MaxBankDeg { get; set; } = 180f;
	/// <summary>Vitesse de tangage à fond (°/s). Tirer en étant incliné serre le virage ; tirer à fond fait un looping.</summary>
	[Export] public float PitchRateDeg { get; set; } = 45f;
	/// <summary>Temps (s) pour atteindre la vitesse de rotation demandée : inertie de pilotage, le nez prend son élan.</summary>
	[Export] public float RateRampTime { get; set; } = 0.35f;
	/// <summary>Virage dû à l'inclinaison seule (°/s à 90°). 0 (défaut) : rouler ne fait que rouler, c'est tirer qui fait virer.</summary>
	[Export] public float BankTurnRateDeg { get; set; } = 0f;
	/// <summary>Retour à plat (s), aussi depuis le dos, seulement après <see cref="AutoLevelDelay"/> sans aucune commande. 0 = désactivé.</summary>
	[Export] public float AutoLevelTime { get; set; } = 0f;
	/// <summary>Délai (s) sans commande de roulis ni de tangage avant le retour à plat : jamais pendant une manœuvre.</summary>
	[Export] public float AutoLevelDelay { get; set; } = 1.5f;
	/// <summary>Maintien d'assiette (1/s) quand le tangage est au neutre : l'engin garde sa pente. 0 = désactivé.</summary>
	[Export] public float PitchHold { get; set; } = 4f;

	[ExportGroup("Houle")]
	/// <summary>Amplitude (°) du léger roulis de houle, comme posé sur l'eau. 0 = désactivé.</summary>
	[Export] public float SwayRollDeg { get; set; } = 3f;
	/// <summary>Amplitude (°) du léger tangage de houle.</summary>
	[Export] public float SwayPitchDeg { get; set; } = 1.5f;
	/// <summary>Fréquence (Hz) de la houle : lente et irrégulière (bruit).</summary>
	[Export] public float SwayFrequency { get; set; } = 0.12f;

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

	[ExportGroup("Vitesse maximale")]
	/// <summary>Vitesse maximale (m/s) hors nitro. 0 : pas de limite (l'équilibre poussée / traînée décide).</summary>
	[Export] public float MaxSpeed { get; set; } = 0f;
	/// <summary>Vitesse maximale (m/s) sous nitro. 0 : pas de limite.</summary>
	[Export] public float NitroMaxSpeed { get; set; } = 0f;
	/// <summary>Fermeté du limiteur (1/s) : au-delà de la limite, freinage proportionnel à l'excès.</summary>
	[Export] public float MaxSpeedStiffness { get; set; } = 10f;

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
	private Vector3 _desiredAxis;
	private float _neutralTime;
	private float _swayTime, _prevSwayRoll;
	private const float SwayNorm = 2.3f;   // le bruit reste dans ±0,42 : on le ramène vers ±1
	private readonly FastNoiseLite _swayNoise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, Seed = 17 };
	private static readonly float AutoLevelMaxRate = Mathf.DegToRad(40f);   // remise à plat douce
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
		ApplySpeedLimit();
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
	/// <summary>Limiteur doux : au-delà de la vitesse maximale, une force freine proportionnellement à l'excès.</summary>
	private void ApplySpeedLimit()
	{
		float limit = NitroActive ? NitroMaxSpeed : MaxSpeed;
		if (limit <= 0f)
			return;
		float speed = LinearVelocity.Length();
		if (speed <= limit)
			return;
		ApplyCentralForce(-LinearVelocity / speed * (speed - limit) * MaxSpeedStiffness * Mass);
	}

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

	/// <summary>Inclinaison (rad) : 0 à plat, positive aile droite basse, ±π sur le dos.</summary>
	public float Bank
	{
		get
		{
			Basis b = GlobalBasis.Orthonormalized();
			return Mathf.Atan2(-b.X.Y, b.Y.Y);
		}
	}

	/// <summary>
	/// Pilotage « incliner et tirer » : le roulis commande une inclinaison (tenue), l'inclinaison fait virer (lacet de coordination autour de
	/// la verticale du monde), tirer serre le virage ; tirer à fond fait un looping. Pas de lacet direct.
	/// La vitesse de rotation visée est approchée avec une accélération plafonnée (RateRampTime) : le nez prend son
	/// élan. Au neutre : maintien d'assiette, et retour à plat (aussi depuis le dos). Tout est × autorité.
	/// </summary>
	private void ApplySteering(VehicleInput input)
	{
		Basis basis = GlobalBasis.Orthonormalized();
		Vector3 right = basis.X;
		Vector3 forward = -basis.Z;
		float bank = Mathf.Atan2(-right.Y, basis.Y.Y);
		float noseUp = Mathf.Asin(Mathf.Clamp(forward.Y, -1f, 1f));
		float rollRate = Mathf.DegToRad(RollRateDeg);
		float pitchRate = Mathf.DegToRad(PitchRateDeg);

		// Houle : roulis et tangage lents et irréguliers, ajoutés à la commande (pas de couple brut, donc sans dériver).
		float stepDt = Mathf.Max((float)GetPhysicsProcessDeltaTime(), 1e-4f);
		_swayTime += stepDt;
		float swayRoll = Mathf.DegToRad(SwayRollDeg) * SwayNorm * _swayNoise.GetNoise1D(_swayTime * SwayFrequency);
		float swayPitch = Mathf.DegToRad(SwayPitchDeg) * SwayNorm * _swayNoise.GetNoise1D(500f + _swayTime * SwayFrequency * 0.8f);
		float swayRollRate = (swayRoll - _prevSwayRoll) / stepDt;
		_prevSwayRoll = swayRoll;

		float rollCmd = 0f;
		Vector3 hold = Vector3.Zero;
		float pitchCmd = input.Pitch * pitchRate;
		bool pitchNeutral = Mathf.Abs(input.Pitch) < 0.02f;
		bool rollNeutral = Mathf.Abs(input.Roll) < 0.02f;

		// Maintien d'assiette : tangage au neutre, on garde la pente (plafonnée à ±60°).
		if (!pitchNeutral)
			_heldPitch = Mathf.Clamp(noseUp, -MaxHeldPitch, MaxHeldPitch);
		else if (PitchHold > 0f && Mathf.Abs(forward.Y) < 0.95f)
		{
			// Autour d'un axe HORIZONTAL (et non de l'aile, qui penche quand on est incliné) : le maintien ne fait que
			// lever ou baisser le nez, il ne fait jamais virer.
			Vector3 level = forward.Cross(Vector3.Up);
			if (level.LengthSquared() > 1e-6f)
				hold = level.Normalized() * Mathf.Clamp((_heldPitch + swayPitch - noseUp) * PitchHold, -pitchRate, pitchRate);
		}

		// Roulis comme un joystick : pousser fait rouler (vitesse), lâcher garde l'inclinaison. Retour à plat seulement
		// après AutoLevelDelay sans aucune commande (aide aux débutants), par le côté le plus court.
		_neutralTime = rollNeutral && pitchNeutral ? _neutralTime + (float)GetPhysicsProcessDeltaTime() : 0f;
		bool levelling = AutoLevelTime > 0f && _neutralTime >= AutoLevelDelay;
		if (!rollNeutral)
		{
			rollCmd = input.Roll * rollRate;
			// Inclinaison maximale : on n'enroule pas au-delà.
			float maxBank = Mathf.DegToRad(MaxBankDeg);
			if (MaxBankDeg < 179f && Mathf.Abs(bank) >= maxBank && Mathf.Sign(rollCmd) == Mathf.Sign(bank))
				rollCmd = 0f;
		}
		else if (levelling && Mathf.Abs(noseUp) < Mathf.DegToRad(70f))
			rollCmd = Mathf.Clamp(-bank / Mathf.Max(AutoLevelTime * 0.4f, 0.05f), -AutoLevelMaxRate, AutoLevelMaxRate);
		rollCmd += swayRollRate;

		// Retour à plat, nez trop près de la verticale (après un looping lâché) : on ramène d'abord le nez vers ±55°.
		// Sens du tangage selon le dos ou le ventre (up.Y), pour baisser le nez dans tous les cas.
		bool recovering = levelling && Mathf.Abs(noseUp) > Mathf.DegToRad(60f);
		if (recovering)
		{
			float wanted = Mathf.Sign(noseUp) * Mathf.DegToRad(55f);
			pitchCmd = Mathf.Clamp((wanted - noseUp) / 0.5f * (basis.Y.Y >= 0f ? 1f : -1f), -pitchRate, pitchRate);
		}

		// Virage dû à l'inclinaison (lacet à droite = -Up du monde), nul nez à la verticale.
		float coordination = Mathf.DegToRad(BankTurnRateDeg) * Mathf.Sin(bank) * Mathf.Cos(noseUp);

		// Tirer et piquer tournent autour de l'aile (repère de l'engin) : piquer baisse le nez par rapport au bateau.
		Vector3 pitchAxis = right;
		Vector3 target = (forward * rollCmd + pitchAxis * pitchCmd + hold - Vector3.Up * coordination) * Authority;
		// Commande transmise aux modules (gouvernes, moteurs orientables) : le tangage seulement. Une gouverne dans l'axe
		// ne peut pas rouler : à qui on demandait du roulis, elle produisait du tangage et du lacet parasites, énormes à
		// grande vitesse (le nez partait pendant un roulis franc). Le roulis est assuré par le couple direct ci-dessous.
		_desiredAxis = pitchAxis * (pitchCmd / pitchRate) - Vector3.Up * (coordination / pitchRate);

		// Rampe : accélération angulaire plafonnée par axe de l'engin (x tangage, y lacet, z roulis).
		float dt = (float)GetPhysicsProcessDeltaTime();
		float ramp = Mathf.Max(RateRampTime, 0.02f);
		Vector3 local = basis.Inverse() * (target - AngularVelocity) / Mathf.Max(dt, 1e-4f);
		Vector3 limit = new Vector3(pitchRate, pitchRate + Mathf.DegToRad(BankTurnRateDeg), rollRate) / ramp * Authority;
		// Freiner une rotation est permis 4 fois plus fort que la lancer : élan au départ, arrêt net
		// (et les couples des gouvernes et moteurs orientables ne font pas dépasser la consigne).
		Vector3 spin = basis.Inverse() * AngularVelocity;
		local = new Vector3(ClampRamp(local.X, spin.X, limit.X), ClampRamp(local.Y, spin.Y, limit.Y), ClampRamp(local.Z, spin.Z, limit.Z));
		ApplyAngularAcceleration(basis * local);
	}

	private static float ClampRamp(float accel, float spin, float limit)
	{
		float l = accel * spin < 0f ? limit * 4f : limit;
		return Mathf.Clamp(accel, -l, l);
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
	/// Tangage nez haut = +Right, roulis droite = +Forward, virage dû à l'inclinaison = -Up du monde.
	/// </summary>
	public Vector3 GetDesiredTorqueAxis()
	{
		return _desiredAxis;
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
