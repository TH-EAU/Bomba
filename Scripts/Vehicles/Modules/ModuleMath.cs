using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>Utilitaires partagés par les modules de vol.</summary>
public static class ModuleMath
{
    /// <summary>
    /// Transforme une commande (-1..1) en niveau (0..1) autour d'un niveau de base.
    /// Commande 0 = baseLevel, commande 1 = 1, commande -1 = 0.
    /// </summary>
    public static float MapCommand(float command, float baseLevel)
    {
        float value = command >= 0f
            ? baseLevel + command * (1f - baseLevel)
            : baseLevel * (1f + command);
        return Mathf.Clamp(value, 0f, 1f);
    }
}
