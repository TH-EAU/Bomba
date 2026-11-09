# Mission : caméra cockpit et pilotage à la souris (réticule)

## Objectif
Par défaut, le joueur voit depuis le poste de pilotage (juste derrière le tableau de bord) et dirige l'engin à la souris : un réticule à l'écran agit comme un gouvernail (droite/gauche = lacet, haut/bas = tangage). Le clavier et la manette continuent de fonctionner.

## Contexte
- `Scripts/Camera/ChaseCamera.cs` (Camera3D, `IVehicleTracker`) : caméra de poursuite. Elle suit le véhicule courant (`_target`) dans `_Process`, gère le champ de vision (FOV selon la vitesse et le nitro) et propose `Shake(amplitude, duration)`, utilisé par les éclairs et le nitro.
- `Scripts/Vehicles/PlayerVehicleController.cs` : lit l'Input Godot et écrit `Target.CurrentInput` (`VehicleInput` : Throttle, Brake, Pitch, Yaw, Roll, Vertical, Trim, Nitro) dans `_PhysicsProcess`. Pitch > 0 = nez vers le haut, Yaw > 0 = nez vers la droite.
- `Scripts/Core/InputBootstrap.cs` : InputMap déclarée en code (classe `GameActions` pour les noms, méthode `Bind`, touches **physiques** `Key(Godot.Key.X)`). Touches déjà prises : W S Up Down A D Left Right Q E Space Ctrl Z C Shift Tab R F1, et les chiffres 1 à 9 (debug).
- Les véhicules sont des scènes `Scenes/Vehicles/*.tscn` (racine `RigidBody3D`, script `Vehicle`). Un véhicule pourra contenir un nœud enfant **`Marker3D` nommé `CockpitCamera`** (orienté vers -Z, comme une caméra), qui donne la position et l'orientation de l'œil du pilote. Le chef de projet l'ajoute dans `Scenes/Vehicles/CloudSurfer.tscn` en parallèle : ne pas toucher aux scènes de véhicules.

## Fichiers concernés
- Modifier : `Scripts/Camera/ChaseCamera.cs`
- Modifier : `Scripts/Vehicles/PlayerVehicleController.cs`
- Modifier : `Scripts/Core/InputBootstrap.cs` (deux actions)
- Créer : `Scripts/UI/SteeringReticle.cs`
- Ne pas toucher : `Scenes/**` (aucun `.tscn`), `Scripts/Vehicles/Vehicle.cs`, les modules, les nuages, les éclairs, `project.godot`, `.gitignore`.

## Étapes
1. `InputBootstrap.cs` :
   - ajouter `GameActions.CameraToggle = "camera_toggle"`, lié à `Key(Godot.Key.F5)` ;
   - ajouter `GameActions.ReleaseMouse = "release_mouse"`, lié à `Key(Godot.Key.Escape)`.
2. `ChaseCamera.cs`, mode cockpit :
   - ajouter `public enum CameraMode { Cockpit, Chase }` et `[Export] public CameraMode Mode { get; set; } = CameraMode.Cockpit;` (groupe « Mode ») ;
   - ajouter `[Export] public float CockpitFov { get; set; } = 75f;` ;
   - dans `_Process` : si `Mode == Cockpit` et que la cible a un enfant `Marker3D` nommé `CockpitCamera` (le chercher avec `_target.GetNodeOrNull<Marker3D>("CockpitCamera")`, à chaque frame ou en cache par cible), la caméra prend exactement sa `GlobalTransform` (transform interpolée, sans lissage ni anticipation) ; FOV = `CockpitFov` + `NitroFovKick` en nitro, lissé avec `FovSharpness` ; la secousse (`Shake`) reste appliquée en plus ;
   - sinon (mode Chase, ou pas de marqueur), comportement actuel inchangé ;
   - `Near = 0.05f` en mode cockpit, et la valeur d'origine restaurée en mode Chase ;
   - l'action `CameraToggle` (`_UnhandledInput` ou `Input.IsActionJustPressed` dans `_Process`) bascule entre Cockpit et Chase.
3. `PlayerVehicleController.cs`, gouvernail à la souris :
   - au `_Ready`, capturer la souris : `Input.MouseMode = Input.MouseModeEnum.Captured`. L'action `ReleaseMouse` la libère (Visible) ; un clic gauche la recapture ;
   - exports (groupe « Souris ») : `MouseSteering` (bool, true), `MouseRadius` (float, 220 px : course du réticule), `MouseSensitivity` (float, 1), `MouseDeadzone` (float, 0.06, en fraction du rayon), `MouseRecenterSpeed` (float, 0 : le réticule reste où on le laisse) ;
   - dans `_Input(InputEvent e)`, si la souris est capturée et `e` est un `InputEventMouseMotion` : `_mouseOffset += motion.Relative * MouseSensitivity`, puis le borner au disque de rayon `MouseRadius` (si la longueur dépasse le rayon, ramener le vecteur sur le cercle). Un clic molette remet `_mouseOffset` à zéro ;
   - si `MouseRecenterSpeed > 0`, ramener `_mouseOffset` vers zéro de façon exponentielle dans `_Process` ;
   - propriété publique `public Vector2 MouseSteer` = `_mouseOffset / MouseRadius` (dans le disque unité), avec la zone morte : composantes nulles si la longueur est sous `MouseDeadzone`, puis remise à l'échelle de façon continue ;
   - dans `_PhysicsProcess`, quand `MouseSteering` est actif : `Yaw = Clamp(clavier + MouseSteer.X, -1, 1)` et `Pitch = Clamp(clavier + (-MouseSteer.Y), -1, 1)` (souris vers le haut = nez vers le haut ; l'Y écran croît vers le bas). `InvertPitch` s'applique aussi à la part souris. Throttle, Brake, Roll, Vertical, Trim et Nitro restent inchangés.
4. `Scripts/UI/SteeringReticle.cs` : `public partial class SteeringReticle : Control`, dessin dans `_Draw` :
   - plein écran (`SetAnchorsPreset(LayoutPreset.FullRect)`), `MouseFilter = Ignore` ;
   - propriété `public PlayerVehicleController Controller { get; set; }` ;
   - dessin au centre de l'écran : un cercle de course (rayon `MouseRadius`, blanc à 15 % d'opacité, épaisseur 1.5), une petite croix centrale fixe (6 px, 40 %), et le réticule en `centre + MouseSteer * MouseRadius` (cercle de rayon 9 px, 85 %, épaisseur 2, plus un point central de 2 px). Une ligne fine (25 %) relie le centre au réticule ;
   - `QueueRedraw()` dans `_Process` ; rien n'est dessiné si `Controller` est nul ou si `MouseSteering` est faux.
5. `PlayerVehicleController._Ready` crée le réticule en code : un `CanvasLayer` (`Layer = 5`) enfant du contrôleur, et dedans un `SteeringReticle` avec `Controller = this`.

## Contraintes
- Code et commentaires en français, style existant, exports regroupés avec `[ExportGroup]` (voir CLAUDE.md).
- Ne pas changer le comportement du clavier ni de la manette, ni le calcul du Throttle.
- Pas de nouveau fichier `.tscn`, pas de modification de scène.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] `"C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe" --headless --path . --quit-after 300` ne sort ni `ERROR` ni `Exception` (hors fuites à la sortie).
- [ ] `res://Scenes/Debug/FlightProbe.tscn` (même commande, avec la scène en argument) tourne sans erreur.
- [ ] Sans marqueur `CockpitCamera` dans le véhicule, la caméra se comporte exactement comme avant (code relu : la branche Chase n'a pas changé).

## Retour attendu
- Liste des fichiers créés ou modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
