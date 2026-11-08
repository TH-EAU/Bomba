using Bomba.Vehicles;

namespace Bomba.Core;

/// <summary>
/// Tout nœud qui suit le véhicule du joueur (caméra, HUD, gizmos, contrôleur).
/// Le nœud doit aussi être dans le groupe <see cref="GroupName"/> : GameRoot lui assigne
/// le véhicule courant à chaque spawn ou changement de véhicule.
/// </summary>
public interface IVehicleTracker
{
    public const string GroupName = "vehicle_trackers";

    Vehicle Target { get; set; }
}
