namespace Bomba.Vehicles;

/// <summary>
/// Canal d'entrée sur lequel un module est branché.
/// Permet de monter n'importe quel module sur n'importe quel véhicule.
/// </summary>
public enum InputChannel
{
    /// <summary>Jamais actif (commande = 0).</summary>
    None,
    /// <summary>Poussée principale, 0..1.</summary>
    Throttle,
    /// <summary>Commande verticale (ballon, rotor), -1..1.</summary>
    Vertical,
    /// <summary>Réglage d'orientation (voile), -1..1.</summary>
    Trim,
    /// <summary>Toujours à fond (commande = 1). Utile pour un ballon à gaz fixe ou un objet sans pilote.</summary>
    Always,
}
