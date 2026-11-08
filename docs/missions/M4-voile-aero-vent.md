# Mission M4 : Voile, surfaces aérodynamiques et zones de vent

## Objectif
Créer trois éléments : `Sail` (voile orientable poussée par le vent relatif), `AeroSurface` (portance « soufflée » qui s'effondre moteurs coupés, avec option gouvernail) et `WindZone` (zone de rafale locale qui s'ajoute au vent global).

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement ces fichiers, ils définissent le contrat :
  - `Scripts/Vehicles/VehicleModule.cs` : classe de base. Hérite-en, implémente `Simulate(float delta, float command)`, applique les forces UNIQUEMENT via `ApplyForceAtSelf`. `GetPointVelocity()` donne la vitesse du point de montage. `Vehicle` peut être null.
  - `Scripts/Vehicles/Vehicle.cs` : fournit `PowerRatio` (0..1), `Authority` (0..1), `GlobalCenterOfMass` et `GetDesiredTorqueAxis()` (axe de rotation global voulu par le joueur).
  - `Scripts/Core/Wind.cs` : `Wind.GetWindAt(pos)` (statique), `Wind.Instance`, `Register(IWindSource)` et `Unregister(IWindSource)`, ainsi que l'interface `IWindSource`.

## Fichiers concernés
- Créer : `Scripts/Vehicles/Modules/Sail.cs`
- Créer : `Scripts/Vehicles/Modules/AeroSurface.cs`
- Créer : `Scripts/Core/WindZone.cs`
- Ne pas toucher : tout autre fichier.

## Étapes

### AeroSurface
Namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class AeroSurface : VehicleModule`.
C'est une surface (coque, aile, gouvernail). Sa **normale** est son axe +Y local, et son **axe avant** est -Z local.
- Dans `_Ready`, après `base._Ready()` : si `Channel == InputChannel.Throttle` (valeur par défaut), mets `Channel = InputChannel.Always`. Le canal n'est pas utilisé.
- Exports (propriétés auto) :
  - `float LiftCoefficient = 0.15f` (N par (m/s)²)
  - `float MaxLift = 600f` (N)
  - `float UnpoweredLiftFactor = 0.15f` (part de la portance qui reste moteurs coupés)
  - `float DragCoefficient = 0.02f` (N par (m/s)², pour une surface vue de face)
  - `float ControlStrength = 0f` (0 = surface portante pure ; > 0 = gouvernail, en N par (m/s)²)
  - `float MinControlSpeed = 3f` (m/s)
- `Simulate` :
  1. `air = GetPointVelocity() - Wind.GetWindAt(GlobalPosition)`, `normal = GlobalBasis.Y.Normalized()`, `fwd = -GlobalBasis.Z.Normalized()`.
  2. `s = Mathf.Max(0, air.Dot(fwd))` (vitesse avant).
  3. `blow = Vehicle != null ? Mathf.Lerp(UnpoweredLiftFactor, 1, Vehicle.PowerRatio) : UnpoweredLiftFactor`.
  4. `lift = normal * Mathf.Min(LiftCoefficient * s * s, MaxLift) * blow`.
  5. Traînée de plaque : `speed = air.Length()`. Si `speed > 0.01`, alors `exposure = 0.1 + 0.9 * Mathf.Abs(normal.Dot(air / speed))` et `drag = -air * speed * DragCoefficient * exposure`. Sinon `drag = 0`.
  6. Gouvernail (si `ControlStrength > 0 && Vehicle != null && s > MinControlSpeed`) :
     - `T = Vehicle.GetDesiredTorqueAxis()`, `r = GlobalPosition - Vehicle.GlobalCenterOfMass`.
     - `wanted = T.Cross(r)` (la force qui produit un couple selon T). Si `wanted.LengthSquared() > 1e-6` :
     - `control = normal * normal.Dot(wanted.Normalized()) * Mathf.Min(T.Length(), 1) * ControlStrength * s * s * Vehicle.Authority`.
  7. `ApplyForceAtSelf(lift + drag + control)`.

### Sail
Namespace `Bomba.Vehicles.Modules`, `[GlobalClass] public partial class Sail : VehicleModule`.
La voile pivote autour de son axe Y local (le mât). Au repos, son plan contient les axes Y et Z locaux, donc sa normale est l'axe +X local.
- Dans `_Ready`, après `base._Ready()` : si `Channel == InputChannel.Throttle`, mets `Channel = InputChannel.Trim`. Mémorise la `Basis` locale de repos (`_restBasis = Basis`).
- Exports :
  - `float SailCoefficient = 0.6f` (N par (m/s)²)
  - `float MaxForce = 500f`
  - `float MaxAngleDegrees = 70f`
  - `float TurnSpeedDegrees = 90f` (degrés par seconde)
  - `Vector2 Size = new Vector2(4f, 5f)` (largeur, hauteur)
  - `bool CreateDefaultVisual = true`
- État : `float AngleDegrees`.
- `Simulate` :
  1. `targetAngle = Mathf.Clamp(command, -1, 1) * MaxAngleDegrees`, puis `AngleDegrees = Mathf.MoveToward(AngleDegrees, targetAngle, TurnSpeedDegrees * delta)`.
  2. `Basis = _restBasis * new Basis(Vector3.Up, Mathf.DegToRad(AngleDegrees))`. Le module pivote lui-même, son visuel enfant suit.
  3. `w = Wind.GetWindAt(GlobalPosition) - GetPointVelocity()` (vent apparent). Si `w.LengthSquared() < 0.01`, on s'arrête là.
  4. `n = GlobalBasis.X.Normalized()`, `wHat = w.Normalized()`.
  5. `magnitude = Mathf.Min(SailCoefficient * w.LengthSquared() * Mathf.Abs(n.Dot(wHat)), MaxForce)`.
  6. `force = n * Mathf.Sign(n.Dot(wHat)) * magnitude`.
  7. `ApplyForceAtSelf(force)`.
- Visuel (si `CreateDefaultVisual`) : un mât (`CylinderMesh` fin, hauteur `Size.Y + 1`, couleur bois) et une toile (`BoxMesh` de taille `(0.05, Size.Y, Size.X)`, couleur toile (0.85, 0.8, 0.65), `CullMode = Disabled`). La toile est décalée vers l'arrière de `Size.X / 2` en +Z, pour que le mât soit au bord avant.

### WindZone
Namespace `Bomba.Core`, `[GlobalClass] public partial class WindZone : Node3D, IWindSource`.
- Exports :
  - `Vector3 WindVelocity = new Vector3(0, 12, 0)` (m/s, global)
  - `float Radius = 40f`
  - `float PulsePeriod = 4f` (s ; 0 = constant)
  - `bool ShowVisual = true`
- `_EnterTree` : `Wind.Instance?.Register(this)`. `_ExitTree` : `Wind.Instance?.Unregister(this)`.
  Comme l'autoload Wind est créé avant la scène, `Wind.Instance` existe normalement. Si l'instance est null au moment de `_EnterTree`, réessaie dans `_Ready`.
- `GetWindContribution(pos)` : `d = pos.DistanceTo(GlobalPosition)`. Si `d >= Radius`, renvoie `Vector3.Zero`. Sinon, `falloff = 1 - d / Radius`, `pulse = PulsePeriod > 0 ? 0.6f + 0.4f * Mathf.Sin(_time * Mathf.Tau / PulsePeriod) : 1f`, et renvoie `WindVelocity * falloff * pulse`. `_time` est incrémenté dans `_PhysicsProcess`.
- Visuel (si `ShowVisual`) : un `CpuParticles3D` qui émet dans une sphère de rayon `Radius * 0.8`, avec des particules allongées (`BoxMesh` 0.05 × 0.05 × 1.5, blanc semi-transparent, non éclairé), direction = `WindVelocity` normalisé, vitesse = `WindVelocity.Length()`, durée de vie 2 s et environ 60 particules. Ajoute aussi une sphère `MeshInstance3D` très transparente (alpha 0,06, non éclairée, `CullMode = Disabled`) de rayon `Radius` pour voir la zone.

## Contraintes
- Pas d'accès à `Input`.
- Ne modifie ni `VehicleModule`, ni `Vehicle`, ni `Wind`. S'il manque quelque chose, signale-le.
- Commentaires XML courts en français sur les membres publics.

## Critères de succès
- [ ] `dotnet build` passe sans erreur dans tes fichiers.
- [ ] Les trois modules n'appliquent leurs forces que via `ApplyForceAtSelf`. `WindZone` implémente `IWindSource`.
- [ ] `AeroSurface` et `Sail` fonctionnent avec `Vehicle == null` (tests null présents).

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, les problèmes rencontrés ou hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher. Pour un choix non couvert ici, prends l'option la plus simple et signale-la.
