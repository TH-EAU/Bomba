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

## 2026-11-08 : crépuscule et ombre de la Terre

- **Soleil couché de 1,2°, statique.** La source de vérité est `Scripts/Core/Twilight.cs`. L'ombre de la Terre est un **plan incliné**, sans aucune courbure de la géométrie : l'altitude d'ombre vaut environ 1 300 m à l'origine et baisse vers le soleil (pente tan 1,2°).
- **Nuages** : le soleil n'éclaire que la partie au-dessus du plan, avec ±60 m de pénombre (`earth_shadow` dans le tampon). En dessous, la lumière vient du ciel seul : les tours sont roses au sommet et bleu-lavande en bas.
- **Ciel** (`sky_dusk.gdshader`) :
  - lueur chaude côté soleil ;
  - côté opposé, bande bleu-gris de l'ombre de la Terre (jusqu'à environ 5°) et ceinture de Vénus rose au-dessus.
  - Le soleil n'apparaît sur l'horizon que si la caméra dépasse l'altitude d'ombre. Son élévation apparente est √(2h/R) − δ, avec R fictif tel que l'horizon s'abaisse de δ à l'altitude d'ombre ; il est posé sur l'horizon plat.
- **Scène** : `TwilightSun` (lumière directionnelle rasante) n'éclaire que la couche 2, que `Vehicle` ajoute à ses visuels. Son énergie suit `Twilight.SunLit` au niveau du véhicule. La mer et les îlots, dans l'ombre, ne reçoivent que le ciel. La mer a une traînée de reflet de la lueur du couchant (en émissif).
- Plans fixes ajoutés : 7 « anti-solaire » (ombre de la Terre) et 8 « altitude » (soleil visible).

## 2026-11-08 : reflets parasites et ciel bleu/nuit

- **Colonne lumineuse au-dessus du soleil** : c'était le **bloom** de l'environnement (seuil HDR à 1,2), qui faisait saigner le disque solaire en colonne. Vérifié en coupant le glow : la colonne disparaît. Seuil relevé à 4,0 : seules les vraies sources (éclairs) font halo. Le disque du soleil n'est plus surexposé (×1 au lieu de ×3).
- **Reflet côté opposé** : le brouillard teintait le ciel en lavande (`FogSkyAffect` 0,15 → 0). Le ciel côté opposé est maintenant bleu nuit.
- **Ciel** (`sky_dusk.gdshader`) : le dégradé est remplacé par deux teintes, pilotées par l'azimut du soleil :
  - côté soleil : bleu ciel, pâle vers l'horizon (diffusion) ;
  - côté opposé : bleu nuit, plus clair vers l'horizon.
  - La lueur de couchant est réduite au ras de l'horizon ; la ceinture de Vénus est atténuée à 15 %.
- Les projecteurs n'étaient pas en cause : un test sans eux donne la même colonne.

## 2026-11-09 : gouttes sur l'écran guidées par une carte de rails
- `Resources/Textures/waterDropMap.jpg` (fournie par l'utilisateur) est préparée une fois au chargement par `Scripts/Effects/DropRailMap.cs`. Chaque rail (composante connexe) reçoit une phase, une vitesse et un seuil de présence, et le masque flouté donne le profil bombé. Raison : avec des secteurs angulaires, chaque rail était découpé en éclats, et le masque binaire donnait des normales en blocs.
- Les perles gonflent sur les parties épaisses et glissent vers l'extérieur par à-coups. Normales par différences finies, réfraction inversée et floue, bord sombre, reflet.
- Seul le nitro fait glisser les gouttes (`NitroFlow`) et vide les rails (`ClearRate`). Sans nitro, elles restent quasi immobiles (`BaseFlow` = 0,02) et sèchent sur place hors des nuages. Choix de l'utilisateur : les gouttes restent statiques tant qu'il n'utilise pas le nitro.
- Renouvellement en mouvement (`RenewRate`, selon la vitesse) : chaque perle s'estompe, puis réapparaît ailleurs sur son rail.
- Réfraction inversée : décalage vers le cœur, jusqu'à `lens_extent_px` au bord. La goutte montre la scène à l'envers. Essais rejetés par l'utilisateur : des gouttes en paraboloïde et des formes en champ de distance. Leurs formes ne respectaient plus la carte ; la forme des perles reste celle de la version d'origine.

## 2026-11-09 : éclairs d'ambiance dans les congestus
- Constat : l'orage (canal `Storm`) n'existe que dans le cumulonimbus de la grille géante, désactivée (`GiantEnabled = false`). Aucun éclair ne partait donc en jeu, seulement dans CloudShots, qui les déclenche directement.
- Choix : 70 % des éclairs d'ambiance (`CloudEffects.CongestusStrikeShare`) partent du cœur d'un congestus local proche (`CloudField.TryRandomLightningPoint`). C'est purement décoratif : le canal d'orage n'est pas modifié, donc ni assombrissement des congestus ni foudre sur le joueur.

## 2026-11-09 : retour du cumulonimbus, au même grain que les nuages locaux
- `GiantEnabled = true` par défaut : le cumulonimbus est rétabli, à la demande de l'utilisateur. Il redevient la source des éclairs. `CongestusStrikeShare` passe à 0 : l'utilisateur ne veut pas de congestus orageux.
- Problème : le géant avait un tout autre grain que le reste. Ses cellules faisaient 384 m, contre 64 m pour la grille locale, et son bruit de forme était ×6, d'où des bourgeons d'environ 330 m contre 55 m.
- Correction :
  - grille géante passée de 128×64×128 à 256×80×256, soit des cellules de 192 m ;
  - dans `clouds_march.glsl`, une passe grossière ×3 (qui efface les cellules), puis la passe locale ×1 à demi-dose ;
  - détail fin à l'échelle locale partout.
- Coût : calcul au chargement de 2,7 à 4,5 s, texture d'environ 42 Mo ; 59–60 FPS sur tous les plans de CloudShots (GTX 1050 Ti).
- Éclairs d'ambiance : `CloudField.TryRandomLightningPoint` vise directement le cœur du cumulonimbus. La recherche au hasard n'aboutissait qu'environ une fois sur huit ; on obtient maintenant environ un éclair toutes les 4 à 8 s quand l'orage est à moins de 25 km.
- Couleur : la teinte d'orage est neutralisée dans le rendu (`storm = c.b * 0.0` dans `clouds_march.glsl`). Le cumulonimbus garde la couleur des autres nuages, à la demande de l'utilisateur. Le canal d'orage reste utilisé par le gameplay et les éclairs.

## 2026-11-09 : nuages lointains visibles à travers le bord d'un nuage proche
- Cause : un rayon qui longe la frange d'un nuage proche y fait des centaines de mètres à pas fin (environ 6 m), dans une densité nulle (zone érodée). Il épuise ses 96 pas avant d'atteindre les nuages lointains, qui disparaissent.
- Correction (`clouds_march.glsl`) :
  - le pas s'allonge sur une suite d'échantillons vides (densité ≤ 0,005), jusqu'à ×4 ;
  - à la rentrée dans la densité, le rayon recule et reprend à pas fin, ce qui évite les stries en veinage de bois ;
  - l'intégration utilise le pas réel.
- Vérification : nouveau plan « bord » dans `CloudShots` (caméra au ras du flanc d'un congestus, regard tangent). Avant, une bande unie sans nuages près du bord ; après, la nappe lointaine est visible jusqu'à la frange. 59–61 FPS sur tous les plans.

## 2026-11-09 : dégradé plus doux sur le cumulonimbus
- Demande : adoucir le passage du blanc externe au gris interne sur le géant, sans toucher aux autres nuages.
- Dans `clouds_march.glsl`, pondéré par la part de la grille géante dans l'échantillon (`gg`) :
  - transition de l'ombre de la Terre élargie : ±60 m → jusqu'à ±960 m ;
  - occlusion ambiante 5× plus progressive (150 m → 750 m).
- Les nuages locaux sont inchangés. Les satellites du géant (même grille) sont adoucis aussi.

## 2026-11-09 : ciel physique (Rayleigh, Mie, ozone)
- `Shaders/Sky/sky_atmosphere.gdshader` remplace `sky_dusk.gdshader`, qui reste disponible pour revenir en arrière.
- Origine : « Sky++ (sorta) » d'Ansol (godotshaders.com, CC0), basé sur « Rayleigh, Mie, Ozone based atmospheric scatter » (port de MinimalAtmosphere, de Fewes). Choix de l'utilisateur.
- Structure reprise : la diffusion est calculée dans le cubemap de radiance, recalculé seulement quand le ciel change. La transmittance est calculée en quart de résolution et l'écran ne fait que lire. 60 FPS sur tous les plans de CloudShots.
- Adaptations :
  - soleil fourni par `Twilight` et non par `LIGHT0`, car `TwilightSun` est masqué dans l'ombre de la Terre ;
  - soleil de diffusion fixe à +1° ;
  - disque solaire à l'élévation apparente envoyée par `TwilightSun`, par paliers de 0,002 rad et seulement entre −0,02 et 0,05 rad. Chaque changement relance le cubemap ;
  - pas de cumulus, de lunes ni d'arc-en-ciel ; étoiles procédurales.
- Teintes violet et rose (demande de l'utilisateur) : `rayleigh_tint` (1,15 ; 0,92 ; 1,1), `mie_tint` rosé, ozone ×3. L'ozone absorbe surtout le vert, donc le zénith et les trajets rasants virent au violet.
- Puis, à la demande de l'utilisateur (« adapte les couleurs à notre environnement, ciel plus clair ») : retour à la palette du jeu. Zénith bleu-lavande, rose près du soleil, horizon pêche pâle, accordés aux nuages lavande. Réglages :
  - soleil de diffusion : 6° ;
  - `exposure` : 16 ;
  - ozone : 2,2 ;
  - `rayleigh_tint` : (0,97 ; 0,97 ; 1,1) ;
  - `mie_tint` : (1 ; 0,72 ; 0,88).
- Les cirrus seront retravaillés plus tard (voir `docs/todo.md`).
- Essai rejeté : soleil de diffusion à −1,5° avec l'ombre de la Terre. En diffusion simple, l'ombre sort presque noire et la ceinture brun-rouge. Le code de l'ombre est conservé : il n'agit que si le soleil passe sous l'horizon.
- Les nuages ne sont pas modifiés (vérifié au pixel près). Ils paraissent plus blancs par contraste avec un ciel plus sombre.

## 2026-11-09 : réglages du cumulonimbus dans l'inspecteur
- Groupe « Cumulonimbus (orages) » du nœud `Clouds` (CloudField), en tête :
  - `GiantEnabled` : interrupteur ;
  - `GiantPosition` : centre en X, Z, en mètres monde ; par défaut (−8 700, −11 600), soit 14,5 km de l'origine, comme avant.
- Ils remplacent `CumulonimbusDirection` et `GiantSpawnClear`. Avec ces deux-là, la position dépendait du rayon tiré au hasard.
- Les deux réglages sont lus au calcul des nuages, au lancement. Géant désactivé : calcul de 4,6 → 2,2 s, et plus d'éclairs d'ambiance (`CongestusStrikeShare` = 0).

## 2026-11-09 : ciel, côté opposé au soleil
- Constat (utilisateur) : à l'opposé du soleil, le ciel était orangé, surtout à l'horizon. Il devait être mauve et s'estomper vers un bleu-gris, pour signifier que la nuit arrive.
- Cause : la diffusion simple ne produit pas le bleu de l'ombre de la Terre (essai avec soleil couché rejeté plus haut). Les trajets rasants restent orangés tout autour.
- Correction (`sky_atmosphere.gdshader`, passe du cubemap, donc sans coût par image) : côté opposé au soleil, la teinte est remplacée et la luminosité de la diffusion conservée.
  - mauve `night_horizon_color` à l'horizon, bleu-gris `night_zenith_color` au-delà de `night_fade` (0,35 rad) ;
  - luminosité ×`night_darken` (0,8) ;
  - transition sur les côtés : `smoothstep(0.3, -0.7, dot(direction, azimut du soleil))`.
- Les plans de CloudShots se placent par rapport au géant : ils ont légèrement bougé avec `GiantPosition` (valeur exacte au lieu d'un calcul dépendant du rayon tiré au hasard). Le rendu reste déterministe d'une exécution à l'autre.

## 2026-11-09 : matériau du ciel réglable dans l'inspecteur
- `Resources/Sky/sky_material.tres` (ShaderMaterial de `sky_atmosphere.gdshader`) porte les réglages du ciel, ainsi que les trois textures de bruit des cirrus (NoiseTexture2D + FastNoiseLite, auparavant générées en code).
- Branché sur l'export `SkyMaterial` du nœud `Arena` (ArenaBuilder) dans `Main.tscn`. Si l'export est vide, le fichier par défaut est chargé.
- Le code n'écrit dans le matériau que l'azimut et le disque solaire, et seulement en jeu. Dans l'éditeur, ces valeurs seraient enregistrées dans la ressource : vérifié, l'ouverture de `Main.tscn` dans l'éditeur ne la modifie pas.
- Rendu identique au pixel près à la version précédente.

## 2026-11-09 : ombre de la Terre physique dans le ciel (remplace la teinte « côté nuit »)
- Rejeté par l'utilisateur : la teinte du côté nuit était une recoloration plaquée, indexée sur la direction horizontale. Elle créait une coupure en éventail au zénith, où cette direction n'est pas définie.
- Remplacée par de la physique, dans la diffusion (`sky_atmosphere.gdshader`, passe du cubemap) :
  - soleil sous l'horizon (`scatter_sun_elevation_deg` = −1,5°) : l'air dont le rayon vers le soleil traverse la planète n'est plus éclairé ;
  - diffusion multiple approchée : ambiance `night_sky_color` × `night_sky_strength` × coefficient de Rayleigh, le long du regard, avec la transmittance. Dans l'ombre, il reste le bleu de la nuit ;
  - Mie à deux lobes (`mie_back_weight` 0,3, `mie_back_anisotropy` −0,4) : la rétrodiffusion de la lumière rougie dessine la ceinture de Vénus ;
  - 16 échantillons sur le trajet de la lumière, pour les rayons rasants ;
  - `exposure` 22, `mie_strength` 1,5.
- Résultat dos au soleil, d'après la photo de référence de l'utilisateur : bande bleu-gris basse, mauve au-dessus, bleu-violet en haut. Côté soleil : horizon rose-orangé.
- Essai à −3° rejeté : l'air éclairé est au-dessus de ~20 km, trop raréfié, et la ceinture disparaît.

## 2026-11-09 : la ressource du ciel est la source des réglages
- Incident : après la modification des valeurs par défaut du shader, l'éditeur gardait les anciennes en cache. En enregistrant `sky_material.tres`, l'utilisateur les y a figées (soleil à 6°, exposition 16…) : le viewport et le jeu ne montraient plus le même ciel.
- Règle : les réglages du ciel se font dans `Resources/Sky/sky_material.tres`, où toutes les valeurs sont écrites en clair. Ne plus retoucher les valeurs par défaut de `sky_atmosphere.gdshader` pour régler le ciel. Si on le fait quand même, recharger le projet dans l'éditeur avant d'enregistrer la ressource.

## 2026-11-09 : éclairs visibles et propagés, tonnerre préparé
- Constat : les traits existants (cylindres de 3 m de rayon) faisaient moins d'un pixel à quelques kilomètres. On ne voyait que l'éclairage des nuages.
- `Shaders/Effects/lightning_channel.gdshader` : rubans tournés vers la caméra, largeur minimale de 2 px, cœur et halo en additif HDR, dessinés après les nuages (composés avant les transparents).
- `LightningBolt` (réécrit) :
  - un traceur avance (temps d'arrivée par point, 30 km/s visuels), puis l'arc en retour : éclat, creux, éclat, extinction ;
  - deux formes : ramifiée vers une cible (`Setup`, l'existant) et dans le nuage (`SetupSpider`) ;
  - l'éclair dans le nuage est un arbre quasi horizontal, qui suit la densité 0,12 (la frange) depuis le cœur ;
  - visibilité par point à travers les nuages (densité CPU, extinction visuelle 0,0015/m car non érodée). Un canal caché garde un halo élargi : lueur diffuse.
- `LightningSystem` : éclair dans le nuage pour 85 % des éclairs d'ambiance (`SpiderChance`), en plus de l'existant. L'éclairage des nuages est inchangé.
- Tonnerre (groupe « Tonnerre » de `LightningSystem`, en attente des sons de l'utilisateur) :
  - retard = distance / 343 m/s ; `AudioStreamPlayer3D` placé à l'éclair ;
  - coupure du passe-bas de 16 kHz à 350 Hz (interpolation logarithmique, jusqu'à 12 km) ;
  - hauteur jusqu'à −15 % au loin ;
  - claquement ajouté sous 2,5 km.

## 2026-11-09 : éclairs en splines, atténués par les nuages, tonnerre branché
- M14 (Haiku) : splines Catmull-Rom effilées (tronc ×3, branches ×1 ; éclair rampant 1,5 / 0,8 / 0,5), `MaxSegments` porté à 2100 (le lissage triple les segments).
- Rendu opaque essayé puis abandonné : les nuages sont rendus en demi-résolution, donc un trait de 1 à 2 px disparaît de la profondeur sous-échantillonnée, et les nuages situés derrière lui sont composés par-dessus.
- Retenu : rubans additifs HDR au profil doux (halo), visibilité par point calculée sur le CPU (nuage entre la caméra et le point, extinction 0,006/m, 32 échantillons), sans lueur minimale : un éclair derrière un nuage épais s'éteint.
- Éclair rampant : sort du cœur (densité la plus faible), puis longe la paroi ou le dessous de l'enclume à l'air libre (densité < 0,005 avec du nuage à 250 m au-dessus ou devant). Dans le nuage, un canal est masqué en quelques dizaines de mètres de rendu.
- Tonnerre : `Thunder1.wav` (grondement) et le claquement MP3 sont chargés par défaut dans `LightningSystem._Ready` si l'inspecteur est vide.
- Tonnerre audible de loin (demande de l'utilisateur) :
  - volume via `AudioStreamPlayer3D`, modèle logarithmique, `ThunderUnitSize` 5 km (−6 dB à 10 km) ;
  - passe-bas via 3 bus « Thunder0..2 » créés en code, chacun avec un `AudioEffectLowPassFilter` en 24 dB, coupure réglée à chaque coup : de 16 kHz à 250 Hz, atteint dès 4 km (`MuffledDistance`) ;
  - le filtre intégré du lecteur 3D est désactivé : il suit l'atténuation du volume, donc il filtrerait moins un son qui porte loin.
- Glow des éclairs (demande de l'utilisateur : « un vrai glow », pas un halo 2D) :
  - le halo dessiné dans le ruban est supprimé : le canal est un cœur fin, à bord antialiasé, très lumineux (HDR 20) ;
  - le rayonnement vient du glow de l'environnement : mélange additif, niveaux 3 à 6 (1 / 0,8 / 1 / 0,3), plafond de luminance 40 ;
  - seuil du glow relevé de 4 à 8, pour que le disque solaire (jusqu'à ~6) ne rayonne pas ;
  - réglages dans le groupe « Rendu » de `LightningSystem` (`BoltEnergy`, `BoltWidth`, `BoltMinPixels`, `BoltColor`), appliqués à chaque éclair.

## 2026-11-09 : CloudSurfer, caméra cockpit, pilotage à la souris
- `Scenes/Vehicles/CloudSurfer.tscn` remplace le Rafiot dans `Main.tscn` (le Rafiot reste dans le projet). Modèle `Assets/Vehicules/CloudSurfer.glb`, retourné de 180° : la proue est vers +Z dans le modèle, et Godot avance vers -Z.
- Le `.glb` a été exporté sans le modificateur Miroir : la coque et la cabine n'en couvrent que la moitié (x ≥ 0). Correction provisoire : nœud `ModelMirror`, seconde instance du modèle en symétrie X, où seuls la coque et la cabine restent visibles. À supprimer après un réexport avec « Appliquer les modificateurs ».
- Physique reprise du Rafiot (masse, traînée, assistance), propulseurs à charbon aux moteurs (±1,5 ; −1,9 ; 2,5), centre de masse (0 ; −1,8 ; −1).
- Sonde de vol, Rafiot → CloudSurfer : mêmes vitesses (palier ~56 m/s, nitro ~88 m/s). Le CloudSurfer cabre de +6° à plein gaz (poussée légèrement sous le centre de masse), donc il tient mieux l'altitude en virage (+2,7 m contre −11 m).
- `.gitignore` : `*.blend`, `*.blend1`, `*.blend.import`. Le jeu n'utilise que le `.glb`.
- M15 (Haiku) :
  - `ChaseCamera` en mode Cockpit par défaut, sur le `Marker3D` « CockpitCamera » du véhicule (F5 bascule vers la poursuite) ; sans marqueur, retour à la poursuite ;
  - `PlayerVehicleController` : souris capturée ; le réticule (`Scripts/UI/SteeringReticle.cs`) est un gouvernail sur un disque de 220 px (lacet et tangage, ajoutés au clavier) ; Échap libère la souris, le clic gauche la reprend, le clic molette recentre ;
  - poussée inchangée.
- Tonnerre remplacé (2026-11-09, fichiers de l'utilisateur) : `Thunder1.wav` enregistré à 0, 2, 6, 10 et 20 km (`Thunder1-2km.wav`, etc.). Chaque fichier contient déjà le retard de propagation, l'atténuation des aigus et la réverbération.
  - On joue les deux enregistrements qui encadrent la distance, en fondu à puissance constante (racine des poids) ; au-delà de 20 km, −6 dB par doublement de distance.
  - Le son est positionné à l'éclair, sans atténuation 3D ni filtre.
  - Supprimés : le retard calculé, les bus passe-bas, la variation de hauteur et le claquement MP3 séparé (fichier gardé, inutilisé).
  - Réglages : `ThunderStreams` et `ThunderDistances` (groupe « Tonnerre »).

## 2026-11-09 : pluie, grêle, dépression et ambiance sonore
- `StormWeather` (nœud de `Main`, source de `Wind`) :
  - pluie sous le cumulonimbus (pleine jusqu'à 0,6 R, nulle au-delà de R, jusqu'au sommet) ;
  - grêle dans le cœur orageux (Storm > 0,3) au-dessus de 3 000 m ;
  - dépression : vent convergent et tournant, jusqu'à 35 m/s, avec rafales de Perlin. Il se lève à 1,3 R, nul au départ (14,5 km du centre), et agit sur toute la physique.
- M16 (Haiku) `Precipitation` : boîte de particules autour de la caméra, repliée modulo (aucune traînée d'émission), traits étirés selon la vitesse relative à la caméra, emportés par le vent et la turbulence. La pluie mouille aussi l'écran (`ScreenDroplets.RainWetRate`).
- M17 (Haiku) `WeatherAmbience` :
  - pluie légère/forte et vent léger/moyen/fort en fondus à puissance constante ;
  - grêle ;
  - grincements aléatoires selon le stress (vent d'orage, rafales, rotation).
- Rideau de pluie : `clouds_march.glsl` (`march_rain`, cylindre analytique de la mer à la base, 20 pas, stries verticales, extinction 3e-4/m) et `vec4 rain` dans `Params` (448 octets). Réglages `CloudField.RainCurtain` et `RainCurtainRadius`. À notre échelle (base des nuages à ~320 m), le rideau est bas et ne se voit bien que sous la nappe, près de l'orage.
- CloudShots : plans « pluie » (sous la nappe, face au rideau) et « sous l'orage » (dans la pluie).
- Mesures : sonde de vol proche de l'existant au départ (vent d'orage ramené à zéro au départ) ; 54 à 60 FPS (premier plan 54, contre 60 avant, cause non isolée).
- Non vérifiés : la grêle à l'écran, et tous les sons à l'écoute.
- Pluie retouchée (M18, Haiku) :
  - particules émises en permanence, apparition progressive par seuil aléatoire par goutte (uniform `intensity` lissé) ;
  - couche masquée hors de la pluie, ce qui corrige la pluie restée visible au point de départ ;
  - rendu éclairé : bleu clair translucide, normale de cylindre calculée dans le shader, reflet des éclairs et du soleil. Particules sur les couches 1 et 2, pour que le soleil les éclaire comme les véhicules ;
  - opacité 0,75 ;
  - `shadows_disabled` : les gouttes ne reçoivent pas l'ombre portée du véhicule (l'ombre de la Terre agit par l'extinction du soleil).

## 2026-11-09 : vitesse maximale réglable
- `Vehicle` : groupe « Vitesse maximale » avec `MaxSpeed`, `NitroMaxSpeed` (0 = pas de limite, comportement d'avant) et `MaxSpeedStiffness` (10/s). Limiteur doux : au-delà de la limite, freinage proportionnel à l'excès.
- Essai sonde (CloudSurfer, `MaxSpeed` 45) : palier à environ 46 m/s au lieu de 56, nitro inchangé. Aucun véhicule n'a de limite par défaut.

## 2026-11-09 : vent plus présent et indicateur dans le HUD
- `Wind` : vent de base 10 m/s (au lieu de 5), rafales en bruit de Perlin (±35 %, 0,15 Hz) au lieu d'une sinusoïde, direction qui tourne de ±30° (0,01 Hz). Exports `DirectionWanderDeg` et `DirectionWanderFrequency`.
- Le vent agissait déjà sur toute la physique (vitesse relative à l'air : traînée, adhérence latérale, surfaces). Vérifié avec la sonde (CloudSurfer, plein gaz 3 s) : 10 m/s de face donnent 48 m/s au lieu de 57 ; de travers, la vitesse et l'altitude bougent peu.
- HUD (`DebugHud`) : ligne « Vent : force m/s (km/h) », direction relative (de face, dans le dos, de travers, pousse à gauche ou à droite), ascendance ou descendance au-delà de 1 m/s, et flèche relative au nez (`Scripts/UI/WindIndicator.cs`). Le vent d'orage est compris.
- Constat sonde : le CloudSurfer descend désormais à plein gaz (−16 m à 3 s), à cause des réglages de l'utilisateur dans `CloudSurfer.tscn` (`VerticalGripPowered` 0,2, `BrakeDrag` 0,2, portance de `HullLift` revenue aux valeurs par défaut). Indépendant du vent.
