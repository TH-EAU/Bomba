using Godot;

namespace Bomba.Vehicles;

/// <summary>
/// Commandes de pilotage d'un véhicule pour une frame physique.
/// Produit par <see cref="PlayerVehicleController"/> (et plus tard par une IA).
/// </summary>
public struct VehicleInput
{
    /// <summary>Poussée principale, 0..1.</summary>
    public float Throttle;
    /// <summary>Aérofrein, 0..1.</summary>
    public float Brake;
    /// <summary>Tangage, -1..1. Positif = nez vers le haut.</summary>
    public float Pitch;
    /// <summary>Lacet, -1..1. Positif = nez vers la droite.</summary>
    public float Yaw;
    /// <summary>Roulis, -1..1. Positif = aile droite vers le bas.</summary>
    public float Roll;
    /// <summary>Commande verticale (ballons, rotors), -1..1. Positif = monter.</summary>
    public float Vertical;
    /// <summary>Réglage de voile, -1..1.</summary>
    public float Trim;
    /// <summary>Bouton nitro maintenu.</summary>
    public bool Nitro;

    /// <summary>Valeur de commande pour un canal donné.</summary>
    public readonly float GetChannel(InputChannel channel) => channel switch
    {
        InputChannel.Throttle => Throttle,
        InputChannel.Vertical => Vertical,
        InputChannel.Trim => Trim,
        InputChannel.Always => 1f,
        _ => 0f,
    };

    public readonly bool HasSteering =>
        !Mathf.IsZeroApprox(Pitch) || !Mathf.IsZeroApprox(Yaw) || !Mathf.IsZeroApprox(Roll);
}
