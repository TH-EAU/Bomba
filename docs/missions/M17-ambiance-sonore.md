# Mission : ambiance sonore météo (pluie, grêle, vent, grincements)

## Objectif
Le joueur entend la pluie, la grêle et le vent, en mélanges de niveaux selon l'intensité, et le véhicule grince quand le vent et les rafales forcissent.

## Contexte
- Sons, en MP3 :
  - `res://Resources/Sounds/Rain and hail/rain-light.mp3`, `rain-heavy.mp3`, `hail.mp3` ;
  - `res://Resources/Sounds/Wind/wind-light.mp3`, `wind-medium.mp3`, `wind-heavy.mp3` ;
  - `res://Resources/Sounds/Squeak/low-boat-squeak.mp3`, `medium-boat-squeak.mp3`.
- `Scripts/Clouds/StormWeather.cs` (à lire, **ne pas modifier**) : `StormWeather.Instance` (peut être nul), `RainAt(Vector3)`, `HailAt(Vector3)`, `StormWind01(Vector3)`, `Turbulence01(Vector3)`, tous dans 0..1.
- `Scripts/Vehicles/Vehicle.cs` : `AirVelocity` (vitesse relative à l'air, vent compris, en m/s) et `AngularVelocity` (RigidBody3D).
- Interface `Bomba.Core.IVehicleTracker` (groupe `IVehicleTracker.GroupName`, propriété `Vehicle Target`) : exemple dans `Scripts/Effects/ScreenDroplets.cs`.

## Fichiers concernés
- Créer : `Scripts/Audio/WeatherAmbience.cs`
- Ne pas toucher : tout le reste, en particulier `Scenes/**` (le chef de projet ajoutera le nœud dans `Main.tscn`), `StormWeather.cs`, `LightningSystem.cs`.

## Étapes
1. `public partial class WeatherAmbience : Node, IVehicleTracker`. Au `_Ready`, s'ajouter au groupe et créer des `AudioStreamPlayer` enfants (non positionnels) pour les boucles : pluie légère, pluie forte, grêle, vent léger, vent moyen, vent fort. Charger chaque MP3 avec `GD.Load<AudioStreamMP3>`, forcer `Loop = true`, démarrer à `VolumeDb = -80`, jouer en continu.
2. Exports, groupe « Volumes » (dB à pleine intensité) : `RainVolumeDb` -4, `HailVolumeDb` -2, `WindVolumeDb` -6, `SqueakVolumeDb` -6, plus `Bus` ("Master").
3. Exports, groupe « Vent » :
   - `WindLightSpeed` 8, `WindMediumSpeed` 35, `WindHeavySpeed` 70 : vitesses ressenties (m/s) où chaque niveau est maximal ;
   - `StormWindBoost` 40 : m/s ajoutés au ressenti à `StormWind01` = 1.
4. Exports, groupe « Grincements » : `SqueakRate` (0.6 coup par seconde à stress 1), `SqueakMinInterval` (1.2 s), `SqueakMediumStress` (0.55), `SqueakSpinWeight` (0.3 : part de la rotation dans le stress).
5. Export `Sharpness` (2) : lissage exponentiel de tous les volumes.
6. `_Process` (si `Target` est valide, sinon tous les volumes cibles à 0) :
   - `p = Target.GlobalPosition` ;
   - **pluie** : `r = RainAt(p)` ; fondu à puissance constante entre léger et fort : léger `sqrt(1-r)*r`, fort `sqrt(r)*r` (le facteur r éteint tout quand il ne pleut pas) ;
   - **grêle** : gain `HailAt(p)` ;
   - **vent** : ressenti `v = AirVelocity.Length() + StormWind01(p) * StormWindBoost`. Gains triangulaires entre les trois niveaux : léger maximal à `WindLightSpeed` (et croissant depuis 0), moyen à `WindMediumSpeed`, fort à `WindHeavySpeed` et au-delà. Les deux niveaux voisins se partagent le gain à puissance constante (racine). Sous `WindLightSpeed`, le gain léger vaut `v / WindLightSpeed` ;
   - **conversion** : gain linéaire → dB avec `Mathf.LinearToDb(max(gain, 0.0001))` + le volume de la couche ; puis lissage de `VolumeDb` vers la cible (`Mathf.Lerp` avec `1 - exp(-Sharpness*dt)`) ;
   - **grincements** :
     - `stress = clamp(max(StormWind01(p), Turbulence01(p)) + SqueakSpinWeight * AngularVelocity.Length() / 2, 0, 1)` ;
     - à chaque frame, si au moins `SqueakMinInterval` s'est écoulé depuis le dernier, tirer avec la probabilité `stress * SqueakRate * dt` ;
     - si oui, jouer un `AudioStreamPlayer` jetable (libéré sur `Finished`) avec le grincement léger, ou le moyen si `stress > SqueakMediumStress` (le moyen tiré une fois sur deux) ;
     - volume `SqueakVolumeDb + LinearToDb(0.4 + 0.6*stress)`, `PitchScale` aléatoire 0.9 à 1.1 ;
     - grincements sans boucle (copie du flux avec `Loop = false` : `Duplicate()` du AudioStreamMP3).
7. Commentaire de classe (en français) qui explique le mélange.

## Contraintes
- Code et commentaires en français, style existant, exports groupés (voir CLAUDE.md).
- Si `dotnet build` échoue sur un fichier d'une autre mission (travail en parallèle), le signaler sans le corriger.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] Une scène de test temporaire (Node racine + un nœud `WeatherAmbience`), lancée en headless 120 frames, ne sort ni `ERROR` ni `Exception` (le pilote audio factice est normal). La supprimer ensuite.

## Retour attendu
- Fichiers créés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
