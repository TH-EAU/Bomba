using Godot;

namespace Bomba.Core;

/// <summary>
/// État de détection du joueur, partagé entre les observateurs (projecteurs, tourelles, ennemis)
/// et l'interface. Les observateurs appellent <see cref="ReportSpotted"/> chaque frame où ils voient
/// le joueur ; l'interface lit <see cref="IsSpotted"/>.
/// </summary>
public static class Detection
{
    /// <summary>Durée (s) pendant laquelle le joueur reste « repéré » après le dernier signalement.</summary>
    public const float SpottedMemory = 0.5f;

    private static double _lastSpotted = -1000.0;

    /// <summary>Signale que le joueur est vu en ce moment.</summary>
    public static void ReportSpotted() => _lastSpotted = Time.GetTicksMsec() / 1000.0;

    /// <summary>Vrai si un observateur a vu le joueur récemment.</summary>
    public static bool IsSpotted => Time.GetTicksMsec() / 1000.0 - _lastSpotted < SpottedMemory;
}
