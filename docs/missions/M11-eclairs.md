# Mission M11 : Éclairs visibles (traits de foudre et lumière)

## Objectif
Rendre la foudre spectaculaire : à chaque éclair émis par les nuages, afficher un **trait de foudre** lumineux et ramifié, accompagné d'un **flash de lumière** sur la scène. Quand la foudre frappe le joueur, le trait relie le nuage à son engin.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement :
  - `Scripts/Clouds/CloudField.cs` : le signal `LightningTriggered(Vector3 position, Vector3 strikeTarget, bool hasTarget)`, émis par `TriggerLightning(...)`. Voir aussi `CloudField.Instance`, `LayerBottom`, `Sample(pos)` (`Density`, `Sdf`).
  - `Scripts/Clouds/CloudEffects.cs` : la méthode `Strike()` déclenche la foudre sur le joueur.
- Les nuages sont rendus par un effet de compositing **avant** les objets transparents, en tenant compte de la profondeur des objets **opaques**. Un trait de foudre **opaque** (matériau non éclairé, émissif) sera donc correctement caché par les nuages devant lui. C'est voulu : **n'utilise pas de transparence** pour le trait.
- Le glow de l'environnement est actif (`GlowHdrThreshold = 1.2`) : une émission forte (énergie 6 à 10) donnera un halo.

## Fichiers concernés
- Créer : `Scripts/Clouds/LightningBolt.cs`
- Créer : `Scripts/Clouds/LightningSystem.cs`
- Modifier : `Scripts/Clouds/CloudEffects.cs`, uniquement l'appel à `TriggerLightning` dans `Strike()`. Il doit passer la position du véhicule comme cible : `TriggerLightning(flash, v.GlobalPosition, true)`.
- Modifier : `Scenes/Main.tscn`. Ajoute un nœud `LightningSystem` (type `Node3D`) avec son script, et rien d'autre. Mets à jour `load_steps`.
- Ne pas toucher : `CloudField.cs`, `CloudRenderer.cs`, les shaders, les autres scripts.

## Étapes

### 1. LightningBolt
Namespace `Bomba.Clouds`, `public partial class LightningBolt : Node3D` (pas `[Tool]`), `TopLevel = true` dans `_Ready`.
- `public void Setup(Vector3 from, Vector3 to, RandomNumberGenerator rng)` construit le trait en coordonnées globales :
  - **Tracé principal** : déplacement du point médian, récursif sur 5 niveaux. À chaque niveau, le milieu du segment est décalé perpendiculairement d'une valeur aléatoire proportionnelle à la longueur du segment (facteur 0,25), ce qui donne environ 32 segments.
  - **Ramifications** : 2 à 4, partant de points aléatoires du tracé principal (pas dans le premier ni le dernier quart). Chacune fait 15 à 35 % de la longueur totale, dans une direction proche du tracé principal mais déviée de 25 à 50°, avec 3 niveaux de déplacement.
  - **Géométrie** : chaque segment est un `MeshInstance3D` avec un `CylinderMesh` (rayon 0,5 m pour le tracé principal, 0,25 m pour les branches, `RadialSegments = 4`, `Rings = 1`), orienté de son point A à son point B : `Basis` dont l'axe Y = (B − A) normalisé, centré au milieu, hauteur = longueur. Partage **un seul** `CylinderMesh` de hauteur 1 par rayon, mis à l'échelle en Y (`Scale`), pour limiter les ressources.
  - **Matériau** : un seul `StandardMaterial3D` partagé, `ShadingMode = Unshaded`, albédo `(0.85, 0.9, 1.0)`, émission identique, `EmissionEnergyMultiplier = 8`, ombres désactivées (`CastShadow = Off`).
- **Vie** : 0,35 s au total. Le trait **scintille** : il est visible 0,06 s, invisible 0,04 s, visible 0,1 s, puis s'estompe. L'estompe se fait en baissant `EmissionEnergyMultiplier` jusqu'à 0, puis le nœud est libéré (`QueueFree`). Utilise `_Process`.

### 2. LightningSystem
Namespace `Bomba.Clouds`, `public partial class LightningSystem : Node3D`.
- Exports : `BoltChance = 0.6f` (probabilité qu'un éclair décoratif ait un trait visible ; sinon flash dans le nuage seul), `MinDownLength = 80f`, `MaxDownLength = 220f`, `FlashEnergy = 10f`, `FlashRange = 450f`.
- `_Ready` : se connecte à `CloudField.Instance.LightningTriggered`. Si l'instance est nulle, réessaie dans `_Process` jusqu'à ce qu'elle existe. Déconnecte dans `_ExitTree`.
- Sur signal `(position, target, hasTarget)` :
  - **Avec cible** : trait de `position` à `target`.
  - **Sans cible** : avec une probabilité `BoltChance`, trait de `position` vers le bas, jusqu'au premier de ces deux points : la sortie du nuage plus 40 à 120 m, ou une longueur de `MinDownLength` à `MaxDownLength`. Pour trouver la sortie, descends par pas de 10 m tant que `Sample(p).Density > 0.02`. Le point d'arrivée est décalé horizontalement au hasard, de 20 % de la longueur au plus.
  - **Flash de lumière** dans tous les cas : un `OmniLight3D` à `position` (couleur `(0.7, 0.8, 1.0)`, `OmniRange = FlashRange`, énergie `FlashEnergy`, ombres désactivées), qui s'éteint en 0,3 s (tween de `LightEnergy` à 0, puis `QueueFree`).
  - Les traits sont ajoutés comme enfants de ce nœud.
  - Limite : au plus 6 traits actifs à la fois. Au-delà, on ignore les nouveaux traits, mais pas les flashs.

## Contraintes
- Pas de `[Tool]` (rien à afficher dans l'éditeur).
- Aucune modification de `CloudField` ni du rendu des nuages.

## Critères de succès
- [ ] `dotnet build` passe.
- [ ] `GODOT --headless --editor --path . res://Scenes/Main.tscn --quit-after 200` : aucune erreur.
- [ ] Capture : `GODOT --path . --write-movie <dossier>/f.png --fixed-fps 30 res://Scenes/Debug/CloudShots.tscn`. Le plan 4 (« orage », frames 60 à 74) déclenche des éclairs vers sa fin. Regarde les frames 70 à 74 avec ton outil de lecture d'image et décris si un trait de foudre est visible.

## Retour attendu
Liste des fichiers créés ou modifiés, un résumé de 3 lignes maximum, la description de la capture, les hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien (le chef de projet modifie les nuages en parallèle), signale-le sans y toucher.
