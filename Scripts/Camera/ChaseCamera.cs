using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Cameras;

/// <summary>
/// Caméra de poursuite : suit le véhicule cible derrière lui, avec lissage exponentiel,
/// anticipation de la vitesse, FOV dynamique et secousse au déclenchement de la nitro.
/// </summary>
public partial class ChaseCamera : Camera3D, IVehicleTracker
{
    /// <summary>Cockpit : la caméra prend le marqueur « CockpitCamera » du véhicule. Chase : poursuite.</summary>
    public enum CameraMode { Cockpit, Chase }

    [ExportGroup("Mode")]
    /// <summary>Mode de vue initial. F5 bascule entre les deux.</summary>
    [Export] public CameraMode Mode { get; set; } = CameraMode.Cockpit;
    /// <summary>FOV en vue cockpit (degrés).</summary>
    [Export] public float CockpitFov { get; set; } = 75f;

    [ExportGroup("Suivi")]
    /// <summary>Distance derrière la cible (m).</summary>
    [Export] public float Distance { get; set; } = 14f;
    /// <summary>Hauteur au-dessus de la cible (m).</summary>
    [Export] public float Height { get; set; } = 4f;
    /// <summary>Anticipation : la caméra vise la position future de la cible (s).</summary>
    [Export] public float LookAheadTime { get; set; } = 0.25f;
    /// <summary>Réactivité du lissage de position (1/s).</summary>
    [Export] public float PositionSharpness { get; set; } = 8f;
    /// <summary>Réactivité du lissage de direction (1/s).</summary>
    [Export] public float RotationSharpness { get; set; } = 5f;

    [ExportGroup("Champ de vision")]
    /// <summary>FOV au repos (degrés).</summary>
    [Export] public float BaseFov { get; set; } = 70f;
    /// <summary>Élargissement maximal du FOV avec la vitesse (degrés).</summary>
    [Export] public float SpeedFovBonus { get; set; } = 15f;
    /// <summary>Vitesse (m/s) à laquelle l'élargissement vitesse est maximal.</summary>
    [Export] public float SpeedForMaxFov { get; set; } = 60f;
    /// <summary>Élargissement supplémentaire pendant la nitro (degrés).</summary>
    [Export] public float NitroFovKick { get; set; } = 12f;
    /// <summary>Réactivité du lissage du FOV (1/s).</summary>
    [Export] public float FovSharpness { get; set; } = 4f;

    [ExportGroup("Secousse nitro")]
    /// <summary>Amplitude maximale de la secousse (m) au déclenchement.</summary>
    [Export] public float NitroShakeAmplitude { get; set; } = 0.35f;
    /// <summary>Durée de la secousse (s).</summary>
    [Export] public float NitroShakeDuration { get; set; } = 0.35f;
    /// <summary>Part de la secousse gardée en vue cockpit : la coque est à quelques centimètres, le moindre décalage y paraît violent.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float CockpitShakeScale { get; set; } = 0.15f;
    /// <summary>Fréquence de la secousse (Hz). Un bruit lisse plutôt qu'un tirage par image : vibration, pas saccade.</summary>
    [Export] public float ShakeFrequency { get; set; } = 18f;

    private Vehicle _target;
    private Vector3 _smoothedDir = Vector3.Forward;
    private Vector3 _smoothedPos;
    private float _fov;
    private float _shakeTime;
    private float _shakeAmplitude;
    private float _shakeDuration = 1f;
    private float _shakeClock;
    private readonly FastNoiseLite _shakeNoise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Seed = 3, Frequency = 1f };
    private float _chaseNear;

    private const float CockpitNear = 0.05f;

    /// <summary>Véhicule suivi. Peut être changé à l'exécution, ou valoir null.</summary>
    public Vehicle Target
    {
        get => _target;
        set
        {
            if (IsInstanceValid(_target))
                _target.NitroStarted -= OnNitroStarted;

            _target = value;

            if (IsInstanceValid(_target))
                _target.NitroStarted += OnNitroStarted;

            // Pas de lissage au changement de cible : on se place directement derrière elle.
            if (IsInsideTree() && IsInstanceValid(_target))
                Snap();
        }
    }

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);
        // Déplacée dans _Process depuis la transform interpolée de la cible : pas de double interpolation.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        Current = true;
        _fov = BaseFov;
        Fov = BaseFov;
        _chaseNear = Near;
        if (IsInstanceValid(_target))
            Snap();
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_target))
            _target.NitroStarted -= OnNitroStarted;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed(GameActions.CameraToggle) && IsInstanceValid(_target))
        {
            Mode = Mode == CameraMode.Cockpit ? CameraMode.Chase : CameraMode.Cockpit;
            // Retour en poursuite : on repart de la position derrière la cible, pas de l'ancien lissage.
            if (Mode == CameraMode.Chase)
                Snap();
        }

        if (!IsInstanceValid(_target))
            return;

        float dt = (float)delta;

        // La secousse est appliquée à part, pour ne pas s'accumuler dans la position lissée.
        Vector3 shake = Vector3.Zero;
        if (_shakeTime > 0f)
        {
            float amp = _shakeAmplitude * (_shakeTime / _shakeDuration);
            _shakeClock += dt * ShakeFrequency;
            // Bruit simplex brut borné à ±0,42 : ×2,3 pour retrouver l'amplitude demandée.
            shake = new Vector3(_shakeNoise.GetNoise1D(_shakeClock), _shakeNoise.GetNoise1D(300f + _shakeClock),
                _shakeNoise.GetNoise1D(600f + _shakeClock)) * (amp * 2.3f);
            _shakeTime -= dt;
        }

        Node3D cockpit = Mode == CameraMode.Cockpit ? _target.GetNodeOrNull<Marker3D>("CockpitCamera") : null;
        if (cockpit != null)
        {
            Transform3D ct = cockpit.GetGlobalTransformInterpolated();
            ct.Origin += shake * CockpitShakeScale;
            GlobalTransform = ct;
            Near = CockpitNear;

            float cockpitFov = CockpitFov + (_target.NitroActive ? NitroFovKick : 0f);
            float kCockpitFov = 1f - Mathf.Exp(-FovSharpness * dt);
            _fov = Mathf.Lerp(_fov, cockpitFov, kCockpitFov);
            Fov = _fov;
            return;
        }

        Near = _chaseNear;

        Transform3D t = _target.GetGlobalTransformInterpolated();
        Vector3 pos = t.Origin;
        Vector3 fwd = -t.Basis.Z.Normalized();

        // En chute, on suit la trajectoire plutôt que le nez, qui tourne.
        Vector3 follow = fwd;
        if (_target.State == FlightState.Ballistic && _target.LinearVelocity.Length() > 2f)
            follow = fwd.Lerp(_target.LinearVelocity.Normalized(), 0.6f).Normalized();

        float kRot = 1f - Mathf.Exp(-RotationSharpness * dt);
        _smoothedDir = _smoothedDir.Lerp(follow, kRot).Normalized();

        Vector3 desired = pos - _smoothedDir * Distance + Vector3.Up * Height;
        float kPos = 1f - Mathf.Exp(-PositionSharpness * dt);
        _smoothedPos = _smoothedPos.Lerp(desired, kPos);

        GlobalPosition = _smoothedPos + shake;

        Vector3 aimPoint = pos + _target.LinearVelocity * LookAheadTime;
        Vector3 aimDir = (aimPoint - GlobalPosition).Normalized();
        Vector3 up = Mathf.Abs(aimDir.Dot(Vector3.Up)) > 0.98f ? _smoothedDir : Vector3.Up;
        LookAt(aimPoint, up);

        float targetFov = BaseFov
            + SpeedFovBonus * Mathf.Clamp(_target.Speed / SpeedForMaxFov, 0f, 1f)
            + (_target.NitroActive ? NitroFovKick : 0f);
        float kFov = 1f - Mathf.Exp(-FovSharpness * dt);
        _fov = Mathf.Lerp(_fov, targetFov, kFov);
        Fov = _fov;
    }

    /// <summary>Place la caméra directement derrière la cible, sans lissage.</summary>
    private void Snap()
    {
        Transform3D t = _target.GetGlobalTransformInterpolated();
        _smoothedDir = -t.Basis.Z.Normalized();
        _smoothedPos = t.Origin - _smoothedDir * Distance + Vector3.Up * Height;
        GlobalPosition = _smoothedPos;
    }

    /// <summary>Secousse ponctuelle. Si une secousse est déjà en cours et plus forte, elle est conservée.</summary>
    public void Shake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;
        float current = _shakeTime > 0f ? _shakeAmplitude * (_shakeTime / _shakeDuration) : 0f;
        if (amplitude < current)
            return;
        _shakeAmplitude = amplitude;
        _shakeDuration = duration;
        _shakeTime = duration;
    }

    private void OnNitroStarted()
    {
        _shakeAmplitude = NitroShakeAmplitude;
        _shakeDuration = NitroShakeDuration;
        _shakeTime = NitroShakeDuration;
    }
}
