# Mission M3 : Hélice (Propeller) et Ballon (Balloon)

## Objectif
Créer deux modules de vol : `Propeller` (hélice ou rotor, dont la poussée chute avec la vitesse) et `Balloon` (ballon à gaz ou montgolfière, avec poussée d'Archimède appliquée à sa position).

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement ces fichiers, ils définissent le contrat :
  - `Scripts/Vehicles/VehicleModule.cs` : classe de base. Hérite-en, implémente `Simulate(float delta, float command)`, applique les forces UNIQUEMENT via `ApplyForceAtSelf(Vector3 globalForce)`. `GetPointVelocity()` donne la vitesse du point où est monté le module. `Body` est le `RigidBody3D` porteur, et `Vehicle` peut être null.
  - `Scripts/Core/Wind.cs` : `Wind.GetWindAt(Vector3 globalPosition)` (statique) renvoie le vent en m/s.
  - `Scripts/Vehicles/InputChannel.cs` : le canal `Vertical` donne une commande entre -1 et 1, `Throttle` entre 0 et 1, `Always` vaut 1.
- **Important** : `Balloon` sera aussi utilisé sur une mine flottante qui est un simple `RigidBody3D`, sans `Vehicle`. Il doit donc fonctionner avec `Vehicle == null`.

## Fichiers concernés
- Créer : `Scripts/Vehicles/Modules/Propeller.cs`
- Créer : `Scripts/Vehicles/Modules/Balloon.cs`
- Ne pas toucher : tout autre fichier.

## Étapes

### Commande avec niveau de base (utilisée par les deux modules)
Écris cette fonction dans chaque classe, ou dans une petite classe statique `ModuleMath` placée dans `Scripts/Vehicles/Modules/ModuleMath.cs` (à toi de choisir) :
`MapCommand(float command, float baseLevel)` vaut `command >= 0 ? baseLevel + command * (1 - baseLevel) : baseLevel * (1 + command)`, bornée entre 0 et 1.
Avec un niveau de base de 0,5, la commande verticale au repos donne 50 %, la commande à fond 100 %, et la commande inverse à fond 0 %.

### Propeller
Namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class Propeller : VehicleModule`.
- Exports (propriétés auto) :
  - `float MaxThrust = 300f` (N)
  - `float MaxEffectiveSpeed = 45f` (m/s ; vitesse axiale à laquelle la poussée tombe à 0)
  - `float SpoolTime = 0.6f` (s, de 0 à 100 % comme de 100 % à 0)
  - `float BaseLevel = 0f` (0..1, voir MapCommand)
  - `float BladeRadius = 1.2f`
  - `bool CreateDefaultVisual = true`
- État : `float SpinLevel` (0..1).
- `MaxPower => MaxThrust` ; `CurrentPower => SpinLevel * MaxThrust`.
- `Simulate` :
  1. `target = MapCommand(command, BaseLevel)`.
  2. `SpinLevel = Mathf.MoveToward(SpinLevel, target, delta / Mathf.Max(SpoolTime, 0.001f))`.
  3. `dir = -GlobalBasis.Z.Normalized()` (l'hélice pousse selon son -Z local ; un rotor est une hélice tournée pour que -Z pointe vers le haut).
  4. `axial = (GetPointVelocity() - Wind.GetWindAt(GlobalPosition)).Dot(dir)`.
  5. `efficiency = Mathf.Clamp(1 - axial / MaxEffectiveSpeed, 0, 1)`.
  6. `thrust = MaxThrust * SpinLevel * efficiency`. Si `thrust > 0`, appelle `ApplyForceAtSelf(dir * thrust)`.
- Visuel (si `CreateDefaultVisual`, créé dans `_Ready` après `base._Ready()`) : un `Node3D` « Rotor » portant 2 pales en `BoxMesh` (longueur `2*BladeRadius`, largeur 0,25, épaisseur 0,05), croisées à 90°, dans le plan XY local, couleur bois (0.45, 0.3, 0.15). Ajoute un moyeu en petite `SphereMesh`. Dans `_Process`, le rotor tourne autour de l'axe Z local à `SpinLevel * 25` rad/s.

### Balloon
Namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class Balloon : VehicleModule`.
- `public enum BalloonMode { Gas, HotAir }`, déclarée dans le même fichier, namespace `Bomba.Vehicles.Modules`.
- Exports :
  - `BalloonMode Mode = BalloonMode.Gas`
  - `float LiftForce = 200f` (N, poussée à chaleur 1)
  - `float BaseHeat = 0.6f` (HotAir : chaleur au repos, voir MapCommand)
  - `float HeatChangeTime = 2.5f` (HotAir : secondes pour passer de 0 à 1, ce qui donne l'inertie de la montgolfière)
  - `float AirDrag = 6f` (N par m/s, traînée linéaire du ballon par rapport à l'air)
  - `float Radius = 2.5f`
  - `Color FabricColor = new Color(0.75f, 0.65f, 0.45f)`
  - `bool CreateDefaultVisual = true`
- Dans `_Ready`, après `base._Ready()` : si `Mode == Gas` et que le canal vaut `InputChannel.Throttle` (valeur par défaut héritée), mets `Channel = InputChannel.Always` (un ballon à gaz pousse toujours).
- État : `float Heat` (0..1). En mode Gas, Heat vaut toujours 1. En mode HotAir, il démarre à `BaseHeat`.
- `MaxPower` et `CurrentPower` restent à 0 : un ballon n'est pas un moteur, ne les surcharge pas.
- `Simulate` :
  1. Mode Gas : `Heat = 1`. Mode HotAir : `target = MapCommand(command, BaseHeat)`, puis `Heat = Mathf.MoveToward(Heat, target, delta / Mathf.Max(HeatChangeTime, 0.001f))`.
  2. `lift = Vector3.Up * LiftForce * Heat`. C'est le haut GLOBAL et non local : c'est ce qui crée le redressement en pendule.
  3. `airVel = GetPointVelocity() - Wind.GetWindAt(GlobalPosition)`, puis `drag = -airVel * AirDrag`.
  4. `ApplyForceAtSelf(lift + drag)`.
- Visuel (si `CreateDefaultVisual`) : une `SphereMesh` de rayon `Radius` (hauteur `Radius * 2.2`, légèrement ovale), couleur `FabricColor`, rugosité 0,9. En mode HotAir, ajoute un petit brûleur sous le ballon : une `SphereMesh` émissive orange dont l'intensité d'émission suit `Heat` dans `_Process`.

## Contraintes
- Pas d'accès à `Input` : seule la valeur `command` compte.
- Ne modifie ni `VehicleModule`, ni `Vehicle`, ni `Wind`. S'il manque quelque chose, signale-le.
- Commentaires XML courts en français sur les membres publics.

## Critères de succès
- [ ] `dotnet build` passe sans erreur dans tes fichiers.
- [ ] `Balloon` fonctionne avec `Vehicle == null` (aucun accès à `Vehicle` sans test null).
- [ ] La poussée du ballon est en haut global, et celle de l'hélice selon son -Z local.

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, les problèmes rencontrés ou hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher. Pour un choix non couvert ici, prends l'option la plus simple et signale-la.
