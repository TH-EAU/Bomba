# Journal des décisions

## 2026-11-07 : POC vol

- **3D libre**, clavier + manette, physique Jolt à 60 Hz avec interpolation. Choisi par l'utilisateur.
- **Véhicule = coque + modules.** Les forces sont appliquées à la position de chaque module, donc le couple vient du placement. `Vehicle` ne connaît pas ses moteurs.
- **Le lacet tourne autour de la verticale du monde**, pas de celle de l'engin. Sinon, virer en étant incliné fait piquer le nez : la sonde a mesuré un tangage de 78° en virage.
- **Maintien d'assiette en tangage** (`PitchHold`). L'asservissement en vitesse angulaire seul laisse le nez dériver sous un couple constant. L'assiette maintenue est plafonnée à ±60°. `PitchAutoLevel` (retour à l'horizontale) est activé pour la Guêpe et le Zeppelin.
- **Les lignes de poussée passent par le centre de masse.** Un propulseur au-dessus du centre de masse faisait piquer le Rafiot et la Guêpe jusqu'à la mer.
- **Gouvernail au niveau du centre de masse.** Placé haut, il créait un roulis inverse en virage (aile extérieure qui monte).
- **Panne sèche des fusées** : le moteur est verrouillé jusqu'à 30 % de carburant. Sinon, la recharge le relançait en boucle (bégaiement).
- **Spawn à 80 m avec une vitesse de lancement** (`LaunchSpeed`, 25 m/s pour le Rafiot), et aucun îlot à moins de 220 m. Sans cela, le charbon, lent à démarrer, faisait chuter le Rafiot d'environ 45 m.
- **Portance du Rafiot plafonnée** (`MaxLift` 210 N) pour qu'il ne grimpe pas tout seul à pleine vitesse.
- **Touches physiques** : la disposition AZERTY est gérée automatiquement.
- **Pas de `global.json`.** Épingler le SDK 9 cassait l'analyse du projet par VS Code / C# Dev Kit (MSB4237 : NuGetSdkResolver réclamant `System.Runtime 10.0.0.0`). Le projet compile avec le SDK installé par défaut (10).

## 2026-11-07 : phase visuelle (nuages, crépuscule)

- **Nuages = un seul `CloudField`** (densité, SDF, orage, ascendance), calculé sur CPU en environ 0,8 s. Le rendu (texture 3D) et le gameplay (`Sample`, `GetTransmittance`) lisent les mêmes données : ce qu'on voit est exactement ce qui cache.
- **SDF conservateur** : la distance extérieure est diminuée d'une cellule. La sonde `CloudProbe` vérifie qu'il ne surestime jamais (0 surestimation mesurée), sans quoi le ray marching traverserait les nuages.
- **Rendu en demi-résolution** (CompositorEffect, avant les objets transparents) pour la GTX 1050 Ti : 60 FPS stables mesurés à 1600 × 900, au-dessus, dans et sous la couche.
- **Projection Godot** : celle du compositor est déjà corrigée Vulkan (Y retourné, Z inversé en [0,1]). On utilise donc `ndc = (uv·2−1, depth)` directement.
- **Extinction 0,12/m** : à 0,05, les détails de 15 à 30 m s'étalaient (nuages flous). Visibilité au cœur ≈ 8 m.
- **Fondu près de la caméra** (18 m) : dans un nuage, on voit toujours son engin.
- **Projecteurs au-dessus de la couche** (lampe vers 90 m). Placés dedans, ils étaient aveugles. La sonde `DetectionProbe` vérifie : repéré à découvert, jamais dans le nuage.
- **Environnement** : `GlowBloom = 0` et brouillard très faible (0,00012). Le bloom plein écran et le brouillard délavaient la mer sombre en lavande.
- **Mer** : `ambient_light_disabled`, sans reflet de ciel PBR (qui la rendait lavande en incidence rasante), remplacé par un fresnel maîtrisé. Le reflet direct du soleil donne la traînée lumineuse.

## 2026-11-07 : détail des nuages, temporel, orages, gouttes

- **Deux passes de détail Perlin-Worley** (référence uhawk), selon le schéma Horizon Zero Dawn / Nubis :
  - texture de forme 64³ : Perlin-Worley + Worley ×3, période 220 m ;
  - texture de détail 32³ : Worley ×3, période 48 m, effilochée en bas et boursouflée en haut.

  L'érosion ne fait que retirer de la densité, ce qui préserve la garantie du SDF.
- **Saturation `1 − exp(−4d)`** de la densité : les bords des bourgeons se découpent au lieu de s'étaler sur 30 m.
- **Le rayon d'ombre lit la forme Perlin-Worley** (4 premiers pas) : c'est l'auto-ombrage des bourgeons qui donne le relief.
- **Accumulation temporelle** en demi-résolution : reprojection à la profondeur moyenne du nuage, et historique borné par le voisinage 3×3 (poids courant 0,12). Le grain disparaît. Toujours 60 FPS sur la GTX 1050 Ti.
- **Orages** : cœurs étendus à 95 % du rayon des tours orageuses, masse bleu-gris ardoise, flash d'éclair plus fort et plus large. Les traits de foudre sont **opaques** pour être correctement cachés par les nuages ; rayon de 1,4 m pour être lisibles à 500 m.
- **Gouttes d'écran** : clairsemées, réfraction inversée, teinte neutre. Une première version teintée de bleu, avec gros reflets, faisait « bulles de savon ».

## 2026-11-08 : échelle réaliste du ciel

- **Volume nuageux** : 12,3 km de période horizontale, de 250 à 4 600 m d'altitude, grille de 192 × 96 × 192. **Maillage vertical non uniforme** (altitude = min + H·v^1,8) : cellules d'environ 25 m dans la couche basse et d'environ 70 m en altitude. La transformée de distance utilise les altitudes réelles des cellules (Felzenszwalb sur positions quelconques). Aucune surestimation du SDF mesurée.
- **Mer de nuages** à 320-470 m, en plaques de 1 à 3 km. **12 cumulus congestus** (rayon 220-480 m, sommet 0,9-1,7 km). **4 cumulonimbus** (rayon 650-1 200 m, sommet 2,8-4,4 km, enclume ×2,7 en largeur, presque plate).
- **Jeu sous et au-dessus du plafond** : apparition à 200 m, sous la couche. Les projecteurs sont sur des piliers bas, près de la mer ; ils visent le plafond (28°, portée 1 100 m) et on leur échappe en montant dans les nuages.
- **Rendu** : vue jusqu'à 30 km, pas intérieur qui double tous les 700 m, rayon d'ombre de plus de 600 m, détail Perlin-Worley sur une période de 900 m, brouillard et perspective aérienne réglés pour des distances de plusieurs kilomètres. 60 FPS mesurés sur les 7 plans fixes.
- **Objets transparents et nuages** : les nuages sont rendus avant les objets transparents. Les faisceaux de projecteur calculent donc eux-mêmes leur transmittance à travers les nuages : ils lisent le même champ via `CloudField.CloudTexture` et `ApplyCloudUniforms`. Les sphères translucides des zones de vent sont désactivées (elles traversaient les nuages).
- **« CACHÉ » à densité ≥ 0,45** (au lieu de 0,25) : en bord de couche, un projecteur voyait encore à travers.
- **Coût** : le calcul du champ prend environ 3 s au démarrage (contre 0,8 s avant). Une mise en cache sur disque est possible si cela gêne.

## 2026-11-08 : forme des nuages (référence photo du cumulonimbus)

- **Cumulonimbus en sablier** : base large, fût étranglé à 55 % du rayon vers 12-42 % de la hauteur, puis évasement à partir de 60 % jusqu'à une enclume ×2,7 au dessous concave, avec un dôme protubérant au centre. **2 seulement** par période de 12 km : rares et inquiétants.
- **Trois familles** : 2 cumulonimbus, 8 congestus et environ 70 petits cumulus, dont 8 satellites au pied de chaque cumulonimbus et 3 autour de chaque congestus. Les petits cumulus sont effilés vers le haut avec un sommet en dôme, et non cylindriques.
- **Relief de la nappe vue d'au-dessus** : bosses Worley à deux échelles (≈ 380 m et 190 m, jusqu'à 150 m de haut), en plus des cumulus qui en émergent.
- **Transition nappe → colonne** : au pied des grandes colonnes, la nappe devient continue et se soulève, jusqu'à 220 m pour les cumulonimbus. La « jupe » s'étend jusqu'à 2,5 × le rayon.
- Les cumulonimbus visibles en nombre à l'horizon sont des répétitions du motif de 12 km. Dans la zone de jeu, on en croise un ou deux.

## 2026-11-08 : colonnes rares, cumulonimbus géants

- **Deux grilles superposées** (`Scripts/Clouds/CloudGrid.cs`), unies au rendu et au gameplay (SDF min, canaux max) :
  - **locale** (12,3 km, 192 × 96 × 192) : nappe, environ 36 petits cumulus et **2 congestus** (avec leurs satellites) ;
  - **géante** (49,2 km = 4 × locale, 128 × 64 × 128, jusqu'à 14 km d'altitude, exposant vertical 2,2) : **1 cumulonimbus** de 8 à 11 km de rayon et 10 à 13 km de haut, 14 satellites de 1,5 à 4,5 km de haut, et la nappe soulevée jusqu'à environ 1 km à son pied.

  Une seule grille de 50 km aurait des cellules de 260 m, ce qui aurait détruit la nappe.
- **Hauteur portée à 10-13 km** (au lieu de ×10 sur le rayon seulement) : avec 10 km de rayon et 4 km de haut, on aurait une galette, pas une colonne. L'enclume ne s'élargit plus que de ×1,8 : à ×2,7, elle aurait dépassé la période et se serait chevauchée elle-même.
- **Échelle du détail croissante avec l'altitude** : ×4 au-dessus de 5 km, où ne reste que le géant. Des bourgeons de 200 m sur un nuage de 20 km donnaient un duvet uniforme.
- **SDF hors tranche = distance exacte à la tranche**, sans marge ajoutée : la marge d'une cellule pouvait surestimer la distance au nuage.
- **Distances de vue** : 60 km pour les nuages, 90 km pour la caméra, brouillard des nuages divisé par deux.
- **Éclairs nuage-sol** : sous un cumulonimbus, un éclair qui sort du nuage frappe la mer dans 60 % des cas.
- Calcul au démarrage : environ 2,9 s. 60 FPS sur les 7 plans fixes.

## 2026-11-08 : un seul cumulonimbus, LOD du bruit

- **Un seul cumulonimbus dans tout le monde** : la grille géante ne se répète plus (`CloudGrid.Repeat = false`). Son domaine de 49 km est centré sur l'orage et placé selon `CumulonimbusDirection`, avec le bord de sa base à `GiantSpawnClear` (4 km) du spawn. Hors du domaine, le SDF vaut la distance exacte au domaine.
- **La dérive n'est plus ramenée modulo une période** : sinon l'orage sauterait de 49 km au bouclage. Elle avance d'environ 9 km par heure.
- **Jupe de l'orage limitée à 1,25 R** : à 1,8 R, elle épaississait la nappe jusqu'à 1 km au-dessus de la zone de départ.
- **LOD du bruit** :
  - le détail fin s'estompe à partir de 1,5 km et n'est plus lu au-delà de 6 km (`LodFineDistance`) ;
  - au-delà de 20 km (`LodShapeDistance`), les rayons d'ombre n'utilisent plus la forme.
  - La forme Perlin-Worley reste toujours calculée : sans elle, les cellules de la grille géante (≈ 380 m) apparaissaient en marches d'escalier.
- **Échelle du bruit selon la grille du nuage**, et non plus selon l'altitude : ×6 pour le cumulonimbus et ses satellites, ×1 pour la nappe et les cumulus locaux. Le changement d'échelle avec l'altitude cisaillait le bruit et créait des rayures horizontales.
- Le gain du LOD n'est pas mesurable ici : la synchro verticale plafonne à 60 FPS, et on y est sur tous les plans.

## 2026-11-08 : géant masqué, liseré sombre

- **Cumulonimbus géant masqué** à la demande de l'utilisateur : `CloudField.GiantEnabled = false` par défaut. La grille géante est alors réduite à 2³ voxels et son domaine repoussé à 10 000 km : elle n'est jamais calculée ni échantillonnée (calcul au démarrage : 2,2 s au lieu de 2,9 s). Tout le code du géant reste en place ; il suffit de rallumer l'option.
- **Liseré sombre entre deux nuages superposés** : la perspective aérienne était appliquée d'un bloc selon la distance du *premier* nuage touché. À travers le bord fin d'un nuage proche, un nuage lointain recevait donc le voile du proche, presque nul, et paraissait plus sombre. Elle est désormais appliquée **à chaque échantillon**, selon sa propre distance.

## Réglages de référence (mesurés par la sonde)

| Véhicule | Vitesse max | Nitro | Comportement moteurs coupés |
|---|---|---|---|
| Rafiot | ≈ 57 m/s | ≈ 89 m/s | chute franche, autorité ≈ 0,07 |
| Zeppelin | ≈ 12 m/s | ≈ 25 m/s | flotte (ballon), dérive au vent |
| Guêpe | ≈ 63 m/s (fusée, 3 s) | ≈ 86 m/s | le rotor garde le stationnaire, autorité ≈ 0,5 |

## 2026-11-08 : cumulus en amas de lobes soudés (M13 + correction)

- Les petits cumulus et les congestus ne sont plus des cylindres coiffés d'un dôme. Ce sont des **amas de 5 à 13 lobes** (`Tower.Lobes`, `BuildLobes`), avec un léger penchant propre à chaque nuage (`Tower.Lean`).
- La première version (M13, Haiku) donnait des **boules empilées détachées** (« bonhomme de neige »). Correction :
  - union douce `1 − Π(1 − b)` au lieu de `max` ;
  - **noyau continu** de la base à 0,75 H, qui suit le penchant ;
  - lobes plus gros (rayon de 1,0 R à 0,45 R) à hauteurs stratifiées ;
  - aplatissement plus faible (`dy / 0.9`).
- Calcul au démarrage : 2,2 s. SDF : 0 surestimation. 60 FPS après le démarrage.
