# Mission M6 : Arène de test, GameRoot et scène principale

## Objectif
Avoir une scène principale jouable : une arène générée en code (ciel, mer, îlots flottants, anneaux de parcours, zone de mort), un `GameRoot` qui fait apparaître le véhicule, permet d'en changer et gère le respawn, et `Scenes/Main.tscn` qui assemble le tout.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`. `project.godot` déclare déjà `run/main_scene="res://Scenes/Main.tscn"`.
- Lis attentivement :
  - `Scripts/Vehicles/Vehicle.cs` : `Respawn(Transform3D)`.
  - `Scripts/Core/IVehicleTracker.cs` : tous les nœuds du groupe `IVehicleTracker.GroupName` doivent recevoir le véhicule courant via `Target`.
  - `Scripts/Core/InputBootstrap.cs` : `GameActions.SwitchVehicle` (Tab) et `GameActions.Respawn` (R).
  - `Scripts/Vehicles/PlayerVehicleController.cs`.
- D'autres missions écrivent en parallèle `Scripts/Camera/ChaseCamera.cs` (classe `ChaseCamera : Camera3D`), `Scripts/Debug/DebugHud.cs` (`DebugHud : CanvasLayer`) et `Scripts/Debug/ForceGizmos.cs` (`ForceGizmos : Node3D`). **Ne les référence pas en C#.** Ne les utilise que dans `Main.tscn`, par leur chemin de script. Le lien avec le véhicule passe par le groupe `IVehicleTracker`.
- Les scènes de véhicules n'existent pas encore (mission suivante). `GameRoot` doit fonctionner avec une liste vide : un avertissement, pas de crash.

## Fichiers concernés
- Créer : `Scripts/Core/GameRoot.cs`
- Créer : `Scripts/Arena/KillZone.cs`, `Scripts/Arena/Ring.cs`, `Scripts/Arena/ArenaBuilder.cs`
- Créer : `Scenes/Main.tscn`
- Ne pas toucher : tout autre fichier, en particulier `project.godot`.

## Étapes

### GameRoot
Namespace `Bomba.Core`, `public partial class GameRoot : Node3D`.
- `public static GameRoot Instance { get; private set; }`, défini dans `_EnterTree` et remis à null dans `_ExitTree`.
- Exports : `Godot.Collections.Array<PackedScene> VehicleScenes` (initialisé à un tableau vide), `Marker3D SpawnPoint`.
- `public Vehicle CurrentVehicle { get; private set; }`, `public int CurrentIndex { get; private set; }`.
- `_Ready` : `CallDeferred(MethodName.SpawnVehicle, 0)`.
- `public void SpawnVehicle(int index)` :
  - Si la liste est vide : `GD.PushWarning("Aucune scène de véhicule dans GameRoot.VehicleScenes")` et on s'arrête.
  - `index` est ramené dans la liste par modulo positif. Libère le véhicule courant s'il existe (`QueueFree`).
  - Instancie la scène. Si la racine n'est pas un `Vehicle`, émets `GD.PushError` et libère l'instance.
  - `AddChild`, puis `GlobalTransform = SpawnPoint.GlobalTransform` (ou l'identité décalée de +60 en Y si `SpawnPoint` est null), puis `ResetPhysicsInterpolation()` sur le véhicule.
  - Pour chaque nœud de `GetTree().GetNodesInGroup(IVehicleTracker.GroupName)` qui est un `IVehicleTracker`, assigne `Target = CurrentVehicle`.
- `public void RespawnCurrent()` : appelle `CurrentVehicle?.Respawn(transform du spawn)`.
- `_Process` : `Input.IsActionJustPressed(GameActions.SwitchVehicle)` déclenche `SpawnVehicle(CurrentIndex + 1)`, et `GameActions.Respawn` déclenche `RespawnCurrent()`.

### KillZone
Namespace `Bomba.Arena`, `public partial class KillZone : Area3D`.
- Export `Vector3 Size = new Vector3(6000, 20, 6000)`. Dans `_Ready`, crée un `CollisionShape3D` avec un `BoxShape3D` de cette taille, et connecte `BodyEntered`.
- Sur `BodyEntered(Node3D body)` : si `body is Vehicle`, appelle `GameRoot.Instance?.CallDeferred(GameRoot.MethodName.RespawnCurrent)`.

### Ring
Namespace `Bomba.Arena`, `public partial class Ring : Area3D`. C'est un anneau de parcours dont l'axe de passage est le Z local.
- `[Signal] public delegate void PassedEventHandler(Ring ring);`
- Exports : `float RingRadius = 9f`, `float TubeRadius = 0.6f`, `float ResetDelay = 4f`.
- `_Ready` :
  - Visuel : `MeshInstance3D` + `TorusMesh` (`InnerRadius = RingRadius - TubeRadius`, `OuterRadius = RingRadius + TubeRadius`), tourné de 90° sur X pour que son axe passe de Y à Z. Matériau jaune-laiton émissif léger.
  - Détection : `CollisionShape3D` + `CylinderShape3D` (`Radius = RingRadius - TubeRadius`, `Height = 1.5`), tourné de 90° sur X.
  - Connecte `BodyEntered`.
- Sur passage d'un `Vehicle` : la couleur passe au vert, le signal `Passed` est émis, et la couleur revient au jaune après `ResetDelay` secondes (`GetTree().CreateTimer`).
- Mets aussi un vrai cadre solide : un `StaticBody3D` enfant avec, en `CollisionShape3D`, une approximation de l'anneau par 12 petites `SphereShape3D` (rayon `TubeRadius`) réparties sur le cercle. Le véhicule peut ainsi heurter le bord.

### ArenaBuilder
Namespace `Bomba.Arena`, `public partial class ArenaBuilder : Node3D`. Il construit toute l'arène en code dans `_Ready`.
- Exports : `int Seed = 1337`, `float SeaLevel = -60f`, `int IslandCount = 28`, `float ArenaRadius = 700f`, `float SpawnClearRadius = 90f`, `int RingCount = 12`, `float RingCourseRadius = 260f`.
- **Environnement** : `WorldEnvironment` avec un `Environment` (`BackgroundMode = Sky`) et un `Sky` à `ProceduralSkyMaterial` aux couleurs de fin de journée « punk » : haut du ciel bleu-ardoise (0.25, 0.35, 0.5), horizon orangé (0.85, 0.55, 0.35), sol sombre. Active `TonemapMode = Aces`, le glow, et un brouillard léger (`FogEnabled`, densité 0,0015, couleur orangée pâle). Ajoute une `DirectionalLight3D` rasante et chaude (rotation à environ -35° en X et 40° en Y), avec ombres activées.
- **Mer** : `MeshInstance3D` + `PlaneMesh` de 8000 × 8000 à `y = SeaLevel`, matériau bleu-vert sombre (0.08, 0.2, 0.25), `Metallic` 0,4, `Roughness` 0,15.
- **KillZone** : instancie `new KillZone()`, positionnée à `y = SeaLevel` (en ajouter une instance suffit, sa forme se crée dans son propre `_Ready`).
- **Îlots** (`RandomNumberGenerator` avec `Seed`) : `IslandCount` îlots placés à une distance horizontale aléatoire entre `SpawnClearRadius` et `ArenaRadius` de l'origine, à une altitude Y entre -10 et 160, et d'un rayon `r` entre 12 et 45. Chacun est un `StaticBody3D` contenant :
  - un plateau : `SphereMesh` (`Radius = r`, `Height = r * 0.7`), couleur roche brune (0.35, 0.28, 0.22), et un `CollisionShape3D` dont la forme vaut `mesh.CreateConvexShape()` ;
  - une pointe dessous : `CylinderMesh` (`TopRadius = r * 0.8`, `BottomRadius = 0.5`, `Height = r * 1.4`) décalée vers le bas de `r * 0.7`, avec sa propre forme convexe ;
  - un dessus d'herbe : `CylinderMesh` plat (`TopRadius = BottomRadius = r * 0.85`, `Height = 0.6`), vert olive (0.3, 0.4, 0.2), posé sur le plateau (`y = r * 0.33`), sans collision.
- **Parcours d'anneaux** : `RingCount` anneaux répartis sur un cercle de rayon `RingCourseRadius` autour de l'origine, à une altitude de `60 + 35 * sin(angle * 2)`. Chaque anneau est orienté selon la tangente au cercle : son axe Z suit la direction du parcours (`LookAt` vers la position suivante une fois ajouté à l'arbre, ou basis calculée).

### Scenes/Main.tscn
Écris le fichier texte à la main, au format Godot 4 sans uid :
```
[gd_scene load_steps=7 format=3]

[ext_resource type="Script" path="res://Scripts/Core/GameRoot.cs" id="1_root"]
[ext_resource type="Script" path="res://Scripts/Arena/ArenaBuilder.cs" id="2_arena"]
[ext_resource type="Script" path="res://Scripts/Vehicles/PlayerVehicleController.cs" id="3_ctrl"]
[ext_resource type="Script" path="res://Scripts/Camera/ChaseCamera.cs" id="4_cam"]
[ext_resource type="Script" path="res://Scripts/Debug/DebugHud.cs" id="5_hud"]
[ext_resource type="Script" path="res://Scripts/Debug/ForceGizmos.cs" id="6_giz"]

[node name="Main" type="Node3D"]
script = ExtResource("1_root")
SpawnPoint = NodePath("SpawnPoint")

[node name="Arena" type="Node3D" parent="."]
script = ExtResource("2_arena")

[node name="SpawnPoint" type="Marker3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 60, 0)

[node name="PlayerController" type="Node" parent="."]
script = ExtResource("3_ctrl")

[node name="ChaseCamera" type="Camera3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 64, 14)
script = ExtResource("4_cam")

[node name="DebugHud" type="CanvasLayer" parent="."]
script = ExtResource("5_hud")

[node name="ForceGizmos" type="Node3D" parent="."]
script = ExtResource("6_giz")
```
Ne mets pas encore de `VehicleScenes`. La mission suivante les ajoutera.

## Contraintes
- Aucune référence C# à `ChaseCamera`, `DebugHud`, `ForceGizmos`, ni aux modules (`Thruster`, `Balloon`…).
- Ne modifie ni `project.godot`, ni les fichiers de `Scripts/Vehicles/`, ni les autres fichiers de `Scripts/Core/`.
- Commentaires XML courts en français sur les membres publics.

## Critères de succès
- [ ] `dotnet build` passe sans erreur dans tes fichiers.
- [ ] `Scenes/Main.tscn` existe, avec exactement la structure ci-dessus.
- [ ] `GameRoot` ne plante pas avec une liste de véhicules vide.
- [ ] Les îlots ne sont jamais à moins de `SpawnClearRadius` (à l'horizontale) de l'origine.

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, les problèmes rencontrés ou hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher. Pour un choix non couvert ici, prends l'option la plus simple et signale-la.
