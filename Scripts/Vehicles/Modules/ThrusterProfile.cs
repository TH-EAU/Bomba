using Godot;

namespace Bomba.Vehicles.Modules;

/// <summary>Réglages d'un propulseur, sous forme de ressource (un fichier .tres par type de moteur).</summary>
[GlobalClass, Tool]
public partial class ThrusterProfile : Resource
{
    /// <summary>Nom affiché dans le HUD et l'éditeur.</summary>
    [Export] public string DisplayName { get; set; } = "Propulseur";

    /// <summary>Poussée maximale (N).</summary>
    [Export] public float MaxThrust { get; set; } = 400f;

    /// <summary>Temps (s) pour passer de 0 à 100 % de poussée.</summary>
    [Export] public float SpoolUpTime { get; set; } = 0.5f;

    /// <summary>Temps (s) pour passer de 100 % à 0 % de poussée.</summary>
    [Export] public float SpoolDownTime { get; set; } = 0.4f;

    /// <summary>Débattement maximal du gimbal (degrés).</summary>
    [Export] public float GimbalDegrees { get; set; } = 15f;

    /// <summary>Chauffe par seconde à pleine poussée. 0 = ne chauffe jamais.</summary>
    [Export] public float HeatPerSecond { get; set; } = 0f;

    /// <summary>Refroidissement par seconde.</summary>
    [Export] public float CoolPerSecond { get; set; } = 0.3f;

    /// <summary>Multiplicateur de poussée pendant la surchauffe.</summary>
    [Export] public float OverheatThrustFactor { get; set; } = 0.3f;

    /// <summary>Autonomie (s) à pleine poussée. 0 = infinie.</summary>
    [Export] public float FuelSeconds { get; set; } = 0f;

    /// <summary>Part du réservoir rechargée par seconde quand le moteur ne pousse pas.</summary>
    [Export] public float FuelRegenPerSecond { get; set; } = 0.2f;

    /// <summary>Couleur des fumées d'échappement.</summary>
    [Export] public Color ExhaustColor { get; set; } = new Color(0.3f, 0.3f, 0.3f);

    /// <summary>Couleur de la flamme.</summary>
    [Export] public Color FlameColor { get; set; } = new Color(1f, 0.55f, 0.15f);

    /// <summary>Résistance à la foudre, 0..1 : 0 = le moteur se coupe à chaque coup, 1 = jamais.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float LightningResistance { get; set; } = 0f;
}
