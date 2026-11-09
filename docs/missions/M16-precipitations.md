# Mission : pluie et grêle en particules autour de la caméra

## Objectif
Dans une zone de pluie, des traits de pluie (et, en altitude dans l'orage, de la grêle) entourent la caméra en permanence. Ils sont emportés par le vent et étirés selon leur vitesse relative à la caméra : au repos, des traits verticaux ; en vol, des traits qui foncent vers le joueur.

## Contexte
- `Scripts/Clouds/StormWeather.cs` (à lire, **ne pas modifier**) : `StormWeather.Instance` (peut être nul), `RainAt(Vector3)` et `HailAt(Vector3)` dans 0..1, `Turbulence01(Vector3)` dans 0..1.
- `Bomba.Core.Wind.GetWindAt(Vector3)` (statique) : vent total en m/s au point.
- `Scripts/Effects/ScreenDroplets.cs` : gouttes sur l'écran. `_coverage` monte avec la densité de nuage (`WetRate`).
- Interface `Bomba.Core.IVehicleTracker` (groupe `IVehicleTracker.GroupName`, propriété `Vehicle Target`) : voir l'utilisation dans `ScreenDroplets.cs`.
- Godot 4.5 C#. `GPUParticles3D.AmountRatio` existe.

## Fichiers concernés
- Créer : `Scripts/Effects/Precipitation.cs`, `Shaders/Effects/precip_process.gdshader`, `Shaders/Effects/precip_draw.gdshader`
- Modifier : `Scripts/Effects/ScreenDroplets.cs` (la pluie mouille l'écran)
- Ne pas toucher : `Scenes/**` (aucun `.tscn`), `StormWeather.cs`, les nuages, les éclairs, les véhicules, `project.godot`. Le chef de projet ajoutera le nœud dans `Main.tscn`.

## Étapes
1. `precip_process.gdshader` (`shader_type particles;`), une « boîte » qui suit la caméra :
   - uniforms : `vec3 camera_pos`, `vec3 box_size`, `vec3 base_velocity` (chute + vent, m/s), `float turbulence` (m/s), `float time_s` ;
   - `start()` : si `RESTART_POSITION`, placer la particule au hasard dans la boîte centrée sur `camera_pos`. Le hasard vient d'un hachage de `INDEX` et `RANDOM_SEED`, en trois valeurs 0..1 différentes. Mettre `TRANSFORM` à l'identité avec cette position ;
   - `process()` :
     - `vec3 v = base_velocity + turbulence * vec3(sin(time_s*1.7 + h1*40.0), 0.3*sin(time_s*2.3 + h2*40.0), sin(time_s*1.3 + h3*40.0));` (h1 à h3 = hachages fixes de la particule) ;
     - `TRANSFORM[3].xyz += v * DELTA;` ;
     - repli dans la boîte : `vec3 rel = TRANSFORM[3].xyz - camera_pos; rel = mod(rel + box_size*0.5, box_size) - box_size*0.5; TRANSFORM[3].xyz = camera_pos + rel;` ;
     - `CUSTOM.xyz = v; VELOCITY = vec3(0.0);`.
2. `precip_draw.gdshader` (`shader_type spatial;`, `render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;`), un trait par particule (QuadMesh 1×1, UV dans 0..1) :
   - uniforms : `vec3 camera_velocity`, `float streak_time` (s), `float width` (m), `float min_px` (largeur minimale en pixels), `vec4 color : source_color`, `float opacity` ;
   - `vertex()` :
     - `vec3 head = (MODEL_MATRIX * vec4(0,0,0,1)).xyz;` (en coordonnées monde : particules non locales) ;
     - `vec3 rel_vel = INSTANCE_CUSTOM.xyz - camera_velocity;` et `vec3 tail = head - rel_vel * streak_time;` ;
     - passer en espace vue (`VIEW_MATRIX`), prendre le point `mix(head, tail, UV.y)`, puis le décaler perpendiculairement au trait (produit vectoriel de la direction du trait et de la direction vers la caméra) de `(UV.x - 0.5) * max(width, min_px * taille_pixel)`, avec `taille_pixel = 2*profondeur / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y)` ;
     - écrire `POSITION = PROJECTION_MATRIX * vec4(point_vue, 1.0);` ;
   - `fragment()` : `ALBEDO = color.rgb * opacity * (1 - abs(UV.x*2-1)) * mix(1.0, 0.3, UV.y);` (plus clair en tête).
3. `Precipitation.cs` (`public partial class Precipitation : Node3D, IVehicleTracker`) :
   - au `_Ready`, s'ajouter au groupe et créer deux `GPUParticles3D` enfants, pluie et grêle :
     - `LocalCoords = false`, `Lifetime = 1000`, `Explosiveness = 1`, `OneShot = false`, `FixedFps = 0`, `Interpolate = false` ;
     - `VisibilityAabb = new Aabb(new Vector3(-1e5f,-1e5f,-1e5f), new Vector3(2e5f,2e5f,2e5f))` ;
     - `ProcessMaterial` = un `ShaderMaterial` sur `precip_process`, `DrawPass1` = `QuadMesh` avec `MaterialOverride`, un `ShaderMaterial` sur `precip_draw` ;
   - exports (groupe « Pluie ») :

     | Export | Défaut |
     |---|---|
     | `RainAmount` | 4000 |
     | `RainBox` | (60, 40, 60) |
     | `RainFallSpeed` | 9 m/s |
     | `RainStreakTime` | 0.05 |
     | `RainWidth` | 0.02 |
     | `RainColor` | (0.75, 0.8, 0.9) |
     | `RainOpacity` | 0.6 |

   - exports (groupe « Grêle ») :

     | Export | Défaut |
     |---|---|
     | `HailAmount` | 800 |
     | `HailBox` | (40, 30, 40) |
     | `HailFallSpeed` | 25 m/s |
     | `HailStreakTime` | 0.015 |
     | `HailWidth` | 0.06 |
     | `HailColor` | (0.95, 0.97, 1) |
     | `HailOpacity` | 0.9 |

   - exports (groupe « Réglage ») : `IntensitySharpness` 1.5, `TurbulenceScale` 12 (m/s à turbulence 1), `MinPixels` 1 ;
   - `_Process` :
     - caméra : `GetViewport().GetCamera3D()` ; sa vitesse = écart de position / dt (premier frame : zéro) ;
     - intensités cibles `RainAt` et `HailAt` au point de la caméra, lissées exponentiellement avec `IntensitySharpness` ;
     - pour chaque couche : `AmountRatio = intensité` et `Emitting = intensité > 0.01` ;
     - mise à jour des uniforms : `camera_pos`, `box_size`, `base_velocity = Wind.GetWindAt(caméra) + Vector3.Down * FallSpeed`, `turbulence = Turbulence01 * TurbulenceScale`, `time_s`, `camera_velocity`, `streak_time`, `width`, `min_px`, `color`, `opacity`.
4. `ScreenDroplets.cs` : export `RainWetRate` (2.0). Dans `_Process`, si `StormWeather.Instance` existe : `_coverage += StormWeather.Instance.RainAt(position de la caméra) * RainWetRate * dt`, juste après la ligne qui ajoute la densité de nuage (avant le séchage). Ajouter `using Bomba.Clouds;` si nécessaire.

## Contraintes
- Code et commentaires en français, style existant, exports groupés (voir CLAUDE.md).
- Après toute modification de shader : `"C:\Program Files (x86)\Godot Engine\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe" --headless --path . --import`.
- Ne pas toucher aux fichiers interdits. Si `dotnet build` échoue sur un fichier d'une autre mission (travail en parallèle), le signaler sans le corriger.

## Critères de succès
- [ ] `dotnet build` passe sans erreur.
- [ ] L'import ne sort aucune ligne `SHADER ERROR`.
- [ ] Une scène de test temporaire (Node3D racine + Camera3D + un nœud `Precipitation`), lancée en headless 120 frames, ne sort ni `ERROR` ni `Exception`. La supprimer ensuite.

## Retour attendu
- Fichiers créés ou modifiés.
- Résumé de 3 lignes maximum.
- Problèmes rencontrés, ou hypothèses faites.
- Ne pas committer.

## Si bloqué
S'arrêter et signaler le blocage plutôt que deviner une décision de design.
