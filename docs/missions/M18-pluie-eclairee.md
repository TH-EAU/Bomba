# Mission : pluie qui apparaît en douceur, disparaît vraiment, et réagit à la lumière

## Objectif
La pluie et la grêle apparaissent et disparaissent progressivement selon l'intensité. Elles disparaissent totalement hors de la pluie. Les gouttes sont bleu clair, translucides et éclairées : le soleil, les éclairs et l'ombre agissent sur elles, avec un reflet obtenu par une normale de cylindre calculée dans le shader.

## Contexte
- `Scripts/Effects/Precipitation.cs` crée deux couches `GPUParticles3D` (pluie, grêle). Aujourd'hui :
  - `Lifetime` 1000, `Explosiveness` 1 ;
  - `_Process` règle `AmountRatio = intensité` et `Emitting = intensité > seuil`.
- Défauts constatés par l'utilisateur :
  - (1) apparition brusque : toutes les particules sont émises d'un coup ;
  - (2) la pluie reste après être sorti de la zone : `Emitting = false` ne tue pas les particules vivantes, qui vivent 1000 s ;
  - (3) le rendu est blanc et non éclairé.
- `Shaders/Effects/precip_process.gdshader` (shader `particles`, boîte repliée autour de la caméra, `CUSTOM.xyz` = vitesse).
- `Shaders/Effects/precip_draw.gdshader` (shader `spatial` `unshaded, blend_add`, trait étiré face caméra, écrit `POSITION`).
- Le soleil (`Scripts/Core/TwilightSun.cs`) n'éclaire que la couche de rendu 2 (`LightCullMask = 1 << 1`). Les éclairs ajoutent des `OmniLight3D` qui éclairent toutes les couches.

## Fichiers concernés
- Modifier : `Scripts/Effects/Precipitation.cs`, `Shaders/Effects/precip_process.gdshader`, `Shaders/Effects/precip_draw.gdshader`
- Ne pas toucher : tout le reste (scènes, autres scripts, TwilightSun, nuages).

## Étapes
1. `precip_process.gdshader` : dans `start()`, stocker un hachage fixe de la particule (0..1, à partir de `INDEX` et `RANDOM_SEED`) dans `CUSTOM.w`. `process()` ne doit jamais l'écraser : n'écrire que `CUSTOM.xyz`.
2. `precip_draw.gdshader` :
   - `render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_schlick_ggx;` : retirer `unshaded` et `blend_add`, garder le brouillard ;
   - nouvel uniform `float intensity` (0..1). Dans `fragment()`, `float keep = smoothstep(INSTANCE_CUSTOM.w - 0.15, INSTANCE_CUSTOM.w, intensity);`. Passer `INSTANCE_CUSTOM.w` du vertex au fragment par un varying ;
   - `ALBEDO = color.rgb;` `ALPHA = opacity * keep * (1 - abs(UV.x*2-1)) * mix(1.0, 0.4, UV.y);` `ROUGHNESS = 0.12;` `METALLIC = 0.0;` `SPECULAR = 0.6;` ;
   - normale de petit cylindre (la « normal map » demandée, calculée et non texturée) : dans `vertex()`, passer en varyings (espace vue) la direction latérale `perp` et la direction vers la caméra `to_cam` déjà utilisées pour construire le quad ; dans `fragment()`, `float x = UV.x * 2.0 - 1.0; NORMAL = normalize(perp * x + to_cam * sqrt(max(1.0 - x*x, 0.0)));` ;
   - `EMISSION` : rien.
3. `Precipitation.cs` :
   - les couches émettent en permanence : `Emitting = true` à la création, `AmountRatio = 1`, et ne plus les modifier dans `_Process` ;
   - `_Process` passe l'intensité lissée au shader de dessin (uniform `intensity`) et règle `Visible = intensité > 0.001` (la couche est masquée hors de la pluie) ;
   - les deux `GPUParticles3D` ont `Layers = 1 | 2` (couche 2 pour être éclairées par le soleil, comme les véhicules) ;
   - couleurs par défaut : `RainColor = new Color(0.62f, 0.78f, 0.95f)`, `RainOpacity = 0.55f` ; `HailColor = new Color(0.88f, 0.94f, 1f)`, `HailOpacity = 0.85f` ;
   - supprimer `EmitThreshold` et toute logique `Emitting` / `AmountRatio` devenue inutile ;
   - mettre à jour les commentaires (apparition progressive, particules permanentes, rendu éclairé).

## Contraintes
- Code et commentaires en français (dans les `.gdshader` aussi), style existant (voir CLAUDE.md).
- Après modification des shaders : `"C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe" --headless --path . --import`.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] L'import ne sort aucune ligne `SHADER ERROR`.
- [ ] `GODOT --headless --path . --quit-after 300` ne sort ni `ERROR` ni `Exception` (hors fuites à la sortie).
- [ ] `grep -n "unshaded\|blend_add" Shaders/Effects/precip_draw.gdshader` ne renvoie rien.

## Retour attendu
- Fichiers modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
