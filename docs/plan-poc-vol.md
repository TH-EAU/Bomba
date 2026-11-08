# Plan — POC gameplay de vol de BOMBA

## Contexte

BOMBA est un jeu de bataille aérienne « punk » (steam / pirate / bomb punk) : des bateaux volants, des zeppelins, des engins bricolés. Deux modes sont prévus plus tard, **Bataille** (arène aérienne) et **Pirate** (attaque d'une base fixe lourdement armée). Ce premier POC sert à **valider la sensation de vol**, sans combat ni visuel final.

Décisions validées avec l'utilisateur :
- **3D libre** (vol sur les 3 axes), contrôles **clavier + manette**.
- Périmètre : **vol + arène de test** (plusieurs véhicules, modules de propulsion, nitro, chute balistique, obstacles et cibles fixes). Pas d'armes ni d'IA.
- Physique **réelle via le moteur** (RigidBody3D + forces appliquées à la position de chaque module), mais réglée **arcade**.

Sensation cible :
1. **Moteurs à fond** : contrôle quasi total de la trajectoire.
2. **Nitro** : dash puissant, contrôle maximal pendant le boost.
3. **Moteurs relâchés** : chute immédiate, l'engin est emporté par son élan et le joueur n'a presque plus de contrôle.
4. **La position des modules compte** : un propulseur décentré fait tourner l'engin, un ballon au-dessus du centre de masse le stabilise.

État actuel du projet : il est vide (`project.godot` en Godot 4.5 Forward Plus, assemblage `Bomba`, pas de `.csproj`). Le .NET SDK 9 est installé. On reprend le gabarit `.csproj` du projet voisin `E:\Godot Games\ultra\Ultra.csproj` (`Godot.NET.Sdk/4.5.0`, `net8.0`).

## Architecture

### Principe central : véhicule = coque + modules
- `Vehicle` (RigidBody3D) ne connaît pas ses moteurs. À `_Ready`, il découvre tous ses enfants `VehicleModule` (récursivement).
- Chaque module, à chaque `_PhysicsProcess`, calcule sa force et l'applique **à sa propre position** : `vehicle.ApplyForce(forceGlobale, module.GlobalPosition - vehicle.GlobalPosition)`. Le couple émerge donc naturellement du placement.
- Chaque module est branché sur un **canal d'entrée** (`InputChannel` : MainThrust, Vertical, Trim, Always…). On peut ainsi monter n'importe quel module sur n'importe quel véhicule, voire sur une mine flottante.
- `VehicleInput` est un struct (Throttle, Pitch, Yaw, Roll, Vertical, Trim, Nitro). Il est produit par `PlayerVehicleController` aujourd'hui, et le sera par une IA demain.
- Les réglages sont exportés (`[Export]`). Les profils de propulseurs sont des `Resource` (`.tres`) pour pouvoir les régler dans l'inspecteur.

### Modèle de vol arcade (formules de base, réglables)
- **Puissance** : `PowerRatio = Σ poussée actuelle / Σ poussée max` des modules moteurs.
- **Autorité de contrôle** : `Authority = clamp(PowerRatio × PowerWeight + airspeed/RefSpeed × AirWeight, 0, 1)`, avec un PowerWeight fort et un AirWeight faible. La nitro force l'autorité à 1.
- **Portance soufflée** (`AeroSurface`) : `lift = LiftCoef × vitesseAvant² × lerp(UnpoweredLiftFactor, 1, PowerRatio)` selon l'axe haut de la surface. Moteurs coupés, la portance s'effondre et l'engin tombe.
- **Traînée anisotrope** (effet de quille) : forte sur les axes latéral et vertical quand l'engin est propulsé, ce qui le fait aller là où il pointe. Elle devient faible quand il ne l'est plus, et l'élan reprend le dessus.
- **États** : `Propulsé` → `Nitro` → `Balistique` (PowerRatio ≈ 0 depuis plus de 0,15 s). Ils servent au HUD et aux multiplicateurs.
- **Assistance de stabilité** : couple de rappel vers un roulis nul, plus un amortissement angulaire. Elle est réglable et désactivable.
- **Nitro** : une jauge (consommation et recharge), une impulsion initiale `ApplyCentralImpulse`, un multiplicateur de poussée temporaire, un FOV kick et un léger shake caméra.
- Le `GravityScale` du véhicule est réglable (probablement autour de 1,5 à 2 pour une chute franche).
- Moteur physique : **Jolt** (`physics/3d/physics_engine`), à 60 ticks.

### Modules du POC (implémentés)
| Module | Comportement |
|---|---|
| `Thruster` (propulseur) | Poussée selon -Z local, avec montée et descente en régime (*spool*). Le **gimbal** oriente la poussée selon Pitch/Yaw : c'est ce qui donne le contrôle quand on pousse et le retire quand on coupe. Il prend un `ThrusterProfile`. |
| Profils de propulseur | **Charbon** : lent à monter, très puissant, fumée noire. **Vapeur** : régulier, surchauffe si utilisé longtemps. **Fusée à poudre** : instantané et brutal, faible autonomie. |
| `Propeller` (hélice) | `poussée = max × input × (1 - vitesseAvant/VitesseMaxEfficace)`. Orientée vers le haut, elle sert de rotor (autogire). Elle a un visuel qui tourne. |
| `Balloon` (ballon) | Poussée d'Archimède vers le haut (repère global), appliquée à sa position, d'où le **redressement en pendule**. Deux modes : *Gaz* (fixe) et *Montgolfière* (la chaleur suit l'entrée Vertical avec inertie). |
| `Sail` (voile orientable) | Vent relatif `w = Vent - vitesse`, force de plaque plane `SailCoef × \|w\|² × dot(n, ŵ) × n`. L'angle est réglé par l'entrée Trim, dans des limites. |
| `AeroSurface` | Portance soufflée et traînée de coque ou d'aile. Variante *gouvernail* : couple Pitch/Yaw proportionnel à `Authority`. |
| `Wind` (autoload) | Vent global, plus des zones de rafale `Area3D` dans l'arène. |

**Backlog des propulsions** (proposé, pas dans ce POC) : ailes battantes (ornithoptère, impulsions par battement), **canon à recul** (on tire vers l'arrière pour avancer, très « bomb punk »), roue à aubes aérienne, cerf-volant / parafoil tracteur, cristal d'éther (poussée omnidirectionnelle faible), moteur à ressort (pic puis décroissance), ballast largable (sursaut vertical), gyroscope (couple de stabilisation).

### Contrôles (InputMap enregistrée en code au démarrage, plus robuste que l'édition à la main de `project.godot`)
| Action | Clavier | Manette |
|---|---|---|
| Poussée + / - | W / S | RT / LT (analogique) |
| Tangage | ↑ / ↓ | Stick gauche Y |
| Lacet | A / D (ou ← / →) | Stick gauche X |
| Roulis | Q / E | LB / RB |
| Vertical (ballon, rotor) | Espace / Ctrl | Stick droit Y |
| Trim de la voile | Z / C | Croix directionnelle ← / → |
| Nitro | Shift | A (bouton du bas) |
| Changer de véhicule | Tab | Select |
| Respawn | R | Start |
| Debug (forces, HUD) | F1 | — |

### Arborescence prévue
```
Bomba.csproj, Bomba.sln
Scripts/Core/        InputBootstrap.cs, Wind.cs (autoload), GameRoot.cs (changement de véhicule, respawn)
Scripts/Vehicles/    Vehicle.cs, VehicleInput.cs, PlayerVehicleController.cs, VehicleModule.cs (base abstraite), InputChannel.cs
Scripts/Vehicles/Modules/   Thruster.cs, ThrusterProfile.cs, Propeller.cs, Balloon.cs, Sail.cs, AeroSurface.cs
Scripts/Camera/      ChaseCamera.cs (suivi lissé, anticipation selon la vitesse, FOV dynamique)
Scripts/Debug/       DebugHud.cs (vitesse, altitude, état, autorité, jauge nitro), ForceGizmos.cs (vecteurs de force via ImmediateMesh)
Scripts/Arena/       Ring.cs (anneaux de parcours), FloatingMine.cs (bombe sous ballon, qui réutilise Balloon), KillZone.cs
Scenes/Main.tscn, Scenes/Arena/TestArena.tscn, Scenes/Vehicles/{Rafiot,Zeppelin,Guepe}.tscn
Resources/Thrusters/{Charbon,Vapeur,Fusee}.tres
docs/missions/       rapports de mission envoyés aux exécutants
```

### Véhicules du POC (en *graybox* : primitives CSG ou Mesh, le visuel viendra ensuite)
- **Le Rafiot** (image 2) : coque de bateau, 2 propulseurs Charbon à gimbal à l'arrière, AeroSurface de coque, petit gouvernail. C'est l'engin de référence.
- **Le Zeppelin** : gros ballon (mode montgolfière), hélice arrière, voile orientable. Lent, flottant, sensible au vent.
- **La Guêpe** : coque légère, rotor (hélice verticale) plus fusées à poudre. Nerveuse.
- **Démo modules** : une mine flottante sous ballon (image 1) dans l'arène, sans aucun code spécifique. Elle prouve que les modules sont génériques.

### Arène de test
Ciel procédural, mer très bas (KillZone qui déclenche le respawn), îlots rocheux flottants, parcours d'anneaux, zones de rafale, mines flottantes. Les collisions avec les mines appliquent une impulsion d'explosion (pas de système de dégâts).

## Exécution : missions et délégation

Les missions sont rédigées au format de `CLAUDE.md`, enregistrées dans `docs/missions/` et confiées à des agents **Haiku** (outil Agent, `model: "haiku"`). Le chef de projet relit le code, lance le build et accepte ou rouvre chaque mission.

| # | Mission | Qui | Dépend de |
|---|---|---|---|
| M0 | Bootstrap : `Bomba.csproj` et `.sln`, dossiers, `project.godot` (Jolt, scène principale, autoloads), mise à jour de `CLAUDE.md` avec le contexte du jeu | Chef de projet | — |
| M1 | Contrats du noyau : `VehicleInput`, `InputChannel`, `VehicleModule`, `Vehicle` (découverte des modules, PowerRatio, Authority, états, traînée, stabilité, nitro), `InputBootstrap`, `PlayerVehicleController` | Chef de projet (architecture) | M0 |
| M2 | `Thruster` et `ThrusterProfile`, plus les 3 `.tres` | Haiku | M1 |
| M3 | `Propeller` et `Balloon` | Haiku | M1 |
| M4 | `Sail`, `AeroSurface`, autoload `Wind` et zones de rafale | Haiku | M1 |
| M5 | `ChaseCamera`, `DebugHud`, `ForceGizmos` | Haiku | M1 |
| M6 | Arène : `TestArena.tscn`, `Ring`, `FloatingMine`, `KillZone`, `GameRoot`, `Main.tscn` | Haiku | M1 (M3 pour la mine) |
| M7 | 3 véhicules graybox et premier réglage | Haiku, puis réglage par le chef de projet | M2 à M6 |

M2 à M5 sont parallélisables (fichiers disjoints). M6 peut démarrer en parallèle avec une scène de véhicule provisoire.

## Vérification

1. **Build** : `dotnet build` à la racine après chaque mission. Il doit passer sans erreur.
2. **Revue** : relecture des fichiers de chaque mission par le chef de projet, contrôle des critères de succès.
3. **Test en jeu** (par l'utilisateur dans l'éditeur Godot, F5, puisque l'exécutable Godot n'est pas localisé ici). Grille de validation :
   - [ ] Le Rafiot décolle, accélère et vire. Il va là où il pointe quand il est propulsé.
   - [ ] La nitro donne un dash net et un contrôle maximal pendant le boost.
   - [ ] Moteurs relâchés : chute immédiate, trajectoire balistique, contrôle quasi nul.
   - [ ] Désactiver un propulseur latéral fait tourner l'engin (le couple vient du placement).
   - [ ] Le Zeppelin se redresse seul (ballon au-dessus du centre de masse) et dérive avec le vent. La voile permet de l'exploiter.
   - [ ] La Guêpe tient le stationnaire au rotor.
   - [ ] La mine flottante flotte grâce au seul module `Balloon`.
   - [ ] Clavier et manette fonctionnent tous les deux. Changement de véhicule et respawn OK.
   - [ ] Le HUD de debug et les gizmos de force (F1) rendent le comportement lisible.
