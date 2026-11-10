using System.Collections.Generic;
using Bomba.Cameras;
using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Clouds;

/// <summary>
/// Effets des nuages sur le véhicule suivi : traînée et turbulences dans le nuage, éclairs dans un
/// cœur d'orage (flash, secousse, impulsion, moteurs coupés un instant). Les densités viennent de
/// <see cref="CloudField"/> ; ce nœud ne fait que des forces sur <see cref="Target"/>.
/// </summary>
public partial class CloudEffects : Node, IVehicleTracker
{
    [ExportGroup("Perturbation")]
    /// <summary>Traînée (1/s) à densité 1.</summary>
    [Export] public float CloudDrag { get; set; } = 0.6f;
    /// <summary>Couple de turbulence maximal (rad/s²) à densité 1.</summary>
    [Export] public float TurbulenceTorque { get; set; } = 2.5f;
    /// <summary>Accélération de turbulence maximale (m/s²) à densité 1.</summary>
    [Export] public float TurbulenceForce { get; set; } = 5f;
    /// <summary>Fréquence (Hz) des turbulences.</summary>
    [Export] public float TurbulenceFrequency { get; set; } = 1.3f;

    [ExportGroup("Orage")]
    /// <summary>Intensité d'orage au-delà de laquelle les éclairs peuvent frapper le joueur.</summary>
    [Export] public float StormThreshold { get; set; } = 0.25f;
    /// <summary>Éclairs par seconde sur le joueur à orage 1.</summary>
    [Export] public float StrikeRate { get; set; } = 0.35f;
    /// <summary>Vitesse (m/s) de l'impulsion d'un éclair.</summary>
    [Export] public float StrikeImpulse { get; set; } = 9f;
    /// <summary>Vitesse angulaire (rad/s) donnée par un éclair.</summary>
    [Export] public float StrikeSpin { get; set; } = 2.5f;
    /// <summary>Durée (s) de la coupure des moteurs.</summary>
    [Export] public float StallDuration { get; set; } = 0.8f;
    /// <summary>Durée (s) du traceur : les filets cherchent l'engin, puis l'arc en retour frappe.</summary>
    [Export] public float StrikeLeaderTime { get; set; } = 0.6f;
    /// <summary>Éclairs décoratifs par seconde autour du joueur.</summary>
    [Export] public float AmbientStrikeRate { get; set; } = 0.25f;
    /// <summary>Rayon (m) des éclairs décoratifs autour du joueur.</summary>
    [Export] public float AmbientStrikeRadius { get; set; } = 25000f;
    /// <summary>Probabilité qu'un éclair d'ambiance puisse aussi partir d'un congestus local (éclair de chaleur, sans danger).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CongestusStrikeShare { get; set; } = 0f;

    [ExportGroup("Détection")]
    /// <summary>Densité au-delà de laquelle le joueur est considéré caché.</summary>
    [Export] public float HiddenDensity { get; set; } = 0.45f;

    private const float InCloudDensity = 0.02f;
    // Origine du traceur : dans le nuage, au-dessus et en avant de l'engin (visible depuis le cockpit).
    // Basse sur l'horizon : le toit du cockpit masque tout ce qui dépasse une vingtaine de degrés.
    private const float StrikeMinHeight = 120f;
    private const float StrikeMaxHeight = 250f;
    private const float StrikeMinAhead = 500f;
    private const float StrikeMaxAhead = 900f;
    private const float StrikeMaxSide = 250f;
    private const float StrikeShakeAmplitude = 0.8f;
    private const float StrikeShakeDuration = 0.4f;
    private const float AmbientMinAltitude = 600f;
    private const float AmbientMaxAltitude = 9000f;
    private const int AmbientCandidates = 8;
    private const float AmbientStormLimit = 0.5f;

    /// <summary>Véhicule suivi.</summary>
    public Vehicle Target { get; set; }

    /// <summary>Densité 0..1 sous le véhicule.</summary>
    public float Density { get; private set; }
    /// <summary>Intensité d'orage 0..1 sous le véhicule.</summary>
    public float Storm { get; private set; }
    /// <summary>Ascendance (m/s) sous le véhicule.</summary>
    public float Updraft { get; private set; }
    public bool InCloud { get; private set; }
    public bool Hidden { get; private set; }
    /// <summary>Vrai pendant une coupure de moteurs par la foudre.</summary>
    public bool Stalled => _stalled.Count > 0;

    private readonly Dictionary<VehicleModule, float> _stalled = new();   // moteur coupé → temps restant (s)
    private Vehicle _stallVehicle;
    private Vehicle _pendingVehicle;   // frappe en cours : traceur lancé, choc à venir
    private float _pendingTime;
    private float _forceStrikeAt;      // test : -- --force-strike=S
    private bool _teleportToCloud;    // test : -- --in-cloud place l'engin dans un nuage dense
    private readonly Dictionary<Vehicle, Vector3> _bowCache = new();
    private float _noiseTime;
    private readonly FastNoiseLite _noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        AddToGroup(IVehicleTracker.GroupName);
        _rng.Randomize();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--force-strike="))
                _forceStrikeAt = Mathf.Max(arg["--force-strike=".Length..].ToFloat(), 0.01f);
            else if (arg == "--in-cloud")
                _teleportToCloud = true;
    }

    public override void _ExitTree() => EndStall();

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _noiseTime += dt;
        _noise.Frequency = TurbulenceFrequency;

        // Coupure : elle prend fin si le véhicule change, disparaît, ou au bout du délai de chaque moteur.
        if (_stallVehicle != null && (_stallVehicle != Target || !IsInstanceValid(_stallVehicle)))
            EndStall();
        UpdateStall(dt);

        // Coup en attente : le choc tombe à l'arc en retour, si l'engin est toujours là.
        if (_pendingVehicle != null)
        {
            _pendingTime -= dt;
            if (_pendingTime <= 0f)
            {
                if (_pendingVehicle == Target && IsInstanceValid(_pendingVehicle))
                    ApplyStrikeImpact(_pendingVehicle, dt);
                _pendingVehicle = null;
            }
        }

        if (_teleportToCloud && IsInstanceValid(Target) && CloudField.Instance?.IsBaked == true
            && CloudField.Instance.TryRandomLightningPoint(Target.GlobalPosition, 40000f, _rng, true, out Vector3 inside))
        {
            _teleportToCloud = false;
            Target.GlobalPosition = inside;
            GD.Print($"[Test] engin placé dans un nuage en {inside}, densité {CloudField.Instance.Sample(inside).Density:F2}");
        }

        if (_forceStrikeAt > 0f)
        {
            _forceStrikeAt -= dt;
            if (_forceStrikeAt <= 0f && IsInstanceValid(Target) && CloudField.Instance?.IsBaked == true && _pendingVehicle == null)
                Strike(Target, Target.GlobalPosition);
            else if (_forceStrikeAt <= 0f)
                _forceStrikeAt = 0.5f;   // champ pas encore prêt : on réessaie
        }

        CloudField field = CloudField.Instance;
        if (!IsInstanceValid(Target) || field?.IsBaked != true)
        {
            Density = Storm = Updraft = 0f;
            InCloud = Hidden = false;
            return;
        }

        Vehicle v = Target;
        Vector3 pos = v.GlobalPosition;
        CloudSample s = field.Sample(pos);
        Density = s.Density;
        Storm = s.Storm;
        Updraft = s.Updraft * field.UpdraftSpeed;
        InCloud = Density > InCloudDensity;
        Hidden = Density >= HiddenDensity;

        // Traînée proportionnelle à la vitesse.
        v.ApplyCentralForce(-v.LinearVelocity * Density * CloudDrag * v.Mass);

        if (InCloud)
            ApplyTurbulence(v);

        if (_pendingVehicle == null && Storm > StormThreshold && _rng.Randf() < StrikeRate * Storm * dt)
            Strike(v, pos);

        if (_rng.Randf() < AmbientStrikeRate * dt)
            AmbientStrikes(field, pos);
    }

    private void ApplyTurbulence(Vehicle v)
    {
        Vector3 rotational = new(Channel(0), Channel(1), Channel(2));
        Vector3 linear = new(Channel(3), Channel(4), Channel(5));
        v.ApplyAngularAcceleration(rotational * TurbulenceTorque * Density);
        v.ApplyCentralForce(linear * TurbulenceForce * Density * v.Mass);
    }

    /// <summary>Bruit de Perlin au temps courant, canal i (6 canaux décalés, dans [-1, 1]).</summary>
    private float Channel(int i) =>
        Mathf.Clamp(_noise.GetNoise2D(_noiseTime, i * 41.7f), -1f, 1f);

    /// <summary>
    /// Frappe : le traceur part du nuage, devant et au-dessus de l'engin, et vise sa proue ; le choc tombe à l'arc en
    /// retour, <see cref="StrikeLeaderTime"/> s plus tard (le traceur suit l'engin : le coup est inévitable).
    /// </summary>
    private void Strike(Vehicle v, Vector3 pos)
    {
        Vector3 fwd = -v.GlobalBasis.Z;
        fwd = new Vector3(fwd.X, 0f, fwd.Z).LengthSquared() > 1e-4f ? new Vector3(fwd.X, 0f, fwd.Z).Normalized() : Vector3.Forward;
        Vector3 side = fwd.Cross(Vector3.Up);
        Vector3 from = pos + fwd * _rng.RandfRange(StrikeMinAhead, StrikeMaxAhead) + side * _rng.RandfRange(-StrikeMaxSide, StrikeMaxSide)
                       + Vector3.Up * _rng.RandfRange(StrikeMinHeight, StrikeMaxHeight);

        Vector3 targetLocal = BowPoint(v);

        CloudField.Instance.TriggerStrike(from, v, targetLocal, StrikeLeaderTime);
        _pendingVehicle = v;
        _pendingTime = StrikeLeaderTime;
        GD.Print($"[Foudre] traceur depuis {from.Y - pos.Y:F0} m au-dessus, proue {targetLocal}, impact dans {StrikeLeaderTime:F2} s");
    }

    /// <summary>
    /// Point d'accroche : la pointe de la proue, c'est-à-dire le sommet le plus en avant du modèle (repère de l'engin).
    /// La foudre s'accroche aux extrémités ; le courant traverse la coque (cage de Faraday) jusqu'aux moteurs.
    /// Visible du cockpit, et le canal touche vraiment la coque. Calculé une fois par engin.
    /// </summary>
    private Vector3 BowPoint(Vehicle v)
    {
        if (_bowCache.TryGetValue(v, out Vector3 cached))
            return cached;
        Transform3D toLocal = v.GlobalTransform.AffineInverse();
        Vector3 best = Vector3.Zero;
        float bestZ = float.MaxValue;
        var stack = new Stack<Node>();
        stack.Push(v);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is VehicleModule)
                continue;   // visuels des moteurs (flammes, fumées)
            if (n is MeshInstance3D { Mesh: Mesh mesh, Visible: true } mi)
            {
                Transform3D t = toLocal * mi.GlobalTransform;
                for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                {
                    if (mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array() is not { } verts)
                        continue;
                    foreach (Vector3 p in verts)
                    {
                        Vector3 q = t * p;
                        // Le plus en avant ; à 5 cm près, le plus haut.
                        if (q.Z < bestZ - 0.05f || (q.Z < bestZ + 0.05f && q.Y > best.Y))
                        {
                            bestZ = Mathf.Min(bestZ, q.Z);
                            best = q;
                        }
                    }
                }
            }
            foreach (Node c in n.GetChildren())
                stack.Push(c);
        }
        _bowCache[v] = best;
        return best;
    }

    /// <summary>Arc en retour : impulsion et rotation (atténuées par la résistance des moteurs), coupures, secousse.</summary>
    private void ApplyStrikeImpact(Vehicle v, float dt)
    {
        float resistance = 0f;
        int engines = 0;
        foreach (VehicleModule m in v.Modules)
        {
            if (m.MaxPower <= 0f)
                continue;
            resistance += Mathf.Clamp(m.LightningResistance, 0f, 1f);
            engines++;
        }
        float shock = 1f - 0.6f * (engines > 0 ? resistance / engines : 1f);

        // Impulsion biaisée vers le haut.
        Vector3 push = new(_rng.RandfRange(-1f, 1f), _rng.RandfRange(0.3f, 1f), _rng.RandfRange(-1f, 1f));
        v.ApplyCentralImpulse(push.Normalized() * StrikeImpulse * shock * v.Mass);

        // Une seule frame : Δω = StrikeSpin après le pas physique.
        if (dt > 0f)
            v.ApplyAngularAcceleration(RandomUnit() * StrikeSpin * shock / dt);

        StallEngines(v);
        ShakeCamera();
        GD.Print($"[Foudre] impact : choc {shock:F2}, moteurs coupés {_stalled.Count}/{engines}");
    }

    /// <summary>
    /// Chaque moteur se coupe avec la probabilité 1 − résistance, pour StallDuration × (1 − résistance) × [0,6 ; 1,4] :
    /// l'aléa rend la coupure asymétrique (vrille). Un nouveau coup prolonge une coupure en cours.
    /// </summary>
    private void StallEngines(Vehicle v)
    {
        if (_stallVehicle != null && _stallVehicle != v)
            EndStall();
        _stallVehicle = v;
        foreach (VehicleModule m in v.Modules)
        {
            if (m.MaxPower <= 0f)
                continue;
            float r = Mathf.Clamp(m.LightningResistance, 0f, 1f);
            if (_rng.Randf() >= 1f - r)
                continue;
            float time = StallDuration * (1f - r) * _rng.RandfRange(0.6f, 1.4f);
            if (_stalled.TryGetValue(m, out float left))
                _stalled[m] = Mathf.Max(left, time);
            else if (m.Enabled)
            {
                m.Enabled = false;
                _stalled[m] = time;
            }
            else
                continue;
            GD.Print($"[Foudre]   {m.Name} coupé {_stalled[m]:F2} s (résistance {r:F2})");
        }
        if (_stalled.Count == 0)
            _stallVehicle = null;
    }

    /// <summary>Décompte les coupures et rallume chaque moteur à la fin de la sienne.</summary>
    private void UpdateStall(float dt)
    {
        if (_stalled.Count == 0)
            return;
        var done = new List<VehicleModule>();
        foreach (VehicleModule m in new List<VehicleModule>(_stalled.Keys))
        {
            _stalled[m] -= dt;
            if (_stalled[m] <= 0f)
                done.Add(m);
        }
        foreach (VehicleModule m in done)
        {
            if (IsInstanceValid(m))
                m.Enabled = true;
            _stalled.Remove(m);
        }
        if (_stalled.Count == 0)
            _stallVehicle = null;
    }

    private void EndStall()
    {
        foreach (VehicleModule m in _stalled.Keys)
            if (IsInstanceValid(m))
                m.Enabled = true;
        _stalled.Clear();
        _stallVehicle = null;
    }

    private void ShakeCamera()
    {
        foreach (Node n in GetTree().GetNodesInGroup(IVehicleTracker.GroupName))
        {
            if (n is ChaseCamera cam)
            {
                cam.Shake(StrikeShakeAmplitude, StrikeShakeDuration);
                break;
            }
        }
    }

    private void AmbientStrikes(CloudField field, Vector3 pos)
    {
        // Viser un cœur orageux proche (cumulonimbus, et congestus selon CongestusStrikeShare).
        if (field.TryRandomLightningPoint(pos, AmbientStrikeRadius, _rng, _rng.Randf() < CongestusStrikeShare, out Vector3 heat))
        {
            field.TriggerLightning(heat);
            return;
        }
        for (int i = 0; i < AmbientCandidates; i++)
        {
            Vector2 horizontal = Vector2.Right.Rotated(_rng.Randf() * Mathf.Tau)
                                 * Mathf.Sqrt(_rng.Randf()) * AmbientStrikeRadius;
            var p = new Vector3(pos.X + horizontal.X,
                                _rng.RandfRange(AmbientMinAltitude, AmbientMaxAltitude),
                                pos.Z + horizontal.Y);
            if (field.Sample(p).Storm > AmbientStormLimit)
            {
                field.TriggerLightning(p);
                break;
            }
        }
    }

    private Vector3 RandomUnit() =>
        new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)).Normalized();
}
