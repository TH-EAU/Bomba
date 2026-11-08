# Mission M2 : Propulseurs (Thruster + ThrusterProfile)

## Objectif
Créer le module `Thruster` (propulseur à gimbal), sa ressource de réglage `ThrusterProfile` et 3 profils `.tres` : Charbon, Vapeur, Fusée.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md` (sections « Le projet » et « Conventions »).
- Lis attentivement ces fichiers existants, ils définissent le contrat à respecter :
  - `Scripts/Vehicles/VehicleModule.cs` : classe de base. Tu dois en hériter, implémenter `Simulate(float delta, float command)`, et appliquer les forces UNIQUEMENT via `ApplyForceAtSelf(Vector3 globalForce)`.
  - `Scripts/Vehicles/Vehicle.cs` : fournit `ThrustMultiplier` (nitro) et `GetGimbalDirection(baseDirection, globalPosition, maxAngleRadians)`.
  - `Scripts/Vehicles/InputChannel.cs`, `Scripts/Vehicles/VehicleInput.cs`.
- Le propriétaire `Vehicle` peut être `null` (module monté sur un simple `RigidBody3D`). Gère ce cas : pas de gimbal, multiplicateur 1.

## Fichiers concernés
- Créer : `Scripts/Vehicles/Modules/ThrusterProfile.cs`
- Créer : `Scripts/Vehicles/Modules/Thruster.cs`
- Créer : `Resources/Thrusters/Charbon.tres`, `Resources/Thrusters/Vapeur.tres`, `Resources/Thrusters/Fusee.tres`
- Ne pas toucher : tout autre fichier, en particulier `Scripts/Vehicles/*.cs`, `Scripts/Core/*.cs` et `project.godot`.

## Étapes
1. `ThrusterProfile.cs`, namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class ThrusterProfile : Resource`. Propriétés `[Export]` (propriétés auto `{ get; set; }`) avec ces valeurs par défaut :
   - `string DisplayName = "Propulseur"`
   - `float MaxThrust = 400f` (Newtons)
   - `float SpoolUpTime = 0.5f` (secondes pour passer de 0 à 100 %)
   - `float SpoolDownTime = 0.4f` (secondes pour passer de 100 % à 0)
   - `float GimbalDegrees = 15f`
   - `float HeatPerSecond = 0f` (chauffe par seconde à pleine poussée ; 0 = ne chauffe jamais)
   - `float CoolPerSecond = 0.3f`
   - `float OverheatThrustFactor = 0.3f` (multiplicateur de poussée quand il est en surchauffe)
   - `float FuelSeconds = 0f` (autonomie à pleine poussée ; 0 = infinie)
   - `float FuelRegenPerSecond = 0.2f` (part du réservoir rechargée par seconde quand il ne pousse pas)
   - `Color ExhaustColor = new Color(0.3f, 0.3f, 0.3f)`
   - `Color FlameColor = new Color(1f, 0.55f, 0.15f)`
2. `Thruster.cs`, namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class Thruster : VehicleModule`.
   - Exports : `ThrusterProfile Profile`, `bool UseGimbal = true`, `bool CreateDefaultVisual = true`.
   - État public en lecture : `float SpoolLevel` (0..1), `float Heat` (0..1), `bool Overheated`, `float Fuel` (0..1, démarre à 1).
   - `public override float MaxPower => Profile?.MaxThrust ?? 0f;`
   - `public override float CurrentPower => SpoolLevel * MaxPower;` (sans le multiplicateur nitro)
   - Dans `_Ready()`, appelle d'abord `base._Ready()`. Si `Profile` est null, crée `new ThrusterProfile()` et émets un `GD.PushWarning`.
   - `Simulate(delta, command)` :
     1. `target = Mathf.Clamp(command, 0, 1)`. Si `FuelSeconds > 0` et `Fuel <= 0`, alors `target = 0`.
     2. `SpoolLevel = Mathf.MoveToward(SpoolLevel, target, delta / (target > SpoolLevel ? SpoolUpTime : SpoolDownTime))`, en protégeant contre les durées ≤ 0 : une durée nulle donne un changement instantané.
     3. Chaleur : `Heat += SpoolLevel * HeatPerSecond * delta - CoolPerSecond * delta`, bornée entre 0 et 1. Si `Heat >= 1`, alors `Overheated = true`. Si `Overheated` et `Heat <= 0.4`, alors `Overheated = false` (hystérésis).
     4. Carburant (si `FuelSeconds > 0`) : `Fuel -= SpoolLevel * delta / FuelSeconds`. S'il ne pousse pas (`target == 0`), `Fuel += FuelRegenPerSecond * delta`. Borne entre 0 et 1.
     5. `thrust = SpoolLevel * MaxThrust * (Overheated ? OverheatThrustFactor : 1) * (Vehicle?.ThrustMultiplier ?? 1)`.
     6. Direction : `dir = -GlobalBasis.Z.Normalized()`. Si `UseGimbal && Vehicle != null`, alors `dir = Vehicle.GetGimbalDirection(dir, GlobalPosition, Mathf.DegToRad(GimbalDegrees))`.
     7. Si `thrust > 0`, appelle `ApplyForceAtSelf(dir * thrust)`.
   - Visuel (créé en code dans `_Ready` si `CreateDefaultVisual`) :
     - Un corps : `MeshInstance3D` avec `CylinderMesh` (rayon 0,35, hauteur 1,2), tourné pour que son axe soit Z (rotation X de 90°), avec un matériau métal sombre (`StandardMaterial3D`, Albedo gris-brun, Metallic 0,6).
     - Une flamme : `MeshInstance3D` avec un `CylinderMesh` conique (TopRadius 0, BottomRadius 0,3), placée derrière (+Z local), avec un matériau non éclairé émissif de couleur `FlameColor`. Chaque frame (`_Process`), son échelle en Z suit `SpoolLevel` (multipliée par 1,8 si `Vehicle?.NitroActive` est vrai), et elle est cachée si `SpoolLevel < 0,02`.
     - Une fumée : `CpuParticles3D` placé à +Z local, qui émet vers +Z local. `Emitting = SpoolLevel > 0.05`, couleur `ExhaustColor`, mesh `SphereMesh` de petite taille (rayon 0,25) avec un matériau non éclairé, transparent et à couleur de sommet (`VertexColorUseAsAlbedo`). `LocalCoords = false` pour que la fumée reste dans le monde.
3. Les 3 `.tres` (format texte Godot 4, sans uid), sur ce modèle :
   ```
   [gd_resource type="Resource" script_class="ThrusterProfile" load_steps=2 format=3]

   [ext_resource type="Script" path="res://Scripts/Vehicles/Modules/ThrusterProfile.cs" id="1"]

   [resource]
   script = ExtResource("1")
   DisplayName = "Charbon"
   MaxThrust = 450.0
   ...
   ```
   - **Charbon** : MaxThrust 450, SpoolUpTime 1.2, SpoolDownTime 0.6, GimbalDegrees 15, HeatPerSecond 0, FuelSeconds 0, ExhaustColor sombre (0.12, 0.1, 0.1), FlameColor (1, 0.45, 0.1). Il est lent à démarrer et très puissant.
   - **Vapeur** : MaxThrust 320, SpoolUpTime 0.5, SpoolDownTime 0.4, GimbalDegrees 20, HeatPerSecond 0.25, CoolPerSecond 0.3, OverheatThrustFactor 0.3, FuelSeconds 0, ExhaustColor blanc (0.9, 0.9, 0.9), FlameColor (1, 0.8, 0.5).
   - **Fusée** (fichier `Fusee.tres`, DisplayName « Fusée à poudre ») : MaxThrust 650, SpoolUpTime 0.05, SpoolDownTime 0.05, GimbalDegrees 10, HeatPerSecond 0, FuelSeconds 3, FuelRegenPerSecond 0.2, ExhaustColor (0.55, 0.5, 0.45), FlameColor (1, 0.6, 0.2).
   - Les `Color` s'écrivent `Color(r, g, b, 1)` dans les `.tres`.

## Contraintes
- Pas d'accès à `Input` dans le module : seule la valeur `command` compte.
- Ne pas modifier `VehicleModule` ni `Vehicle`. S'il manque quelque chose dans ces classes, signale-le dans ton retour.
- Commentaires XML courts (`///`) en français sur les membres publics.

## Critères de succès
- [ ] `dotnet build` à la racine passe sans erreur dans tes fichiers.
- [ ] `Thruster` hérite de `VehicleModule` et n'applique de force que via `ApplyForceAtSelf`.
- [ ] Les 3 `.tres` existent et référencent bien le script par son chemin `res://`.

## Retour attendu
- Liste des fichiers créés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés ou hypothèses faites.
- Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans le modifier. Pour une décision de design non couverte ici, choisis l'option la plus simple et signale-la.
