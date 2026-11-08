# Mission M12 : Gouttes d'eau sur l'écran dans les nuages

## Objectif
Quand le joueur traverse un nuage, l'écran se couvre **progressivement** de gouttes d'eau qui déforment l'image comme de petites lentilles. **La vitesse les chasse** : en allant vite, les gouttes s'étirent, glissent vers les bords de l'écran (flux d'air) puis disparaissent. Hors du nuage et à l'arrêt, elles sèchent lentement.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`.
- Lis attentivement :
  - `Scripts/Clouds/CloudEffects.cs` : `Density` (0..1 sous le véhicule) et `InCloud`. Ce nœud est dans le groupe `IVehicleTracker.GroupName`.
  - `Scripts/Core/IVehicleTracker.cs` : ton nœud reçoit le véhicule via `Target`.
  - `Scripts/Vehicles/Vehicle.cs` : `Speed`, `NitroActive`.
  - `Scripts/Debug/DebugHud.cs` : c'est un `CanvasLayer` de couche 1. Tes gouttes doivent passer **sous** le HUD.
- Rendu Forward+, Godot 4.5. Un shader `canvas_item` peut lire l'image 3D déjà rendue avec `uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;`.

## Fichiers concernés
- Créer : `Shaders/Effects/screen_droplets.gdshader` (`shader_type canvas_item`)
- Créer : `Scripts/Effects/ScreenDroplets.cs`
- Créer : `Scenes/Debug/DropletsTest.tscn`, une scène de test temporaire (voir les critères).
- Ne pas toucher : tout autre fichier, **y compris `Scenes/Main.tscn`** (le chef de projet ajoutera le nœud ; un autre exécutant modifie `Main.tscn` en parallèle).

## Étapes

### 1. `screen_droplets.gdshader`
Uniforms :
- `coverage` (0..1) : part des gouttes présentes ;
- `streak` (0..1) : étirement et glissement dû à la vitesse ;
- `flow_time` (float, temps cumulé du glissement, piloté par le script) ;
- `drop_scale = 14.0` : nombre de cellules sur la hauteur de l'écran pour la couche principale ;
- `refraction = 0.025` ;
- `tint`, une couleur `source_color` légère, par défaut `(0.85, 0.9, 1.0)`, et `darken_rim = 0.25`.

Le principe :
- **Trois couches** de gouttes de tailles différentes (échelles ×1, ×1,8, ×3,1). Dans chaque couche, l'écran est découpé en cellules (corrige l'aspect avec `SCREEN_PIXEL_SIZE` pour que les gouttes soient rondes), avec au plus une goutte par cellule.
- Chaque goutte a un **seuil d'apparition** aléatoire (`hash`). Elle est présente si `hash < coverage`, ce qui la fait apparaître progressivement quand `coverage` monte. Sa taille aléatoire va de 0,15 à 0,4 de la cellule, et sa position est aléatoire dans la cellule.
- **Flux d'air** : avec la direction radiale `dir = normalize(uv - 0.5)`, décale la position des gouttes de `dir * streak * flow_time * vitesse_par_goutte`. Les gouttes glissent vers les bords, avec un défilement qui boucle (`fract`) cellule par cellule pour qu'il en revienne. Étire-les dans la direction radiale : distance anisotrope, allongée d'un facteur `1 + streak * 4`. Quand `streak` est proche de 1, la goutte laisse une **traînée** derrière elle : un segment plus fin et plus transparent.
- **Rendu d'une goutte** : à partir de la distance normalisée au centre `d` (0 au centre, 1 au bord), calcule une normale de lentille `n = (p - centre) / rayon`. L'UV écran lue est `SCREEN_UV - n.xy * refraction * (1 - d²)`, ce qui donne une image réfractée et légèrement inversée. Bord légèrement plus sombre (`darken_rim`), petit reflet blanc décentré en haut à gauche.
- Hors des gouttes : `COLOR = texture(screen_texture, SCREEN_UV)` (aucune modification). Pour la performance, si `coverage < 0.001`, renvoie directement l'image.
- Mets `COLOR.a = 1.0`.

### 2. `ScreenDroplets`
Namespace `Bomba.Effects`, `public partial class ScreenDroplets : CanvasLayer, IVehicleTracker`.
- `_Ready` : `Layer = 0` (sous le HUD), rejoint le groupe `IVehicleTracker.GroupName`, crée un `ColorRect` plein écran (ancres à 0 et 1, `MouseFilter = Ignore`) avec un `ShaderMaterial` sur `res://Shaders/Effects/screen_droplets.gdshader`.
- Exports : `WetRate = 0.9f` (couverture par seconde à densité 1), `DryRate = 0.12f` (séchage par seconde hors nuage), `ClearSpeedStart = 25f` (m/s), `ClearSpeedFull = 70f` (m/s), `ClearRate = 0.8f` (couverture retirée par seconde à vitesse maximale), `NitroClearBoost = 2f`.
- `_Process(delta)` :
  - `density` vient du premier `CloudEffects` du groupe (0 s'il est absent). `speed` vient de `Target?.Speed` (0 s'il n'y a pas de cible).
  - `s = smoothstep(ClearSpeedStart, ClearSpeedFull, speed)`.
  - `coverage += density * WetRate * delta`. Hors nuage (`density < 0.02`), `coverage -= DryRate * delta`. Ensuite, `coverage -= s * ClearRate * delta * (nitro ? NitroClearBoost : 1)`. Borne entre 0 et 1.
  - Dans un nuage à grande vitesse, l'humidité continue d'arriver mais est chassée. L'équilibre doit donner des gouttes qui naissent et filent : c'est voulu.
  - `streak` suit `s` avec lissage (`Mathf.Lerp(streak, s, 1 - exp(-4 * delta))`). `flowTime += delta * (0.2 + 2.5 * streak)`.
  - Transmet `coverage`, `streak` et `flow_time` au shader. Masque le `ColorRect` (`Visible = false`) quand `coverage < 0.001`, pour ne rien coûter hors nuage.
- Export de debug `ForceCoverage = -1f` : s'il est ≥ 0, il remplace la couverture calculée (pour les tests).

## Contraintes
- Pas de `[Tool]`.
- Aucun accès à `Input`.
- Le shader doit rester léger (3 couches et un voisinage 3×3 maximum par couche).

## Critères de succès
- [ ] `dotnet build` passe et `GODOT --headless --path . --import` ne signale aucune erreur de shader.
- [ ] Scène de test `Scenes/Debug/DropletsTest.tscn` : une instance de `res://Scenes/Main.tscn` plus un nœud `ScreenDroplets` avec `ForceCoverage = 0.7`. Capture avec `GODOT --path . --write-movie <dossier>/f.png --fixed-fps 30 --quit-after 40 res://Scenes/Debug/DropletsTest.tscn`. Regarde la frame 35 avec ton outil de lecture d'image et décris les gouttes (taille, réfraction, lisibilité de la scène derrière).
- [ ] Le jeu normal (`Main.tscn`) n'est pas modifié.

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, la description de la capture, les hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier qui n'est pas le tien (d'autres travaillent en parallèle), relance après quelques instants ou signale-le sans y toucher.
