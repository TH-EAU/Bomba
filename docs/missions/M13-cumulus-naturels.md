# Mission M13 : Petits cumulus et congestus plus naturels (amas de lobes)

## Objectif
Les petites colonnes (cumulus et congestus de la grille locale) doivent ressembler à de **vrais cumulus** et non plus à des cylindres coiffés d'un dôme. On veut des amas bourgeonnants faits de lobes arrondis empilés, plus gros à la base et plus petits au sommet, à peu près aussi larges que hauts pour les petits cumulus, avec une silhouette irrégulière et un léger penchant.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`, en particulier les sections « Outils de vérification » et « Nuages ».
- Tout se passe dans `Scripts/Clouds/CloudField.cs` (indentation par **tabulations**) :
  - `struct Tower` (vers la ligne 208) : `X, Z` (position dans la tuile), `Radius`, `Top` (altitude du sommet), `Kind` (`Cumulus`, `Congestus`, `Cumulonimbus`), `Storm`, `Big`.
  - `PlaceLocalTowers` / `AddLocalTowers` créent les congestus, leurs satellites (de type `Cumulus`) et les petits cumulus. `PlaceGiantTowers` crée le cumulonimbus et ses satellites (la grille géante est désactivée par défaut, mais le code doit continuer de compiler et de fonctionner).
  - `BakeLocalSlice` remplit `near` avec les tours proches d'une colonne de voxels (test `if (d >= t.Radius * (t.Big ? 2.6f : 1.3f) + 2f * g.CellXZ)`, vers la ligne 618).
  - `WriteVoxel` appelle `TowerBody(t, d, y, warp, ref storm, ref updraft)`, avec `d` = distance horizontale enroulée (`WrappedDistance`).
  - `TowerBody`, branche `else` (vers la ligne 763) : **c'est la forme actuelle à remplacer** (cylindre effilé et dôme).
- Le SDF est calculé automatiquement à partir de la densité (`CloudGrid.Finish`). Tant que la densité reste dans [0, 1], il n'y a rien à faire de ce côté.

## Fichiers concernés
- Modifier : `Scripts/Clouds/CloudField.cs`, uniquement `struct Tower`, la création des tours non orageuses, le test de portée `near` dans `BakeLocalSlice` et `BakeGiantSlice`, l'appel à `TowerBody` dans `WriteVoxel`, la branche non orageuse de `TowerBody`, et les valeurs par défaut des exports `Cumulus*` et `Congestus*`.
- Ne pas toucher : la branche `if (t.Storm)` de `TowerBody` (cumulonimbus), la nappe (calcul `layer`), les shaders, `CloudRenderer.cs`, `CloudGrid.cs`, les scènes.

## Étapes
1. **Lobes** : ajoute à `Tower` un champ `public Vector4[] Lobes;` (x = décalage X en m, y = décalage Z en m, z = altitude du centre du lobe en m, w = rayon en m) et `public float Reach;` (rayon horizontal d'influence en m).
2. Ajoute une méthode `private void BuildLobes(ref Tower t, RandomNumberGenerator rng)`, appelée pour **chaque tour non orageuse** juste avant son ajout à la liste (petits cumulus, congestus, satellites des congestus et du cumulonimbus). Avec `H = t.Top - LayerBottom` :
   - nombre de lobes : `Cumulus` de 5 à 8, `Congestus` de 8 à 13 ;
   - penchant : une direction aléatoire × `rng.RandfRange(0f, 0.25f)`, appelée `lean` (sans unité) ;
   - pour chaque lobe i : une hauteur relative `f` tirée dans [0,1[, transformée par `f = 0.08f + 0.82f * f * f` pour concentrer les lobes en bas ;
     - rayon `r = t.Radius * Mathf.Lerp(0.8f, 0.32f, f) * rng.RandfRange(0.8f, 1.2f)` ;
     - centre vertical `LayerBottom + f * H` ;
     - décalage horizontal = direction aléatoire × `t.Radius * (1f - f) * rng.RandfRange(0f, 0.5f)`, plus `lean * f * H` ;
   - force toujours un **lobe de base** (`f = 0.08`, rayon `0.85 × Radius`, sans décalage) et un **lobe sommital** (`f = 0.9`, rayon `0.3 × Radius`, décalé de `lean * 0.9 * H`) ;
   - `t.Reach` = max sur les lobes de (norme du décalage horizontal + rayon), arrondi au-dessus.
3. **Portée** : dans `BakeLocalSlice` et `BakeGiantSlice`, pour les tours non orageuses, utilise `Mathf.Max(t.Reach, portée_actuelle)` dans le test qui remplit `near`. La portée actuelle couvre la jupe des congestus : garde-la.
4. **Décalage horizontal** : ajoute `private static Vector2 WrappedDelta(float x0, float z0, float x1, float z1, float tile)`, qui renvoie (x0 − x1, z0 − z1) ramené dans [−tile/2, tile/2] sur chaque axe. Passe ce delta à `TowerBody` (nouveau paramètre `Vector2 delta`) en plus de `d`.
5. **Forme** : dans `TowerBody`, branche non orageuse, remplace le calcul par :
   - pour chaque lobe, avec `dx = delta.X - lobe.x`, `dz = delta.Y - lobe.y` et `dy = (y - lobe.z) / 0.8f` (lobe aplati verticalement), `dist = Mathf.Sqrt(dx*dx + dy*dy + dz*dz) * (1f + 0.35f * warp)` ;
   - `body = max sur les lobes de (1f - SmoothStep(0.5f * lobe.w, lobe.w, dist))` ;
   - `return body * SmoothStep(-0.05f, 0.04f, h)` (la base reste posée sur le bas de la nappe) ;
   - garde le test `if (h < -0.05f || h > 1.08f) return 0f;` existant en tête de fonction, avant la branche (ajuste la borne haute à 1.15 si des lobes dépassent le sommet) ;
   - le calcul de l'ascendance (`updraft`) reste inchangé.
6. **Proportions par défaut** :
   - `CumulusRadiusMin = 140`, `CumulusRadiusMax = 320`, `CumulusTopMin = 550`, `CumulusTopMax = 850` ;
   - `CongestusRadiusMin = 300`, `CongestusRadiusMax = 500`, `CongestusTopMin = 1100`, `CongestusTopMax = 1900`.

## Contraintes
- Aucune modification hors des éléments listés. Conserve les commentaires XML en français.
- Coût : le temps de calcul (`[CloudField] calcul … ms` dans la sortie) doit rester **sous 3 000 ms** (il est d'environ 2 200 ms aujourd'hui).
- Pas plus de **3 cycles** de capture et réglage : vise juste du premier coup en suivant les formules, puis ajuste seulement si une capture montre un défaut net.

## Critères de succès (commandes dans `CLAUDE.md`, `GODOT` = exécutable Godot)
- [ ] `dotnet build` passe.
- [ ] `GODOT --headless --path . res://Scenes/Debug/CloudProbe.tscn -- --out=<dossier>` affiche `surestimations 0`, et le temps de calcul est inférieur à 3 000 ms.
- [ ] Captures avant et après : `GODOT --path . --write-movie <dossier>/f.png --fixed-fps 30 res://Scenes/Debug/CloudShots.tscn`. Fais la capture « avant » **avant** toute modification. Compare les frames 12, 27 et 102 avant et après, et décris les formes (doivent être arrondies, bourgeonnantes, non cylindriques).
- [ ] `GODOT --path . res://Scenes/Debug/CloudShots.tscn -- --fps --shot-frames=150` affiche au moins 58 FPS partout.
- [ ] `GODOT --headless --path . res://Scenes/Debug/DetectionProbe.tscn` : phase 1 avec plus de 600 frames repéré, phase 2 avec au plus 31.
- [ ] `GODOT --headless --editor --path . res://Scenes/Main.tscn --quit-after 200` : aucune erreur.

Utilise des dossiers de capture sous `C:\Users\theau\AppData\Local\Temp\claude\0f386f85d8cb4994\da86c8bf-d965-404c-b168-50384c0d74c8\scratchpad\m13`.

## Retour attendu
Liste des modifications, un résumé de 3 lignes maximum, le temps de calcul et les FPS mesurés, la description avant/après des 3 frames, les hypothèses faites. **Pas de commit** : le chef de projet relit puis committe.

## Si bloqué
Si une capture montre des nuages coupés net, en marches d'escalier, ou une erreur de build dans un fichier qui n'est pas le tien, arrête-toi et décris le problème au lieu de bricoler.
