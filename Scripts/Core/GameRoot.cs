using Bomba.Vehicles;
using Godot;

namespace Bomba.Core;

/// <summary>
/// Racine de la scène principale : fait apparaître le véhicule, permet d'en changer (Tab)
/// et de le respawner (R). Les nœuds du groupe <see cref="IVehicleTracker.GroupName"/>
/// reçoivent le véhicule courant à chaque spawn.
/// </summary>
public partial class GameRoot : Node3D
{
	/// <summary>Instance de la racine, valide tant que la scène est dans l'arbre.</summary>
	public static GameRoot Instance { get; private set; }

	/// <summary>Scènes de véhicules parcourues par Tab. Vide : avertissement, aucun véhicule.</summary>
	[Export] public Godot.Collections.Array<PackedScene> VehicleScenes { get; set; } = new();

	/// <summary>Point d'apparition et de respawn. Si null, apparition à 60 m au-dessus de l'origine.</summary>
	[Export] public Marker3D SpawnPoint { get; set; }

	/// <summary>Véhicule courant, ou null.</summary>
	public Vehicle CurrentVehicle { get; private set; }

	/// <summary>Index dans <see cref="VehicleScenes"/> du véhicule courant.</summary>
	public int CurrentIndex { get; private set; }

	public override void _EnterTree() => Instance = this;

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _Ready() => CallDeferred(MethodName.SpawnVehicle, 0);

	public override void _Process(double delta)
	{
		if (Input.IsActionJustPressed(GameActions.SwitchVehicle))
			SpawnVehicle(CurrentIndex + 1);

		if (Input.IsActionJustPressed(GameActions.Respawn))
			RespawnCurrent();
	}

	/// <summary>Remplace le véhicule courant par celui d'index donné (ramené dans la liste par modulo).</summary>
	public void SpawnVehicle(int index)
	{
		int count = VehicleScenes.Count;
		if (count == 0)
		{
			GD.PushWarning("Aucune scène de véhicule dans GameRoot.VehicleScenes");
			return;
		}

		index = ((index % count) + count) % count;

		if (CurrentVehicle != null && IsInstanceValid(CurrentVehicle))
			CurrentVehicle.QueueFree();
		CurrentVehicle = null;
		CurrentIndex = index;

		PackedScene scene = VehicleScenes[index];
		if (scene == null)
		{
			GD.PushError($"VehicleScenes[{index}] est vide.");
			return;
		}

		Node node = scene.Instantiate();
		if (node is not Vehicle vehicle)
		{
			GD.PushError($"La scène VehicleScenes[{index}] n'a pas de racine Vehicle.");
			node.QueueFree();
			return;
		}

		AddChild(vehicle);
		vehicle.Respawn(GetSpawnTransform());
		CurrentVehicle = vehicle;

		foreach (Node tracker in GetTree().GetNodesInGroup(IVehicleTracker.GroupName))
		{
			if (tracker is IVehicleTracker t)
				t.Target = vehicle;
		}
	}

	/// <summary>Respawn du véhicule courant au point d'apparition.</summary>
	public void RespawnCurrent()
	{
		if (CurrentVehicle != null && IsInstanceValid(CurrentVehicle))
			CurrentVehicle.Respawn(GetSpawnTransform());
	}

	private Transform3D GetSpawnTransform() =>
		SpawnPoint != null
			? SpawnPoint.GlobalTransform
			: new Transform3D(Basis.Identity, new Vector3(0f, 60f, 0f));
}
