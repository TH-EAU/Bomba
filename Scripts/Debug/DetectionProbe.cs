using Bomba.Arena;
using Bomba.Vehicles;
using Bomba.Clouds;
using Bomba.Core;
using Godot;

namespace Bomba.DebugTools;

/// <summary>
/// Vérifie la cachette : immobilise le véhicule du joueur (1) à découvert dans l'axe de balayage
/// d'un projecteur, puis (2) dans un nuage sur ce même balayage, et compte les frames où il est repéré.
/// Attendu : (1) repéré au moins une fois pendant un cycle de balayage, (2) jamais repéré.
/// <c>godot --headless --path . res://Scenes/Debug/DetectionProbe.tscn</c>
/// </summary>
public partial class DetectionProbe : Node
{
    private const float PhaseDuration = 11f; // > période de balayage (9 s)

    private Searchlight _light;
    private int _phase = -1;
    private float _time;
    private int _spottedFrames;
    private Vector3 _openPoint, _cloudPoint;

    public override void _PhysicsProcess(double delta)
    {
        GameRoot root = GameRoot.Instance;
        if (root?.CurrentVehicle == null || CloudField.Instance?.IsBaked != true)
            return;

        if (_phase < 0)
        {
            _light = GetTree().Root.FindChild("SearchlightNord", true, false) as Searchlight;
            if (_light == null) { GD.PrintErr("[Détection] projecteur introuvable"); GetTree().Quit(1); return; }
            FindPoints();
            _phase = 0;
            Place(root.CurrentVehicle, _openPoint);
            GD.Print($"[Détection] phase 1 : à découvert en {_openPoint}, transmittance {Transmittance(_openPoint):F2}");
            return;
        }

        _time += (float)delta;
        if (Detection.IsSpotted)
            _spottedFrames++;
        Place(root.CurrentVehicle, _phase == 0 ? _openPoint : _cloudPoint);

        if (_time < PhaseDuration)
            return;

        GD.Print($"[Détection] phase {_phase + 1} : repéré pendant {_spottedFrames} frames");
        _time = 0f;
        _spottedFrames = 0;
        _phase++;
        if (_phase == 1)
        {
            // Les nuages ont dérivé pendant la phase 1 : on choisit le point nuageux maintenant.
            FindPoints();
        }
        if (_phase == 1)
        {
            GD.Print($"[Détection] phase 2 : dans le nuage en {_cloudPoint}, densité {CloudField.Instance.GetDensity(_cloudPoint):F2}, transmittance {Transmittance(_cloudPoint):F2}");
        }
        else
        {
            GetTree().Quit();
        }
    }

    private Vector3 Lamp => _light.GlobalPosition + Vector3.Up * 12f;

    private float Transmittance(Vector3 p) => CloudField.Instance.GetTransmittance(Lamp, p);

    /// <summary>Cherche un point dégagé et un point dans un nuage, tous deux dans le cône de balayage.</summary>
    private void FindPoints()
    {
        Vector3 forward = -_light.GlobalBasis.Z;
        forward.Y = 0;
        forward = forward.Normalized();
        float pitch = Mathf.DegToRad(_light.BeamPitchDegrees);
        bool openFound = false, cloudFound = false;

        for (float yaw = 0f; yaw <= 60f && !(openFound && cloudFound); yaw += 4f)
        foreach (float sign in new[] { 1f, -1f })
        for (float dist = 120f; dist <= 1100f; dist += 20f)
        {
            Vector3 dir = forward.Rotated(Vector3.Up, Mathf.DegToRad(yaw * sign));
            Vector3 p = Lamp + (dir * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch)) * dist;
            float t = Transmittance(p);
            if (!openFound && t > 0.8f && CloudField.Instance.GetDensity(p) < 0.01f) { _openPoint = p; openFound = true; }
            if (!cloudFound && t < 0.03f && CloudField.Instance.GetDensity(p) > 0.5f) { _cloudPoint = p; cloudFound = true; }
        }
        if (!cloudFound)
            GD.PrintErr("[Détection] aucun point nuageux trouvé dans le balayage");
    }

    private static void Place(Vehicle v, Vector3 p)
    {
        v.Freeze = true;
        v.GlobalPosition = p;
    }
}
