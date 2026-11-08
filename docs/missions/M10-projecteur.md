# Mission M10 : Projecteur détecteur (démonstration de la cachette)

## Objectif
Créer un projecteur de défense : une tour sur un pilier rocheux dont le faisceau lumineux visible balaie le ciel. S'il voit le joueur (dans le cône, sans obstacle et **pas masqué par les nuages**), il passe au rouge, suit le joueur et signale la détection. Le joueur peut s'en cacher dans les nuages.

## Contexte
- Projet Godot 4.5 C#, racine `E:\Godot Games\bomba`. Lis d'abord `CLAUDE.md`, en particulier « Construction des scènes » (`[Tool]`, conteneur `_Generated`, protections `Engine.IsEditorHint()`).
- Lis attentivement :
  - `Scripts/Clouds/CloudField.cs` : `CloudField.Instance.GetTransmittance(Vector3 from, Vector3 to)` renvoie 0..1 (1 = vue dégagée, 0 = masqué par le nuage).
  - `Scripts/Core/Detection.cs` : appelle `Detection.ReportSpotted()` chaque frame où le joueur est vu.
  - `Scripts/Core/IVehicleTracker.cs` : le projecteur reçoit le véhicule du joueur via `Target`.
- Ambiance : crépuscule rose et bleu. Faisceau bleu pâle `(0.55, 0.75, 1.0)` au repos, rouge `(1.0, 0.15, 0.1)` quand le joueur est repéré. Feux d'obstacle rouges sur la tour (référence : base hérissée de feux rouges).

## Fichiers concernés
- Créer : `Scripts/Arena/Searchlight.cs`
- Créer : `Shaders/Effects/light_beam.gdshader`
- Ne pas toucher : tout autre fichier, scènes comprises (le chef de projet placera les projecteurs).

## Étapes

### 1. `light_beam.gdshader`
Shader spatial pour un cône de lumière visible : `render_mode unshaded, blend_add, depth_draw_never, cull_disabled`.
- Uniforms : `beam_color` (`source_color`), `intensity = 1.5`, `length = 500.0`.
- Le cône est un `CylinderMesh`. Utilise `UV.y` ou la position de sommet locale pour connaître la distance le long du faisceau.
- Alpha (via la couleur en mode additif) : décroît le long du faisceau (fort près de la lampe, nul au bout), avec des bords doux. L'atténuation des bords vient du fresnel : `pow(abs(dot(NORMAL, VIEW)), 1.5)`, de sorte que les bords vus de côté soient plus transparents.
- Atténuation de proximité avec la scène (`hint_depth_texture`) : le faisceau s'estompe là où il touche une surface opaque, pour éviter les coupures nettes.

### 2. `Searchlight`
Namespace `Bomba.Arena`, `[Tool] public partial class Searchlight : Node3D, IVehicleTracker`.
- Exports : `Range = 650f`, `BeamAngleDegrees = 9f` (demi-angle), `SweepYawDegrees = 70f` (amplitude de balayage de part et d'autre du cap initial), `SweepPeriod = 9f` (s), `BeamPitchDegrees = 18f` (élévation au repos), `TrackSpeedDegrees = 35f` (°/s quand il suit), `LoseDelay = 2.5f` (s avant de reprendre le balayage), `VisibilityThreshold = 0.35f` (transmittance minimale pour voir), `PillarHeight = 90f` (m, pilier rocheux sous la tour).
- Construction en code (sous un conteneur `_Generated`, comme `ArenaBuilder`), visible dans l'éditeur :
  - Un pilier rocheux : `CylinderMesh` évasé vers le bas (roche ardoise `(0.12, 0.13, 0.18)`) qui descend de `PillarHeight` sous l'origine du nœud. `StaticBody3D` avec forme convexe.
  - Une tour : poutrelle métallique sombre (`BoxMesh` fins ou `CylinderMesh`), d'environ 12 m de haut.
  - En haut, une tête de projecteur orientable (`Node3D` « Head ») avec un tambour (`CylinderMesh`) et, dans la tête :
    - une `SpotLight3D` (`SpotRange = Range`, `SpotAngle = BeamAngleDegrees`, énergie 8, ombres désactivées, couleur du faisceau) ;
    - le cône visible : `CylinderMesh` (TopRadius 0,6, `BottomRadius = Range × tan(BeamAngle)`, `Height = Range`), décalé pour partir de la lampe dans la direction de visée (le -Z de la tête), avec le `ShaderMaterial` du faisceau.
  - 4 à 6 feux d'obstacle rouges : petites sphères émissives rouges qui clignotent lentement (`_Process`), réparties sur la tour.
- Logique (seulement hors éditeur, `_PhysicsProcess`) :
  - **Balayage** : lacet = cap initial + `SweepYawDegrees × sin(2π t / SweepPeriod)`, tangage = `BeamPitchDegrees`.
  - **Détection** du joueur (`Target` valide), avec `lamp` = position globale de la tête et `toTarget` = cible − lamp :
    - distance < `Range` ;
    - angle entre la visée et `toTarget` < `BeamAngleDegrees × 1.4` ;
    - pas d'obstacle : `PhysicsRayQueryParameters3D` de la lampe à la cible, qui exclut le véhicule cible et le pilier ;
    - `CloudField.Instance == null` ou `GetTransmittance(lamp, cible) ≥ VisibilityThreshold`.
  - Si toutes les conditions sont vraies, le joueur est **repéré** : appelle `Detection.ReportSpotted()`, passe le faisceau et la lumière au rouge, et la tête **suit** la cible (rotation limitée à `TrackSpeedDegrees` par seconde).
  - S'il est perdu (non vu pendant `LoseDelay`), le projecteur repasse au bleu et reprend le balayage en douceur depuis son orientation actuelle.

## Contraintes
- La logique de jeu est protégée par `if (Engine.IsEditorHint()) return;`. Le visuel se construit aussi dans l'éditeur.
- Aucune modification d'autres fichiers. Si une API manque, signale-le.

## Critères de succès
- [ ] `dotnet build` passe.
- [ ] `GODOT --headless --path . --import` ne signale aucune erreur de shader.
- [ ] Crée une scène temporaire `Scenes/Debug/SearchlightTest.tscn` contenant uniquement un `Searchlight`. Ouvre-la avec `GODOT --headless --editor --path . res://Scenes/Debug/SearchlightTest.tscn --quit-after 200` : aucune erreur.

## Retour attendu
Liste des fichiers créés, un résumé de 3 lignes maximum, les hypothèses faites. Pas de commit.

## Si bloqué
Si `dotnet build` échoue à cause d'un fichier verrouillé, relance. S'il échoue à cause d'un fichier qui n'est pas le tien, signale-le sans y toucher.
