# Mission M8 : Ciel crépuscule, mer et lumière

## Objectif
Donner à l'arène l'ambiance « crépuscule rose et bleu » de la référence. Il faut un ciel en dégradé (bleu nuit au zénith, puis bleu, lavande et rose à l'horizon) avec des cirrus peints en traits, une mer sombre où le soleil bas trace une traînée de reflet, des points lumineux crème sur l'eau et des îlots en roche sombre.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`, en particulier « Construction des scènes » (`[Tool]`, conteneur `_Generated`).
- Référence visuelle :
  - **ciel** : en haut, bleu nuit `(0.02, 0.03, 0.12)` ; plus bas, bleu `(0.1, 0.35, 0.7)` puis bleu clair `(0.3, 0.55, 0.88)` ; à 15° environ au-dessus de l'horizon, lavande `(0.62, 0.58, 0.92)` ; à l'horizon, rose `(0.98, 0.72, 0.86)` ;
  - **cirrus** : longs traits fins, inclinés en diagonale, bleu clair et lavande, avec des touches rose saumon `(1.0, 0.55, 0.6)` près de l'horizon ;
  - **mer** : quasi noire bleutée `(0.006, 0.012, 0.04)`, avec des aplats peints un peu plus clairs `(0.02, 0.05, 0.11)`, une traînée de reflet lavande et blanc `(0.85, 0.8, 1.0)` sous le soleil, et des points lumineux crème `(0.95, 0.9, 0.75)` épars.
- Les nuages volumétriques existent déjà (`Scripts/Clouds/`) : ne pas y toucher. Ils lisent automatiquement la `DirectionalLight3D` de la scène (direction, couleur, énergie).
- Le générateur d'arène est `Scripts/Arena/ArenaBuilder.cs` (script `[Tool]`). Ses méthodes `BuildEnvironment()`, `BuildSea()` et `CreateIsland()` sont celles à modifier. Toutes les autres étapes restent telles quelles.

## Fichiers concernés
- Créer : `Shaders/Sky/sky_dusk.gdshader` (`shader_type sky`)
- Créer : `Shaders/Ocean/ocean.gdshader` (`shader_type spatial`)
- Modifier : `Scripts/Arena/ArenaBuilder.cs`, uniquement `BuildEnvironment`, `BuildSea` et les couleurs dans `CreateIsland`.
- Ne pas toucher : `Scripts/Clouds/*`, `Shaders/Clouds/*`, les scènes `.tscn` et les autres scripts.

## Étapes
1. **`sky_dusk.gdshader`** (shader de ciel Godot 4), avec des uniforms pour chaque couleur ci-dessus (`source_color`) et :
   - Un dégradé selon l'élévation `EYEDIR.y`, composé de `smoothstep` entre les bandes. Sous l'horizon, un bleu nuit très sombre `(0.01, 0.015, 0.05)`.
   - Des cirrus : projette la direction sur un plan (`EYEDIR.xz / max(EYEDIR.y, 0.05)`), applique un FBM de bruit de valeur 2D écrit dans le shader, **étiré** le long d'une direction diagonale (étirement d'environ 8× sur un axe), puis seuille-le en traits fins. Teinte bleu clair et lavande, plus rose près de l'horizon et du côté du soleil. Atténue les traits près de l'horizon (`EYEDIR.y < 0.03`).
   - Le soleil, via `LIGHT0_DIRECTION` et `LIGHT0_COLOR` : un halo doux rose-pêche autour du soleil bas, et un disque discret.
   - Des étoiles très faibles au zénith (hachage de `EYEDIR` quantifié), visibles seulement si `EYEDIR.y > 0.5`.
   - Un uniform `float sky_energy = 1.0` multiplie `COLOR`.
2. **`BuildEnvironment()`** :
   - `Sky` avec un `ShaderMaterial` (`sky_dusk.gdshader`), chargé par `GD.Load<Shader>("res://Shaders/Sky/sky_dusk.gdshader")`. `BackgroundMode = Sky`.
   - Lumière ambiante depuis le ciel (`AmbientLightSource = Sky`, énergie 0,6) et reflets depuis le ciel.
   - `TonemapMode = Filmic`, `TonemapExposure = 1.0` ; glow activé (intensité 0,6, bloom 0,05).
   - Brouillard : `FogEnabled`, couleur `(0.72, 0.62, 0.85)`, densité 0,0010, `FogSkyAffect = 0.15`, `FogAerialPerspective = 0.5`.
   - `DirectionalLight3D` : soleil bas d'environ 7° d'élévation, **placé vers -Z** (le joueur apparaît face à -Z et doit voir le soleil et son reflet devant lui). La lumière éclaire selon son -Z local, donc elle doit pointer vers +Z et légèrement vers le bas. Couleur `(1.0, 0.74, 0.8)`, énergie 1,1, ombres activées, `DirectionalShadowMaxDistance = 600`.
3. **`ocean.gdshader`** (spatial), sur le `PlaneMesh` de la mer (agrandis-le à 12 000 × 12 000, `SubdivideWidth` et `SubdivideDepth` à 0) :
   - `ALBEDO` = couleur de base sombre ; `ROUGHNESS` ≈ 0,12 ; `METALLIC` 0 ; `SPECULAR` 0,6. Le reflet du soleil et du ciel rose vient alors du rendu PBR de Godot.
   - `NORMAL_MAP` ou `NORMAL` perturbée par deux couches de bruit qui défilent dans des directions différentes (`TIME`), avec une intensité qui diminue avec la distance pour éviter le scintillement au loin.
   - Aplats peints : un bruit étiré horizontalement mélange la couleur de base avec la couleur plus claire.
   - Points lumineux : un bruit cellulaire (Worley) seuillé donne des points crème en `EMISSION`, qui scintillent lentement. Ils sont épars, de taille constante à l'écran (plus nombreux près de la caméra) et s'atténuent au loin.
   - Uniforms exposés pour toutes les couleurs, intensités et échelles.
4. **`CreateIsland()`** : roche ardoise sombre `(0.12, 0.13, 0.18)` (rugosité 0,9), dessus vert sarcelle sombre `(0.08, 0.17, 0.17)`.

## Contraintes
- Shaders Godot (`.gdshader`) : commentaires en français permis. Pas de texture externe, tout est procédural.
- Le shader du ciel doit rester léger : 4 octaves maximum pour les cirrus.
- Après modification d'un shader, lance l'import : `GODOT --headless --path . --import` (voir `CLAUDE.md`, « Outils »). Le jeu lancé hors éditeur ne réimporte pas les ressources.

## Critères de succès
- [ ] `dotnet build` passe.
- [ ] `GODOT --headless --path . --import` ne signale aucune erreur de shader.
- [ ] `GODOT --headless --editor --path . res://Scenes/Main.tscn --quit-after 200` ne signale aucune erreur.
- [ ] Capture d'écran : `GODOT --path . --write-movie <dossier temporaire>/f.png --fixed-fps 30 --quit-after 30`. Regarde la dernière image avec ton outil de lecture d'image et décris ce que tu vois : dégradé du ciel, traits de cirrus, mer sombre, reflet du soleil devant la caméra.

## Retour attendu
Liste des fichiers créés ou modifiés, un résumé de 3 lignes maximum, la description de la capture, les hypothèses faites. Pas de commit.

## Si bloqué
Si un shader ne compile pas, lis l'erreur de l'import et corrige. Pour un choix artistique non couvert ici, reste fidèle aux couleurs de la référence et signale-le.
