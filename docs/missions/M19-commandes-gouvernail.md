# Mission : commandes « incliner et tirer » (souris, clavier, manette), réticule et aide

## Objectif
Le joueur pilote avec peu de commandes :
- **réticule** : X = inclinaison, Y = tangage (haut = cabrer, bas = piquer, tout en bas = aérofrein en plus) ;
- **clavier** : roulis et tangage ;
- poussée, frein et nitro inchangés.
Le réticule affiche la zone de frein et un horizon artificiel.

## Contexte
- Le modèle de vol (`Scripts/Vehicles/Vehicle.cs`, **ne pas modifier**) a changé :
  - `VehicleInput.Roll` (-1..1) est maintenant une **inclinaison demandée** et tenue : 1 = `MaxBankDeg`, soit 180°, sur le dos ; 0 = retour à plat ;
  - `VehicleInput.Pitch` reste une vitesse de tangage (positif = nez vers le haut) ;
  - `VehicleInput.Yaw` n'a plus d'effet ;
  - `Vehicle.Bank` (radians, positif = aile droite basse) donne l'inclinaison courante.
- `Scripts/Vehicles/PlayerVehicleController.cs` : il lit le clavier, la manette et le réticule (`MouseSteer`, un `Vector2` dans le disque unité ; Y écran vers le bas), puis écrit `Target.CurrentInput`. Le réticule reste là où on le laisse ; clic molette pour recentrer.
- `Scripts/UI/SteeringReticle.cs` : dessine le cercle de course, la croix centrale et le réticule (`Controller.MouseSteer`, `Controller.MouseRadius`).
- `Scripts/Core/InputBootstrap.cs` : l'InputMap est déclarée en code (`GameActions`, touches **physiques** `Key(Godot.Key.X)`).
- `Scripts/Debug/DebugHud.cs` : constante `HelpText`.

## Fichiers concernés
- Modifier : `Scripts/Core/InputBootstrap.cs`, `Scripts/Vehicles/PlayerVehicleController.cs`, `Scripts/UI/SteeringReticle.cs`, `Scripts/Debug/DebugHud.cs` (texte d'aide seulement).
- Ne pas toucher : `Scripts/Vehicles/Vehicle.cs`, les modules, `Scripts/Debug/FlightProbe.cs`, les scènes, les nuages et tout le reste.

## Étapes
1. `InputBootstrap.cs` :
   - supprimer les liaisons `YawLeft` et `YawRight`, en gardant les constantes `GameActions` (d'autres fichiers pourraient les citer) ;
   - `RollLeft` : `Key(A)`, `Key(Q)`, `Key(Left)`, `Axis(JoyAxis.LeftX, -1f)` ;
   - `RollRight` : `Key(D)`, `Key(E)`, `Key(Right)`, `Axis(JoyAxis.LeftX, 1f)` ;
   - garder les boutons d'épaule : plus de bouton de roulis dédié, ils restent liés au roulis ;
   - mettre à jour le commentaire du clavier.
2. `PlayerVehicleController.cs` :
   - export `KeyboardBank` (0.45, `Range 0..1`) : au clavier ou à la manette, le roulis vaut `Input.GetAxis(RollLeft, RollRight) * KeyboardBank` (0,45 × 180° ≈ 80° à fond) ;
   - export `MouseBrakeStart` (0.8) : part de la course vers le bas à partir de laquelle l'aérofrein s'ajoute ;
   - réticule actif :
     - `Roll = clamp(rollClavier + steer.X, -1, 1)` ;
     - `pitchSouris = -steer.Y` (`InvertPitch` toujours appliqué), puis `Pitch = clamp(pitchClavier + pitchSouris, -1, 1)` ;
     - `freinSouris = steer.Y > MouseBrakeStart ? (steer.Y - MouseBrakeStart) / (1 - MouseBrakeStart) : 0` ;
     - `Brake = max(Input.GetActionStrength(Brake), freinSouris)` ;
   - propriété publique `MouseBrake` (dernier frein du réticule, 0..1), lue par le réticule ;
   - `Yaw = 0`, et supprimer la lecture des actions de lacet ;
   - mettre à jour le commentaire de classe.
3. `SteeringReticle.cs` :
   - **zone de frein** : un arc dessiné sur le cercle de course, en bas, entre les angles qui correspondent à `y = MouseBrakeStart` (rouge, 30 % d'opacité, épaisseur 3). L'arc passe à 85 % d'opacité quand `Controller.MouseBrake > 0` ;
   - **horizon artificiel** : il faut le véhicule, donc exposer dans le contrôleur `public Vehicle Target` (existe déjà). Au centre, un segment de 60 px tourné de `-Target.Bank`, blanc à 50 %, épaisseur 2. À +20° de part et d'autre, deux petits traits de 8 px (repères d'inclinaison) ;
   - ne rien dessiner si `Controller` est nul ou si `MouseSteering` est faux, comme aujourd'hui.
4. `DebugHud.cs` : `HelpText` = `"[AZERTY] Poussée Z · Frein S · Réticule souris : gauche/droite = incliner, haut = cabrer, bas = piquer (tout en bas : frein) · Clavier : roulis Q/D ou A/E, tangage flèches · Nitro Maj · Recentrer clic molette · Souris Échap · Véhicule Tab · Respawn R · Caméra F5"`.

## Contraintes
- Code et commentaires en français, style existant, exports groupés (voir CLAUDE.md).
- Ne pas changer la poussée, le nitro ni le vertical.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] `"C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe" --headless --path . --quit-after 300` ne sort ni `ERROR` ni `Exception` (hors fuites à la sortie).
- [ ] `grep -n "YawLeft\|YawRight" Scripts/Core/InputBootstrap.cs` ne renvoie que les constantes, aucun `Bind`.
- [ ] `grep -n "Yaw" Scripts/Vehicles/PlayerVehicleController.cs` ne renvoie que `Yaw = 0`, ou rien.

## Retour attendu
- Fichiers modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
