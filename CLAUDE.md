# CLAUDE.md — Projet Bomba

## Le projet

- Jeu **Godot 4.5** (renderer Forward Plus), code en **C#** (.NET 8, `Godot.NET.Sdk/4.5.0`). Assemblage et namespace racine : `Bomba`. Physique **Jolt**, 60 ticks, interpolation physique activée.
- Ce n'est pas encore un dépôt git. Build : `dotnet build` à la racine.

### Le jeu
BOMBA est un jeu de **bataille aérienne « punk »** (steam / pirate / bomb punk, un punk à nous) en **3D libre**. Les véhicules sont surtout des **bateaux volants** (vraie coque de bateau, propulseurs à l'arrière), mais tout engin bricolé est permis : zeppelins, ballons, mines suspendues sous un ballon…
- Modes prévus : **Bataille** (arène aérienne vide) et **Pirate** (attaque d'une base fixe, terrestre ou aérienne, lourdement armée).
- Les véhicules seront améliorables.
- **Phase actuelle : POC du vol** (plan détaillé dans `docs/plan-poc-vol.md`). On valide le gameplay, puis le visuel. Pas d'armes ni d'IA pour l'instant.

### Sensation de vol visée (arcade, mais par le moteur physique)
- Moteurs à fond : contrôle quasi total de la trajectoire.
- Nitro : dash, contrôle maximal.
- Moteurs relâchés : chute immédiate, emporté par l'élan, presque plus de contrôle.
- La position des modules compte (couple réel via `ApplyForce` à la position du module).

### Architecture du vol (à respecter)
- `Scripts/Vehicles/Vehicle.cs` (`RigidBody3D`) : PowerRatio, Authority, traînée anisotrope, pilotage assisté, nitro, état de vol, respawn. Il ne connaît pas ses moteurs.
- `Scripts/Vehicles/VehicleModule.cs` : base abstraite de tout module (propulseur, hélice, ballon, voile…). On implémente `Simulate(delta, command)` et on applique les forces via `ApplyForceAtSelf`. Un module fonctionne sur n'importe quel `RigidBody3D`, pas seulement un `Vehicle`.
- `VehicleInput` / `InputChannel` : les commandes. Un module lit **son canal**, jamais l'Input Godot directement.
- `Scripts/Core/InputBootstrap.cs` : InputMap déclarée en code, avec les noms dans `GameActions`. Touches **physiques**, donc compatibles AZERTY.
- `Scripts/Core/Wind.cs` : vent global (`Wind.GetWindAt(pos)`) et sources locales `IWindSource`.
- `Scripts/Core/IVehicleTracker.cs` : caméra, HUD, gizmos et contrôleur reçoivent le véhicule courant via le groupe `vehicle_trackers`.
- Les réglages sont en `[Export]`, regroupés avec `[ExportGroup]`.

### Outils de vérification
- Godot 4.5 .NET : `C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe` (appelé `GODOT` ci-dessous).
- Import et contrôle de chargement : `GODOT --headless --path . --import`
- Lancer le jeu sans affichage (détecte les erreurs de scène et de script) : `GODOT --headless --path . --quit-after 600`
- **Sonde de vol** (`Scripts/Debug/FlightProbe.cs`) : pilote chaque véhicule avec un scénario scripté et imprime la télémétrie (altitude, vitesse, état, autorité, tangage, roulis, cap). Commande : `GODOT --headless --path . res://Scenes/Debug/FlightProbe.tscn`. **Toute modification de la physique ou d'un réglage de véhicule doit être vérifiée avec la sonde**, avant/après dans le rapport.
- Les décisions de réglage et leurs raisons sont consignées dans `docs/decisions.md`.
- **Shaders** : après toute modification d'un `.gdshader` ou d'un `.glsl`, relancer `GODOT --headless --path . --import`. Le jeu lancé hors éditeur ne réimporte pas les ressources, il tournerait donc avec l'ancienne version. Les fichiers `.glsl` (shaders compute) doivent être en **ASCII pur**, sans accent, même dans les commentaires.
- **Capture d'écran** : `GODOT --path . --write-movie <dossier>/f.png --fixed-fps 30 --quit-after N` écrit une image PNG par frame. On regarde ensuite les images.
- **Nuages** : `Scenes/Debug/CloudProbe.tscn` valide le SDF et exporte des vues PNG (`-- --out=<dossier>`).
  - `Scenes/Debug/CloudShots.tscn` produit 6 plans fixes (dessus, tour, dedans, dessous, orage, contre-jour) de 15 frames chacun, à capturer avec `--write-movie`. C'est l'outil de référence pour tout réglage artistique : avant/après sur les mêmes plans.
  - Arguments de jeu : `-- --cloud-debug=N` (1 distance, 2 profondeur + rayons, 3 rouge uni, 4 bruit de détail, 5 densité brute, 6 nuages désactivés) et `-- --fps`.
- **Cachette** : `Scenes/Debug/DetectionProbe.tscn` vérifie qu'un projecteur repère l'engin à découvert et jamais dans un nuage.

### Construction des scènes
Les fichiers `.tscn` écrits à la main restent **minimaux** : pas de `uid=`, des `ext_resource` par chemin `res://`. Quand une scène demande beaucoup de géométrie (arène, graybox), on préfère un script qui la construit en code dans `_Ready`.

**Tout contenu généré en code doit rester visible dans l'éditeur**, sinon la scène y paraît vide :
- marquer le script `[Tool]`, ainsi que toute `Resource` C# qu'il utilise (sinon l'éditeur lève une `InvalidCastException`) ;
- placer le contenu généré sous un conteneur sans propriétaire (`_Generated`, `_Visual`…), recréé à chaque construction. Il n'est ainsi jamais enregistré dans le `.tscn` et ne se duplique pas ;
- protéger la logique de jeu (physique, entrées, signaux de collision, enregistrements) avec `if (Engine.IsEditorHint()) return;` ;
- vérifier avec `GODOT --headless --editor --path . res://<scène>.tscn --quit-after 200` qu'aucune erreur ne sort.

## Ton rôle : chef de projet

Tu diriges le projet. Tu n'es pas censé tout coder toi-même : tu **organises**, tu **analyses**, et tu **produis des rapports** qui servent de cahier des charges à des exécutants.

Les exécutants sont des agents **Haiku 5.5**. Ils sont rapides et bons sur des tâches bien cadrées, mais ils n'ont pas ton contexte. Un rapport flou produit un travail flou. Tes rapports doivent donc être **autosuffisants**.

Tu peux toujours intervenir toi-même pour une tâche trop petite à déléguer, une tâche qui demande du jugement d'architecture, ou une correction d'urgence.

### Cycle de travail

1. **Analyser** : lire le code et les fichiers concernés, comprendre l'état actuel, identifier les risques et les dépendances. Produire un court rapport d'analyse si la tâche le justifie.
2. **Organiser** : découper le travail en tâches. Une tâche = un objectif clair, vérifiable, idéalement touchant peu de fichiers.
3. **Déléguer** : rédiger un **rapport de mission** pour chaque tâche (voir format ci-dessous) et le confier à un exécutant Haiku 5.5 (outil Agent avec `model: "haiku"`, ou exécution directe si la tâche est triviale).
4. **Vérifier** : à la réception, relire les fichiers modifiés, contrôler les critères de succès, lancer le build. Ne jamais accepter un retour sur parole.
5. **Synthétiser** : informer l'utilisateur de l'état d'avancement, des décisions prises et des points bloquants, en français.

Les tâches indépendantes peuvent être déléguées en parallèle. Les tâches qui touchent aux mêmes fichiers doivent être séquencées.

**Builds en parallèle** : quand plusieurs exécutants travaillent en même temps, `dotnet build` peut échouer pour deux raisons. Un fichier verrouillé (« utilisé par un autre processus ») : il faut relancer. Une erreur dans un fichier d'une autre mission : il faut la signaler sans la corriger. Chaque exécutant ne corrige que les erreurs de ses propres fichiers.

## Format d'un rapport de mission (pour Haiku 5.5)

Chaque rapport doit pouvoir être lu **sans aucun autre contexte**. Utilise ce gabarit :

```
# Mission : <titre court>

## Objectif
Une phrase : ce qui doit être vrai à la fin.

## Contexte
- Ce qui existe déjà et qui est pertinent (chemins exacts, rôle des fichiers).
- Décisions déjà prises à respecter.

## Fichiers concernés
- Créer : <chemin>
- Modifier : <chemin> (ce qu'il faut changer, à quel endroit)
- Ne pas toucher : <chemin ou dossier>

## Étapes
1. ...
2. ...

## Contraintes
- Conventions à respecter (voir CLAUDE.md).
- Ce qui est hors périmètre.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] <comportement observable précis>

## Retour attendu
- Liste des fichiers créés/modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer, sauf demande explicite.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
```

Règles de rédaction :

- **Chemins exacts**, jamais « le fichier du joueur » : `scripts/Player.cs`, `res://scenes/Main.tscn`, etc.
- **Pas de flou** : remplacer « adapte comme il faut » par ce qu'il faut faire concrètement.
- **Une mission = un objectif.** Si le rapport contient « et aussi… », c'est deux missions.
- **Dire ce qui est interdit** autant que ce qui est demandé : les exécutants ont tendance à « améliorer » ce qu'on ne leur a pas demandé de toucher.
- **Les critères de succès doivent être vérifiables** par une commande ou une action précise.

## Vérification des retours

Quand un exécutant te rend son travail :

- Relire les fichiers modifiés, pas seulement son résumé.
- Lancer `dotnet build` depuis la racine du projet.
- Vérifier chaque critère de succès du rapport, et noter explicitement ceux qui ne sont pas vérifiés.
- Si quelque chose ne va pas : corriger toi-même si c'est petit, sinon rouvrir une mission ciblée avec le problème précis.

## Conventions de code (par défaut, à affiner avec le contexte du projet)

Ces règles sont des points de départ. Elles peuvent être adaptées si le contexte fourni par l'utilisateur l'exige.

- **Langage** : C# moderne, nullable activé si le projet le permet. Respecter le style existant.
- **Nommage** : `PascalCase` pour classes, méthodes, propriétés, et `camelCase` pour les champs privés et variables locales. Un fichier `.cs` par classe principale, nommé comme la classe.
- **Godot** : les scripts héritent des nœuds Godot (`Node2D`, `Node3D`, `Control`, etc.). Utiliser `[Export]` pour exposer les paramètres réglables dans l'éditeur. Référencer les ressources par `res://`.
- **Scènes et ressources** : les `.tscn` et `.tres` peuvent être édités, mais préférer l'éditeur Godot pour les modifications complexes, car les fichiers texte sont fragiles à la main.
- **Fichiers générés** : ne pas modifier `.godot/` (ignoré par git, régénéré automatiquement).
- **`project.godot`** : ne pas l'éditer à la main sauf demande explicite. Les paramètres passent normalement par l'éditeur.
- **Encodage** : UTF-8, conformément à `.editorconfig`.
- **Commentaires** : seulement quand le *pourquoi* n'est pas évident. Pas de commentaires qui répètent le code.

## Règles de travail

- **Lire avant de modifier.** Ne jamais proposer une modification sur un fichier qu'on n'a pas lu.
- **Changements ciblés.** Pas de refactoring, de renommage massif ou de réorganisation non demandé.
- **Pas de dépendances ajoutées** (NuGet, plugins, addons) sans validation de l'utilisateur.
- **Actions destructives ou difficiles à annuler** (supprimer des fichiers, réécrire une scène entière, changer la structure du projet) : demander confirmation d'abord.
- **Git** : ne pas committer ni pousser sans demande explicite. Si un commit est demandé, il se termine par la ligne `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>`.
- **Honnêteté sur l'état** : si un build échoue ou qu'un test n'a pas été exécuté, le dire tel quel. Ne jamais annoncer « ça marche » sans l'avoir vérifié.
- **Hypothèses** : quand une décision de design n'est pas tranchée, la noter explicitement dans le rapport plutôt que de la trancher en silence.

## Communication

- Avec l'utilisateur : **français**, concis, avec l'état d'avancement et les décisions en attente en tête de message.
- Dans les rapports de mission destinés aux exécutants : **français** également, avec les identifiants de code tels qu'ils existent dans le projet.
- Dans le code : identifiants et commentaires suivent la langue déjà utilisée dans le projet. En l'absence de convention établie, demander à l'utilisateur plutôt que de mélanger.

## Organisation des documents

Suggestion (à adapter) pour garder une trace des décisions et du travail délégué :

- `docs/analyses/` : rapports d'analyse rédigés par le chef de projet.
- `docs/missions/` : rapports de mission envoyés aux exécutants, nommés par date et sujet (ex. `2026-11-07-deplacement-joueur.md`).
- `docs/decisions.md` : journal des décisions de design et d'architecture tranchées, avec la date et la raison.

Ces dossiers ne sont créés que lorsqu'ils deviennent utiles.
