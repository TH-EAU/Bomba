# Mission M5 : Caméra de poursuite, HUD de debug et gizmos de force

## Objectif
Rendre le vol lisible : une caméra de poursuite fluide (FOV dynamique, secousse à la nitro), un HUD (vitesse, altitude, état, jauges) avec un mode debug sur F1, et l'affichage des vecteurs de force de chaque module.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement :
  - `Scripts/Vehicles/Vehicle.cs` : propriétés publiques `DisplayName`, `Speed`, `ForwardSpeed`, `PowerRatio`, `Authority`, `State` (`FlightState.Powered/Nitro/Ballistic`), `NitroGauge`, `NitroActive`, `Modules`, `GlobalCenterOfMass`, `Forward`, et signaux `NitroStarted` / `NitroEnded`.
  - `Scripts/Vehicles/VehicleModule.cs` : `Enabled` (modifiable), `LastForce` (force globale de la dernière frame), `Name`.
  - `Scripts/Core/IVehicleTracker.cs` : interface à implémenter, plus le groupe `IVehicleTracker.GroupName` à rejoindre dans `_Ready`. Le véhicule est assigné **à l'exécution** via `Target`, et peut changer à tout moment (changement de véhicule) ou valoir null.
  - `Scripts/Core/InputBootstrap.cs` : `GameActions.ToggleDebug` (F1).
- L'interpolation physique est activée : pour suivre un `RigidBody3D` dans `_Process`, utilise `Target.GetGlobalTransformInterpolated()`.
- D'autres missions écrivent en parallèle les classes `Thruster`, `Propeller`, `Balloon`, `Sail` et `AeroSurface`. **Ne les référence pas en C#** : pour distinguer les modules, utilise `module.GetType().Name`.

## Fichiers concernés
- Créer : `Scripts/Debug/DebugSettings.cs`
- Créer : `Scripts/Camera/ChaseCamera.cs`
- Créer : `Scripts/Debug/DebugHud.cs`
- Créer : `Scripts/Debug/ForceGizmos.cs`
- Ne pas toucher : tout autre fichier.

## Étapes

### DebugSettings
Namespace `Bomba.DebugTools`, `public static class DebugSettings { public static bool ShowDebug; }` (false par défaut).

### ChaseCamera
Namespace `Bomba.Cameras`, `public partial class ChaseCamera : Camera3D, IVehicleTracker`.
- Exports : `Distance = 14f`, `Height = 4f`, `LookAheadTime = 0.25f`, `PositionSharpness = 8f`, `RotationSharpness = 5f`, `BaseFov = 70f`, `SpeedFovBonus = 15f`, `SpeedForMaxFov = 60f`, `NitroFovKick = 12f`, `FovSharpness = 4f`, `NitroShakeAmplitude = 0.35f`, `NitroShakeDuration = 0.35f`.
- `Target` est une propriété avec un champ privé. Dans le setter : déconnecte `NitroStarted` de l'ancien véhicule (s'il est valide), connecte le nouveau (`vehicle.NitroStarted += OnNitroStarted`), puis repositionne la caméra immédiatement derrière la cible (pas de lissage au changement).
- `_Ready` : rejoint le groupe, `Current = true`, `Fov = BaseFov`.
- `_Process(delta)` (si `Target` est valide) :
  1. `t = Target.GetGlobalTransformInterpolated()`, `pos = t.Origin`, `fwd = -t.Basis.Z.Normalized()`.
  2. Direction suivie : si `Target.State == FlightState.Ballistic` et `Target.LinearVelocity.Length() > 2`, alors `follow = fwd.Lerp(Target.LinearVelocity.Normalized(), 0.6f).Normalized()` (en chute, on suit la trajectoire plutôt que le nez qui tourne). Sinon `follow = fwd`.
  3. `_smoothedDir` lissé vers `follow` avec `k = 1 - Mathf.Exp(-RotationSharpness * delta)`, puis renormalisé.
  4. Position voulue : `pos - _smoothedDir * Distance + Vector3.Up * Height`. On l'atteint par lissage exponentiel (`PositionSharpness`).
  5. Point visé : `pos + Target.LinearVelocity * LookAheadTime`. Utilise `LookAt(point, Vector3.Up)`, sauf si la direction de visée est quasi parallèle à `Vector3.Up` (produit scalaire absolu > 0,98) : dans ce cas, utilise `_smoothedDir` comme vecteur up.
  6. FOV visé : `BaseFov + SpeedFovBonus * clamp(Target.Speed / SpeedForMaxFov, 0, 1) + (Target.NitroActive ? NitroFovKick : 0)`. On l'atteint par lissage avec `FovSharpness`.
  7. Secousse : si `_shakeTime > 0`, ajoute à la position un décalage aléatoire d'amplitude `NitroShakeAmplitude * (_shakeTime / NitroShakeDuration)` et décrémente `_shakeTime`. N'applique pas la secousse au point lissé, sinon elle s'accumule : garde la position lissée dans un champ séparé.
- `OnNitroStarted()` : `_shakeTime = NitroShakeDuration`.
- `_ExitTree` : déconnecte le signal.

### DebugHud
Namespace `Bomba.DebugTools`, `public partial class DebugHud : CanvasLayer, IVehicleTracker`. L'interface est construite **entièrement en code** dans `_Ready`.
- **Panneau principal** (en haut à gauche, toujours visible), `PanelContainer` avec un fond semi-transparent et un `VBoxContainer` :
  - Nom du véhicule (`DisplayName`), en grand.
  - Vitesse en m/s et en km/h, altitude (`GlobalPosition.Y`, en m).
  - État en français, en couleur : « PROPULSÉ » vert, « NITRO » orange, « BALISTIQUE » rouge.
  - Trois `ProgressBar` (0..1) avec libellé : Puissance (`PowerRatio`), Autorité (`Authority`), Nitro (`NitroGauge`).
- **Panneau détail** (sous le principal, visible seulement si `DebugSettings.ShowDebug`) : un `Label` multiligne listant chaque module de `Target.Modules` avec son index (1 à 9), son type (`GetType().Name`), son nom, l'indication « ON » ou « OFF » selon `Enabled`, et `LastForce.Length()` en N (sans décimale).
- **Aide des contrôles** (en bas à gauche, petit texte, toujours visible) :
  « Poussée Z/W · Frein S · Lacet Q/D · Tangage ↑↓ · Roulis A/E · Vertical Espace/Ctrl · Voile W/C · Nitro Maj · Véhicule Tab · Respawn R · Debug F1 (1-9 : couper un module) ». Les touches sont physiques : indique les deux lettres AZERTY/QWERTY quand elles diffèrent, comme ci-dessus (Z/W = Z en AZERTY, W en QWERTY).
- `_Process` :
  - Si `Input.IsActionJustPressed(GameActions.ToggleDebug)`, inverse `DebugSettings.ShowDebug`.
  - Met à jour les textes et les barres si `Target` est valide. Sinon, affiche « Aucun véhicule ».
- `_UnhandledInput(InputEvent e)` : si `DebugSettings.ShowDebug` et que `e` est un `InputEventKey` pressé (non répété) dont le `PhysicalKeycode` va de `Key.Key1` à `Key.Key9`, inverse `Enabled` du module d'index correspondant (s'il existe).
- `_Ready` : rejoint le groupe `IVehicleTracker.GroupName`.

### ForceGizmos
Namespace `Bomba.DebugTools`, `public partial class ForceGizmos : Node3D, IVehicleTracker`.
- `_Ready` : rejoint le groupe, `TopLevel = true`, `GlobalTransform = Transform3D.Identity`. Crée un enfant `MeshInstance3D` avec un `ImmediateMesh`, et un matériau `StandardMaterial3D` avec `ShadingMode = Unshaded`, `VertexColorUseAsAlbedo = true`, `NoDepthTest = true` et `Transparency = Disabled`. Mets `CastShadow = Off` sur l'instance.
- Export `MetersPerWeight = 4f` : une force égale au poids du véhicule (`Mass * 9.8`) est dessinée sur 4 m.
- `_Process` : `mesh.ClearSurfaces()`. Si `!DebugSettings.ShowDebug` ou si `Target` n'est pas valide, on s'arrête là. Sinon, `SurfaceBegin(Mesh.PrimitiveType.Lines, material)`, puis :
  - Pour chaque module : un trait de `module.GlobalPosition` à `module.GlobalPosition + module.LastForce * scale`, avec `scale = MetersPerWeight / (Target.Mass * 9.8f)`. Couleur selon `GetType().Name` : Thruster orange, Propeller jaune, Balloon cyan, Sail blanc, AeroSurface vert, autre magenta. Ajoute une petite croix (3 traits de 0,3 m) à la position du module.
  - La vitesse : un trait bleu depuis `Target.GlobalCenterOfMass`, de longueur `LinearVelocity * 0.2`.
  - Le centre de masse : une croix rouge de 0,6 m.
  - `SurfaceEnd()`. Si aucun sommet n'a été ajouté, n'appelle ni `SurfaceBegin` ni `SurfaceEnd` (un ImmediateMesh vide plante sinon).

## Contraintes
- Aucune référence C# aux classes de modules concrètes (Thruster, etc.).
- Ne modifie ni `Vehicle`, ni `VehicleModule`, ni les fichiers `Core`.
- Toujours tester `IsInstanceValid(Target)` avant usage : le véhicule peut être libéré lors d'un changement.
- Commentaires XML courts en français sur les membres publics.

## Critères de succès
- [ ] `dotnet build` passe sans erreur dans tes fichiers.
- [ ] Les 3 nœuds implémentent `IVehicleTracker` et rejoignent le groupe dans `_Ready`.
- [ ] Changer `Target` en cours de jeu ne laisse aucun signal connecté sur l'ancien véhicule.

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, les problèmes rencontrés ou hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher. Pour un choix non couvert ici, prends l'option la plus simple et signale-la.
