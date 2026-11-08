# Mission M9 : Effets des nuages sur le vol (perturbation, orage, HUD)

## Objectif
Faire des nuages un élément de gameplay ressenti. Dans un nuage, l'engin subit une traînée accrue et des turbulences. Dans un cœur d'orage, des éclairs frappent : flash, secousse, impulsion et moteurs coupés un instant. Le HUD indique l'état (dans le nuage / caché, orage, ascendance, repéré).

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement :
  - `Scripts/Clouds/CloudField.cs` : `CloudField.Instance`, `Sample(Vector3)` qui renvoie un `CloudSample` (`Density` 0..1, `Storm` 0..1, `Updraft` 0..1, `Sdf` en m), `UpdraftSpeed`, `TriggerLightning(Vector3)`, `IsBaked`.
  - `Scripts/Vehicles/Vehicle.cs` : `Mass`, `LinearVelocity`, `ApplyCentralForce`, `ApplyCentralImpulse`, `ApplyAngularAcceleration(Vector3)`, `Modules` (chaque `VehicleModule` a `Enabled` et `MaxPower`, > 0 pour un moteur).
  - `Scripts/Core/IVehicleTracker.cs` : le nœud reçoit le véhicule courant via `Target` (groupe `vehicle_trackers`).
  - `Scripts/Core/Detection.cs` : `Detection.IsSpotted` (vrai si un projecteur voit le joueur).
  - `Scripts/Camera/ChaseCamera.cs` et `Scripts/Debug/DebugHud.cs`.
- Les ascendances sont **déjà** appliquées : `CloudField` est une source de vent (`IWindSource`). Ne les réapplique pas.

## Fichiers concernés
- Créer : `Scripts/Clouds/CloudEffects.cs`
- Modifier : `Scripts/Camera/ChaseCamera.cs`. Ajoute uniquement une méthode publique `Shake(float amplitude, float duration)`, sans changer le reste.
- Modifier : `Scripts/Debug/DebugHud.cs`. Ajoute uniquement une ligne d'état « nuage » dans le panneau principal.
- Ne pas toucher : `CloudField.cs`, `CloudRenderer.cs`, les shaders, `Vehicle.cs`, les scènes `.tscn` (le chef de projet ajoutera le nœud à `Main.tscn`).

## Étapes

### 1. ChaseCamera.Shake
`public void Shake(float amplitude, float duration)` : réutilise le mécanisme de secousse existant (`_shakeTime`), avec une amplitude et une durée propres. Garde la plus forte si une secousse est déjà en cours. La secousse de la nitro doit continuer à fonctionner.

### 2. CloudEffects
Namespace `Bomba.Clouds`, `public partial class CloudEffects : Node, IVehicleTracker` (pas `[Tool]`).
- Rejoint `IVehicleTracker.GroupName` dans `_Ready`. `Target` est une propriété auto.
- Exports (avec `[ExportGroup]`) :
  - Perturbation : `CloudDrag = 0.6f` (1/s, à densité 1), `TurbulenceTorque = 2.5f` (rad/s²), `TurbulenceForce = 5f` (m/s²), `TurbulenceFrequency = 1.3f` (Hz).
  - Orage : `StormThreshold = 0.25f`, `StrikeRate = 0.35f` (éclairs par seconde à orage 1), `StrikeImpulse = 9f` (m/s), `StrikeSpin = 2.5f` (rad/s), `StallDuration = 0.8f` (s), `AmbientStrikeRate = 0.25f` (éclairs décoratifs par seconde), `AmbientStrikeRadius = 700f` (m).
  - `HiddenDensity = 0.25f` : au-delà, le joueur est considéré caché.
- État public en lecture : `float Density`, `float Storm`, `float Updraft` (m/s, soit `Sample().Updraft * UpdraftSpeed`), `bool InCloud` (densité > 0,02), `bool Hidden` (densité ≥ `HiddenDensity`), `bool Stalled`.
- `_PhysicsProcess(delta)` (si `Target` est valide et que `CloudField.Instance?.IsBaked` est vrai) :
  1. Échantillonne en `Target.GlobalPosition` et met à jour l'état.
  2. **Traînée** : `Target.ApplyCentralForce(-Target.LinearVelocity * Density * CloudDrag * Target.Mass)`.
  3. **Turbulence**, si `Density > 0.02` : un `FastNoiseLite` (Perlin, fréquence `TurbulenceFrequency`) échantillonné au temps sur 6 canaux décalés donne deux `Vector3` dans [-1, 1]. Applique `Target.ApplyAngularAcceleration(v1 * TurbulenceTorque * Density)` et `Target.ApplyCentralForce(v2 * TurbulenceForce * Density * Target.Mass)`.
  4. **Foudre sur le joueur**, si `Storm > StormThreshold` : avec une probabilité `StrikeRate * Storm * delta` par frame :
     - appelle `CloudField.Instance.TriggerLightning(position + direction aléatoire × 15 à 40 m)` ;
     - applique `ApplyCentralImpulse(direction aléatoire biaisée vers le haut × StrikeImpulse × Mass)` et une accélération angulaire aléatoire de `StrikeSpin / delta`, sur une seule frame ;
     - **coupure moteur** : mémorise les modules moteurs (`MaxPower > 0`) qui sont `Enabled`, les désactive, puis les réactive au bout de `StallDuration` (seulement ceux que tu as désactivés). `Stalled = true` pendant ce temps ;
     - appelle `Shake(0.8f, 0.4f)` sur le premier nœud `ChaseCamera` du groupe `IVehicleTracker.GroupName`.
  5. **Éclairs décoratifs** : avec une probabilité `AmbientStrikeRate * delta`, tire jusqu'à 8 points aléatoires dans un rayon `AmbientStrikeRadius` autour du joueur, à une altitude de 60 à 200 m. Si l'un d'eux a `Storm > 0.5`, appelle `TriggerLightning` dessus.
- Si `Target` change pendant une coupure, réactive immédiatement les modules de l'ancien véhicule (s'il est encore valide).

### 3. DebugHud : ligne d'état nuage
Dans le panneau principal, sous l'état de vol, ajoute un `Label` mis à jour chaque frame. `DebugHud` trouve `CloudEffects` dans le groupe des trackers (premier nœud de type `CloudEffects`), sans dépendance dure : si absent, la ligne est vide. Texte :
- `REPÉRÉ !` en rouge si `Detection.IsSpotted`, sinon :
- `ORAGE ⚡` en violet si `Storm > 0.25`, sinon `CACHÉ (nuage)` en bleu clair si `Hidden`, sinon `Dans le nuage` en gris si `InCloud`, sinon rien ;
- ajoute ` · Ascendance +X m/s` si `Updraft > 1`, et ` · MOTEURS COUPÉS` si `Stalled`.

## Contraintes
- Aucune modification de `Vehicle` ni de `CloudField`. Si une API manque, signale-le.
- Toute la logique dans `_PhysicsProcess`, pas d'accès à `Input`.

## Critères de succès
- [ ] `dotnet build` passe.
- [ ] `GODOT --headless --path . res://Scenes/Debug/FlightProbe.tscn` tourne sans erreur. `CloudEffects` n'est pas encore dans la scène, la sonde ne doit donc pas changer.
- [ ] Les modules désactivés par la foudre sont toujours réactivés, y compris au changement de véhicule.

## Retour attendu
Liste des fichiers créés ou modifiés, un résumé de 3 lignes maximum, les hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher.
