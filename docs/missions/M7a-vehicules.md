# Mission M7a : Les 3 véhicules graybox

## Objectif
Créer trois scènes de véhicules jouables, composées d'un `Vehicle` et de modules : **Le Rafiot** (bateau volant à propulseurs charbon), **Le Zeppelin** (montgolfière, hélice et voile) et **La Guêpe** (rotor et fusée). Puis les brancher dans `Scenes/Main.tscn`.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis ces scripts pour connaître les noms exacts des propriétés exportées :
  - `Scripts/Vehicles/Vehicle.cs` et `Scripts/Vehicles/VehicleModule.cs` (`Channel`, `Enabled`)
  - `Scripts/Vehicles/Modules/Thruster.cs`, `Propeller.cs`, `Balloon.cs`, `Sail.cs`, `AeroSurface.cs`
  - `Resources/Thrusters/Charbon.tres`, `Fusee.tres`
- Les modules créent **eux-mêmes** leur visuel (propulseur, hélice, ballon, voile). Tu ne dessines que la coque et la cabine.
- Valeurs d'enum dans les `.tscn` (entiers) :
  - `InputChannel` : None = 0, Throttle = 1, Vertical = 2, Trim = 3, Always = 4.
  - `BalloonMode` : Gas = 0, HotAir = 1.
- Axes : l'avant du véhicule est **-Z**, le haut **+Y** et la droite **+X**. Les propulseurs poussent selon leur -Z local, donc ils sont orientés comme le véhicule, sans rotation.
- **Placement dans les `.tscn`** : n'écris PAS de `transform = Transform3D(...)`. Utilise `position = Vector3(x, y, z)` et, si besoin, `rotation_degrees = Vector3(x, y, z)`.
- Format `.tscn` sans uid. Les propriétés C# exportées s'écrivent avec leur nom C# exact (ex. `DisplayName = "Le Rafiot"`). Les propriétés natives de Godot s'écrivent en snake_case (ex. `mass = 20.0`, `gravity_scale = 1.6`, `angular_damp = 1.5`).

## Fichiers concernés
- Créer : `Scenes/Vehicles/Rafiot.tscn`, `Scenes/Vehicles/Zeppelin.tscn`, `Scenes/Vehicles/Guepe.tscn`
- Modifier : `Scenes/Main.tscn`. Ajoute uniquement les 3 `ext_resource` `PackedScene` et, sur le nœud `Main`, la propriété `VehicleScenes = [ExtResource("..."), ExtResource("..."), ExtResource("...")]` (dans l'ordre Rafiot, Zeppelin, Guêpe). Mets à jour `load_steps`.
- Ne pas toucher : tous les fichiers `.cs`, `project.godot`, `.tres`.

## Étapes

### Structure commune d'une scène
```
[gd_scene load_steps=N format=3]

[ext_resource type="Script" path="res://Scripts/Vehicles/Vehicle.cs" id="1_vehicle"]
[ext_resource type="Script" path="res://Scripts/Vehicles/Modules/Thruster.cs" id="2_thruster"]
... autres scripts et ressources

[sub_resource type="BoxShape3D" id="BoxShape3D_hull"]
size = Vector3(3, 2.2, 9)

[sub_resource type="StandardMaterial3D" id="Mat_hull"]
albedo_color = Color(0.25, 0.5, 0.45, 1)
roughness = 0.8

[node name="Rafiot" type="RigidBody3D"]
script = ExtResource("1_vehicle")
mass = 20.0
...

[node name="Collision" type="CollisionShape3D" parent="."]
shape = SubResource("BoxShape3D_hull")

[node name="Hull" type="CSGBox3D" parent="."]
size = Vector3(3, 1.6, 8)
material = SubResource("Mat_hull")

[node name="ThrusterL" type="Node3D" parent="."]
position = Vector3(-1.6, 0, 4.6)
script = ExtResource("2_thruster")
Profile = ExtResource("3_charbon")
```
Pour les visuels de coque, utilise des `CSGBox3D` et `CSGCylinder3D` (propriétés `size`, `radius`, `height`, `material`), et des `MeshInstance3D` + `PrismMesh` pour une proue pointue. Aucune collision sur les visuels : la collision passe uniquement par les `CollisionShape3D`.

### 1. Le Rafiot (`Rafiot.tscn`), le bateau volant de référence
- Racine `Rafiot` (RigidBody3D + Vehicle) : `DisplayName = "Le Rafiot"`, `mass = 20.0`, `gravity_scale = 1.6`, `angular_damp = 1.5`, `CenterOfMassOffset = Vector3(0, -0.5, 0)`, `ForwardDrag = 0.012`, `HoverAssist = 0.35`.
- Collision : boîte `(3, 2.2, 9)`.
- Visuels :
  - Coque : boîte `(3, 1.6, 8)` bleu-vert `(0.25, 0.5, 0.45)`.
  - Bande basse : boîte `(3.05, 0.6, 8.05)` à `y = -0.6`, jaune sale `(0.6, 0.5, 0.2)`.
  - Proue : `PrismMesh` à `z = -4.8`, pointe vers l'avant (rotation à ajuster pour que la pointe soit en -Z), même couleur que la coque.
  - Cabine : boîte `(2.2, 1.3, 2.6)` à `(0, 1.4, 1.5)`, brune `(0.35, 0.25, 0.18)`.
  - Toit : boîte `(2.6, 0.2, 3)` à `(0, 2.15, 1.5)`, rouille `(0.45, 0.3, 0.2)`.
- Modules :
  - `ThrusterL` : Thruster à `(-1.6, 0, 4.6)`, `Profile = Charbon.tres`.
  - `ThrusterR` : Thruster à `(1.6, 0, 4.6)`, `Profile = Charbon.tres`.
  - `HullLift` : AeroSurface à `(0, 0, -0.5)`, `LiftCoefficient = 0.2`, `MaxLift = 450.0`, `UnpoweredLiftFactor = 0.15`, `DragCoefficient = 0.02`.
  - `Rudder` : AeroSurface à `(0, 1.2, 4.2)`, `rotation_degrees = Vector3(0, 0, 90)` (sa normale devient latérale), `LiftCoefficient = 0.0`, `ControlStrength = 0.03`, `DragCoefficient = 0.005`.
  - `Elevator` : AeroSurface à `(0, 0.3, 4.3)`, `LiftCoefficient = 0.0`, `ControlStrength = 0.03`, `DragCoefficient = 0.005`.

### 2. Le Zeppelin (`Zeppelin.tscn`) : lent, flottant, sensible au vent
- Racine `Zeppelin` : `DisplayName = "Le Zeppelin"`, `mass = 30.0`, `gravity_scale = 1.0`, `angular_damp = 2.0`, `CenterOfMassOffset = Vector3(0, -1, 0)`, `ForwardDrag = 0.03`, `LateralGripPowered = 1.0`, `VerticalGripPowered = 0.6`, `MaxTurnRates = Vector3(0.5, 0.6, 0.8)`, `RollAssist = 3.0`, `AutoBankDegrees = 10.0`, `AirWeight = 0.3`, `HoverAssist = 0.0`.
- Collisions : une boîte pour la nacelle `(2.4, 1.6, 6)`, et une sphère de rayon 4 à `(0, 6, 0)` pour le ballon (deux `CollisionShape3D`).
- Visuels : nacelle en boîte `(2.4, 1.4, 6)` en bois `(0.45, 0.32, 0.2)`, avec 4 cordages (`CSGCylinder3D` de rayon 0,05) qui la relient au ballon, approximativement entre `(±1, 0.7, ±2.5)` et `(±1.5, 3, ±2)`.
- Modules :
  - `Envelope` : Balloon à `(0, 6, 0)`, `Mode = 1` (HotAir), `Channel = 2` (Vertical), `LiftForce = 490.0`, `BaseHeat = 0.6`, `HeatChangeTime = 3.0`, `AirDrag = 8.0`, `Radius = 4.0`.
  - `Propeller` : Propeller à `(0, 0, 3.4)`, `MaxThrust = 250.0`, `MaxEffectiveSpeed = 30.0`, `BladeRadius = 1.4`.
  - `Sail` : Sail à `(0, 0.8, -1.5)`, `SailCoefficient = 3.0`, `MaxForce = 500.0`, `Size = Vector2(3.5, 3.5)`.
  - `Keel` : AeroSurface à `(0, -0.5, 0)`, `LiftCoefficient = 0.0`, `DragCoefficient = 0.05`.

### 3. La Guêpe (`Guepe.tscn`) : légère et nerveuse
- Racine `Guepe` : `DisplayName = "La Guêpe"`, `mass = 10.0`, `gravity_scale = 1.4`, `angular_damp = 2.0`, `CenterOfMassOffset = Vector3(0, -0.4, 0)`, `ForwardDrag = 0.015`, `PowerWeight = 3.0`, `MaxTurnRates = Vector3(1.8, 1.8, 2.5)`, `HoverAssist = 0.0`.
- Collision : boîte `(1.6, 1.4, 4.5)`.
- Visuels : fuselage en boîte `(1.4, 1.2, 4)` jaune `(0.85, 0.65, 0.1)` avec bandes noires (deux boîtes fines), cockpit en sphère CSG `(0, 0.6, -1)` de rayon 0,6, et un mât de rotor (cylindre fin) de `y = 0.6` à `y = 1.5`.
- Modules :
  - `Rotor` : Propeller à `(0, 1.6, 0)`, `rotation_degrees = Vector3(90, 0, 0)` (sa poussée -Z pointe alors vers le haut), `Channel = 2` (Vertical), `MaxThrust = 220.0`, `BaseLevel = 0.62`, `MaxEffectiveSpeed = 25.0`, `BladeRadius = 2.2`, `SpoolTime = 0.3`.
  - `Rocket` : Thruster à `(0, 0, 2.4)`, `Profile = Fusee.tres`.
  - `Body` : AeroSurface à `(0, 0, 0)`, `LiftCoefficient = 0.05`, `MaxLift = 120.0`, `DragCoefficient = 0.01`.

### 4. Main.tscn
Ajoute les trois scènes à `VehicleScenes` sur le nœud `Main`, sans rien changer d'autre.

## Contraintes
- Ne modifie aucun `.cs`. Si une propriété citée ici n'existe pas sous ce nom dans le script, utilise le nom réel et signale l'écart.
- Pas de `uid=` dans les fichiers.

## Critères de succès
- [ ] Les 3 `.tscn` existent, avec un `RigidBody3D` racine portant `Vehicle.cs`.
- [ ] Chaque nom de propriété exportée utilisé existe réellement dans le script correspondant : vérifie-le en relisant les `.cs`.
- [ ] `Main.tscn` référence les 3 scènes dans `VehicleScenes`.
- [ ] `dotnet build` passe toujours.

## Retour attendu
Liste des fichiers créés ou modifiés, un résumé de 3 lignes maximum, les écarts de noms de propriétés constatés, les hypothèses faites. Pas de commit.

## Si bloqué
Pour un choix visuel non couvert ici, prends le plus simple et signale-le.
