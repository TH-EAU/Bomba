# Mission : éclairs en splines lisses, opaques et lumineux

## Objectif
Les canaux de foudre sont des traits lisses, effilés (tronc épais, branches fines), opaques et très lumineux (HDR), donc atténués naturellement par l'épaisseur des nuages devant eux.

## Contexte
- `Scripts/Clouds/LightningBolt.cs` (MeshInstance3D) construit un arbre de points : listes `_points`, `_distance` (longueur depuis l'origine), `_segments` (A, B, Width). `Setup` = éclair ramifié vers une cible, `SetupSpider` = éclair qui rampe dans le nuage. `AddChain(from, path, width)` ajoute une chaîne de points. `Finish(leaderSpeed, field)` construit le maillage : un quad par segment, avec CUSTOM0 (autre extrémité, côté) et CUSTOM1 (x = temps d'arrivée, y = visibilité, z = largeur). Il calcule aussi `Visibility()` sur le CPU. `_Process` pilote `progress` et `flash` (propagation, puis arc en retour) : **ne pas modifier cette logique**.
- `Shaders/Effects/lightning_channel.gdshader` : rubans additifs (`blend_add`, `depth_draw_never`) élargis vers la caméra dans `vertex()`, avec une largeur minimale en pixels.
- Les nuages (`Scripts/Clouds/CloudRenderer.cs`, effet de compositeur `PreTransparent`) s'arrêtent à la profondeur des objets opaques et les atténuent (couleur × transmittance). Un objet **transparent** est dessiné après eux, donc il n'est jamais atténué : c'est le défaut à corriger. Un objet **opaque** est atténué correctement.
- L'environnement (`Scripts/Arena/ArenaBuilder.cs`) a le glow activé, avec un seuil HDR de 4 : une couleur supérieure à 4 produit un halo.

## Fichiers concernés
- Modifier : `Shaders/Effects/lightning_channel.gdshader`
- Modifier : `Scripts/Clouds/LightningBolt.cs`
- Ne pas toucher : `Scripts/Clouds/LightningSystem.cs`, `Scripts/Clouds/CloudRenderer.cs`, `Shaders/Clouds/*`, `Scripts/Arena/ArenaBuilder.cs`, tout le reste.

## Étapes
1. Shader `lightning_channel.gdshader` :
   - `render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;` : retirer `blend_add` et `depth_draw_never`, le rendu devient opaque.
   - Supprimer les uniformes `glow_ratio`, `hidden_glow`, `hidden_glow_spread` et le varying `v_core_frac`. La demi-largeur du ruban = `max(core_width * CUSTOM1.z, min_core_px * px_world)` (sans facteur de halo).
   - Défauts : `core_width = 4.0`, `min_core_px = 1.5`, `energy = 40.0`.
   - `fragment()` : `if (v_arrival > progress) discard;` ; calculer `level` comme aujourd'hui ; `if (level < 0.001) discard;` ; `float x = abs(v_x); ALBEDO = bolt_color * energy * level * (1.0 - 0.5 * x * x);`.
   - Garder `vertex()` (expansion face caméra) et les uniformes `progress`, `leader_end`, `flash`, `leader_level`, `head_boost`, `bolt_color`.
2. `LightningBolt.cs` : chemins lissés (spline).
   - Ajouter `private static List<Vector3> Smooth(List<Vector3> pts, int perSegment)` : Catmull-Rom uniforme. Entre chaque paire de points `pts[i]` et `pts[i+1]`, insérer `perSegment - 1` points intermédiaires, en utilisant `pts[i-1]` et `pts[i+2]`, bornés aux extrémités. Garder le premier et le dernier point.
   - Dans `AddChain`, appliquer `Smooth(path, 3)` avant d'ajouter les points.
3. `LightningBolt.cs` : effilement.
   - Remplacer la largeur par segment par une largeur par point : nouvelle liste `_width` remplie dans `AddPoint`.
   - Dans `AddChain(from, path, width)`, la largeur d'un point vaut `width * (1 - 0.7 * i / (path.Count - 1))`.
   - Dans `Finish`, `CUSTOM1.z` de chaque sommet = `_width` de son point.
   - Largeurs de départ : `Setup`, tronc `3f` et branches `1f` ; `SetupSpider`, génération 0 `1.5f`, 1 `0.8f`, 2 et plus `0.5f`.
   - `_segments` peut garder son champ Width, ou le perdre : au choix, mais il ne sert plus au maillage.
4. `LightningBolt.cs` : retirer l'occlusion CPU.
   - Supprimer `Visibility()`, `VisualExtinction` et `VisibilitySamples`, ainsi que le calcul de `visibility` dans `Finish`.
   - Mettre `CUSTOM1.y = 1`.
   - Le paramètre `field` de `Finish` ne sert plus : le retirer et adapter les appels.
5. Mettre à jour les commentaires de classe et d'en-tête de shader : rendu opaque HDR, atténuation par les nuages via le compositeur, halo par le glow de l'environnement, splines Catmull-Rom effilées.

## Contraintes
- Code et commentaires en français, style existant (voir CLAUDE.md).
- Ne pas modifier la génération des arbres (`Subdivide`, `Wiggle`, la boucle de `SetupSpider`), sauf les largeurs passées à `AddChain`. Ne pas modifier `_Process`.
- Après toute modification du `.gdshader` : `"C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe" --headless --path . --import`.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] L'import ne sort aucune ligne `SHADER ERROR`.
- [ ] `GODOT --headless --path . res://Scenes/Debug/CloudShots.tscn --quit-after 200` ne sort ni `ERROR` ni `Exception` (hors fuites à la sortie).
- [ ] `grep -n "Visibility\|hidden_glow\|blend_add" Scripts/Clouds/LightningBolt.cs Shaders/Effects/lightning_channel.gdshader` ne renvoie rien.

## Retour attendu
- Liste des fichiers modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
