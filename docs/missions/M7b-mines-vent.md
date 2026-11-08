# Mission M7b : Mines flottantes et zones de vent dans l'arène

## Objectif
Créer une mine flottante (une bombe suspendue sous un ballon) qui flotte **uniquement grâce au module `Balloon` existant** et qui explose au contact d'un véhicule. Puis peupler l'arène de mines et de zones de rafale.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis :
  - `Scripts/Vehicles/Modules/Balloon.cs` : propriétés `Mode`, `LiftForce`, `AirDrag`, `Radius`, `FabricColor`, `Channel` (héritée). Un ballon `Gas` avec le canal `Always` pousse en permanence, même sans `Vehicle`.
  - `Scripts/Vehicles/VehicleModule.cs` : un module trouve son corps porteur (le premier `RigidBody3D` ancêtre) dans son `_Ready`.
  - `Scripts/Vehicles/Vehicle.cs`.
  - `Scripts/Core/WindZone.cs` : propriétés `WindVelocity`, `Radius`, `PulsePeriod`.
  - `Scripts/Arena/ArenaBuilder.cs` : le générateur d'arène, que tu vas compléter.

## Fichiers concernés
- Créer : `Scripts/Arena/FloatingMine.cs`
- Modifier : `Scripts/Arena/ArenaBuilder.cs`. Ajoute seulement deux étapes de génération à la fin de la construction, plus leurs exports.
- Ne pas toucher : tout autre fichier.

## Étapes

### FloatingMine
Namespace `Bomba.Arena`, `public partial class FloatingMine : RigidBody3D`. Il se construit entièrement en code dans `_Ready`.
- Exports : `float TetherLength = 4f`, `float AnchorSlack = 5f`, `float AnchorStiffness = 6f` (en m/s² par mètre de dépassement), `float AnchorDamping = 1.5f`, `float ExplosionImpulse = 25f` (changement de vitesse en m/s infligé au véhicule), `float RespawnDelay = 8f`.
- `_Ready` :
  1. `Mass = 3`, `GravityScale = 1`, `LinearDamp = 0.5f`, `AngularDamp = 2f`, `ContactMonitor = true`, `MaxContactsReported = 4`, `CanSleep = false`.
  2. Mémorise `_anchor = GlobalPosition`. Les mines sont ajoutées à l'arbre par ArenaBuilder avec leur position déjà définie : fais cette mémorisation dans `_Ready`, pas avant.
  3. Corps : `MeshInstance3D` + `SphereMesh` de rayon 0,9, métal rouillé (Albedo (0.45, 0.4, 0.35), Metallic 0,8, Roughness 0,5). Ajoute 8 pointes en `CylinderMesh` coniques (TopRadius 0, BottomRadius 0,15, Height 0,6), réparties sur la sphère, chacune orientée vers l'extérieur. Collision : `CollisionShape3D` + `SphereShape3D` de rayon 1,1.
  4. Câble : `MeshInstance3D` + `CylinderMesh` fin (rayon 0,03, hauteur `TetherLength`), centré à `y = TetherLength / 2`, de couleur sombre.
  5. Ballon : `new Balloon { Mode = BalloonMode.Gas, Channel = InputChannel.Always, LiftForce = Mass * 9.8f * 1.05f, AirDrag = 3f, Radius = 1.4f, FabricColor = new Color(0.7f, 0.2f, 0.15f) }`, à `Position = new Vector3(0, TetherLength + 1.4f, 0)`, puis `AddChild`. Pas de collision pour le ballon.
  6. Connecte `BodyEntered`.
- `_PhysicsProcess` : rappel vers l'ancre (le câble est attaché à un point fixe invisible). Avec `offset = GlobalPosition - _anchor` et `d = offset.Length()`, si `d > AnchorSlack`, alors `ApplyCentralForce((-offset / d * (d - AnchorSlack) * AnchorStiffness - LinearVelocity * AnchorDamping) * Mass)`.
- `BodyEntered(Node body)` : si `body is Vehicle v` et que la mine n'est pas déjà explosée :
  - `dir = (v.GlobalPosition - GlobalPosition).Normalized() + Vector3.Up * 0.3f`, normalisé.
  - `v.ApplyCentralImpulse(dir * ExplosionImpulse * v.Mass)`, et un léger couple aléatoire : `v.ApplyTorqueImpulse(random_unit * v.Mass * 3)`.
  - Effet visuel : une `OmniLight3D` orange (énergie 8, portée 25) et une sphère émissive orange qui grossit de 0,5 à 6 en 0,3 s puis disparaît (`CreateTween`). Ces deux nœuds sont ajoutés au **parent** de la mine, à sa position, et libérés à la fin du tween.
  - La mine se désactive : `Visible = false`, `Freeze = true`, collision désactivée (`CollisionShape3D.Disabled` via `SetDeferred`). Après `RespawnDelay` secondes (`GetTree().CreateTimer`), elle revient à `_anchor` avec une vitesse nulle (`PhysicsServer3D.BodySetState` comme dans `Vehicle.Respawn`), redevient visible, `Freeze = false`, et sa collision est réactivée.

### ArenaBuilder : deux étapes ajoutées
- Nouveaux exports : `int MineCount = 14`, `int WindZoneCount = 5`.
- **Mines** (même `RandomNumberGenerator` que les îlots, ou un second initialisé avec `Seed + 1`) : `MineCount` mines à une distance horizontale de 120 à 500 de l'origine, à une altitude de 20 à 110. Positionne-les (`Position`) AVANT `AddChild`.
- **Zones de vent** : `WindZoneCount` instances de `WindZone`, à une distance de 150 à 550 et une altitude de 30 à 120, avec `Radius` entre 35 et 60. Leur `WindVelocity` est :
  - pour la moitié d'entre elles, une colonne ascendante : `(0, 14, 0)` ;
  - pour les autres, un vent latéral horizontal de 15 m/s, dans une direction aléatoire.

## Contraintes
- La mine n'a **aucun code de flottaison** : elle flotte uniquement grâce à `Balloon`. C'est le but de la démo.
- Ne modifie ni `Balloon`, ni `Vehicle`, ni `WindZone`. Dans `ArenaBuilder`, ne change pas les étapes existantes.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] `FloatingMine` ne contient aucune force verticale « magique » : seul le rappel d'ancre au-delà du mou, plus le Balloon.
- [ ] Les mines et les zones de vent sont générées par `ArenaBuilder`.

## Retour attendu
Liste des fichiers créés ou modifiés, un résumé de 3 lignes maximum, les problèmes rencontrés ou hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. Pour un choix non couvert ici, prends l'option la plus simple et signale-la.
