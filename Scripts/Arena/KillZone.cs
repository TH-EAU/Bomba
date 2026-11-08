using Bomba.Core;
using Bomba.Vehicles;
using Godot;

namespace Bomba.Arena;

/// <summary>Zone de mort : un véhicule qui y entre est renvoyé au point d'apparition.</summary>
public partial class KillZone : Area3D
{
    /// <summary>Dimensions de la zone (m), centrée sur le nœud.</summary>
    [Export] public Vector3 Size { get; set; } = new Vector3(6000f, 20f, 6000f);

    public override void _Ready()
    {
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Size } });
        BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is Vehicle)
            GameRoot.Instance?.CallDeferred(GameRoot.MethodName.RespawnCurrent);
    }
}
