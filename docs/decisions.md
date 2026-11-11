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

## 2026-11-09 : ombre des nuages sur l'engin, œil du cumulonimbus, nuages vivants
- Ombre : `CloudField.SunTransmittance` (24 pas vers le soleil rasant, jusqu'à 6 km, extinction 0,004/m, exports `ShadowMaxDistance` et `ShadowExtinction`). `TwilightSun` multiplie son énergie par cette valeur, lissée sur 0,3 s. Elle agit sur l'engin et sur la pluie (couche 2). Mesures : 1,0 en plein air, 0 dans la nappe, 0,49 sous le géant. La mer et les îlots restent dans l'ombre de la Terre.
- Œil : `EyeRadiusRatio` 0,1 (≈ 1 km) et `EyeSoftness` 0,25. Le masque `EyeMask` vide la colonne centrale du cumulonimbus (densité et canal orage) sur toute sa hauteur. `StormWeather` y annule pluie, grêle, vent et turbulence, le rideau de pluie aussi (`vec4 storm_eye`, `Params` à 464 octets), et les éclairs sont tirés hors de l'œil. En bas de l'œil, la nappe soulevée au pied de l'orage reste présente.
- Nuages vivants : le bruit de forme dérive et monte (convection, `NoiseRise` 1,5), le détail fin dérive autrement : le relief évolue au lieu de glisser. Vitesse `NoiseDriftSpeed` 0,012 (au lieu de 0,004). Coût nul : 60 FPS sur tous les plans de CloudShots.
- CloudShots : plan « œil » (vue plongeante sur le géant).
- Note : depuis le vent en rafales, la sonde de vol n'est plus parfaitement reproductible (la phase des rafales dépend de la durée du calcul des nuages). À geler pour les comparaisons fines.
- Œil percé de part en part (demande de l'utilisateur : « un trou de nuage ») :
  - rayon 0,15 R (≈ 1,6 km) ;
  - l'œil perce aussi la nappe soulevée au pied de l'orage (grille géante) ;
  - et la nappe locale, qui se répète : masquée à l'échantillonnage, à la position réelle de l'œil (shader `sample_cloud` et `CloudField.Sample`). Le SDF n'est pas modifié : retirer du nuage ne fait qu'allonger la distance, le saut reste sûr.
  - Vérification : densité nulle au centre et à 1 km, de 350 m à 10 km ; vue plongeante (plan « œil » à la verticale) : puits jusqu'à la mer.

## 2026-11-09 : motif répétitif sur les façades et dans l'œil du géant
- Diagnostic (couverture brute, `--cloud-debug=5`, et essais par élimination) :
  - **cause principale** : le bruit de déformation du géant (`warp`, Perlin 2 octaves sur un même réseau ×2, environ 3 km × 2,4 km) dessinait un quadrillage de bosses sur les façades, et des anneaux réguliers dans l'œil, dont le rayon pulsait avec la hauteur ;
  - **causes secondaires** : la répétition des textures de bruit (900 m et 2,7 km) sur les façades, et la grille verticale grossière.
  - **écarté** : le pas de rendu (anneaux identiques avec des pas 5 fois plus fins).
- Corrections :
  - `CloudField` : pour le géant seulement (domaine sans répétition), trois octaves à rapports irrationnels (×1, ×2,13, ×4,71) et décalées ; les nuages locaux, qui doivent se répéter sans raccord, sont inchangés ;
  - `clouds_march.glsl` : déformation à très grande échelle (`giant_warp`, environ 15 km) et rotation hors des axes (`GIANT_ROT`) pour le bruit de forme et de détail du géant ;
- La silhouette du géant change un peu : plus déchiquetée et naturelle.
- Suite (demande de l'utilisateur : « trop de détail sur trop de surface ») :
  - **masque de détail** du géant (`giant_detail_mask`, un bruit de valeur quintique tourné, échelle 4 km, calculé une fois par échantillon et réutilisé par le détail via `g_mask`) : la passe fine et l'érosion de détail varient de 12 à 100 %. Des zones lisses alternent avec des zones boursouflées. Plan CloudShots « paroi » (façade côté soleil, à 2 500 m) ;
  - `giant_warp` : bruit de valeur vectoriel (`value_noise3v`, 8 hachages) ; une texture 64³ étirée sur 15 km donnait des facettes (ombrage anguleux) ;
  - la marche vers le soleil n'utilise ni déformation ni masque (`g_light`) : la première version, avec ces bruits recalculés partout, faisait tomber les FPS à 15–43 ;
  - érosion CPU du géant tournée et inclinée hors des axes.
- Grille verticale du géant laissée à 80 étages (exposant 2,2). L'essai à 128 étages faisait apparaître des marches carrées alignées sur les axes sur l'enclume. Ont été écartés comme cause : la marge du SDF, l'azimut du soleil, la déformation et le masque du shader, l'érosion et le bruit de déformation CPU. La cause exacte n'est pas trouvée.
- Coût final : calcul au chargement inchangé (4,4 s) ; 52 à 60 FPS sur tous les plans de CloudShots.

## 2026-11-09 : pilotage « incliner et tirer » (gouvernail)
- Demande de l'utilisateur : peu de commandes, simples ; un débutant se déplace librement, un expert domine ; tourner prend du temps ; le roulis crée le virage, tirer le serre, trop de roulis met sur le dos (looping possible). Décisions de l'utilisateur :
  - réticule bas = piquer, et frein au-delà de 80 % de la course ;
  - retour à plat doux au neutre.
- `Vehicle.ApplySteering` réécrit, groupe « Pilotage » :
  - **roulis = inclinaison demandée et tenue** (`MaxBankDeg` 180 : roulis plein = sur le dos), rejointe à `RollRateDeg` 140 °/s avec `BankResponseTime` 0,3 s. Avec un réticule qui reste où on le laisse, une commande en vitesse de roulis aurait donné un tonneau continu ;
  - **tangage** en vitesse (`PitchRateDeg` 45 °/s), maintien d'assiette au neutre (`PitchHold`) ;
  - **virage dû à l'inclinaison** : lacet de coordination autour de la verticale du monde, `BankTurnRateDeg` 22 × sin(inclinaison) × cos(tangage) ;
  - **plus de lacet direct** : la gouverne de direction fait déraper, ce qui est contre-intuitif ;
  - **rampe** : accélération angulaire plafonnée (`RateRampTime` 0,35 s), freinage d'une rotation 4 fois plus fort (élan au départ, arrêt net, et les couples des modules ne font pas dépasser la consigne) ;
  - **retour à plat** (`AutoLevelTime` 1,2 s) seulement roulis et tangage au neutre, par le côté le plus court (aussi depuis le dos). En tirant roulis au neutre, le roulis n'est pas corrigé : c'est ce qui permet le looping. Nez à plus de 60° au neutre : il est d'abord ramené vers 55°.
  - `GetDesiredTorqueAxis` renvoie la commande réellement calculée : les modules lisaient le roulis comme une vitesse et faisaient dériver l'inclinaison.
- Supprimés : `MaxTurnRates`, `SteerResponse`, `RollAssist`, `AutoBankDegrees`, `PitchAutoLevel` (scènes nettoyées). Zeppelin : inclinaison 30°, roulis 30 °/s, tangage 15 °/s, rampe 0,8 s.
- CloudSurfer : réglage `VerticalGripPowered = 0.2` de la scène retiré (retour à 1,5). Avec cette valeur, la vitesse ne suivait pas le nez en tirant, et l'engin perdait toute sa vitesse en virage serré (68 → 4 m/s).
- Sonde (CloudSurfer, plein gaz, nouveaux scénarios dans `FlightProbe`) :
  - palier ~58 m/s, altitude tenue ;
  - incliné 45° : 18 °/s, −3 m/s d'altitude ;
  - 75° + tirer : ~80 °/s, vitesse tenue (52 à 70 m/s), inclinaison réelle ~88° (dépassement de 13° dû au couplage tangage-roulis, toléré) ;
  - looping complet en ~7 s ;
  - lâché depuis le dos : à plat en ~2 s.
- Sensibilité (demande de l'utilisateur : « rendre tous les contrôles moins sensibles », les réglages de pilotage étant trop nombreux) : un seul réglage, `PlayerVehicleController.Sensitivity` (0,1 à 1, défaut 0,5). Il ralentit le réticule (déplacement souris × sensibilité) et applique une courbe « expo » de force 1 − sensibilité au roulis et au tangage. Les petits mouvements sont doux, les grands restent pleins. À 0,5, mi-course donne 31 % au lieu de 50 %.
- Rouler ne fait plus que rouler (demande de l'utilisateur : « c'est moi, avec le joystick, qui pousse vers le haut et ça génère le virage ») :
  - `BankTurnRateDeg` passe à 0 par défaut (et retiré du Zeppelin) ;
  - le maintien d'assiette tourne autour d'un axe horizontal, et non de l'aile qui penche une fois incliné (il faisait virer).
  - Sonde : incliné 45° → cap stable (≈ 1 °/s de dérive) ; le virage vient de tirer.
- Virage inversé en relâchant (signalé par l'utilisateur) : en ramenant le réticule vers le centre, un léger piqué involontaire, avec l'engin incliné, faisait virer du côté opposé (symétrique du « tirer »).
  - Piquer tourne maintenant autour d'un axe horizontal : il baisse le nez sans faire virer. Tirer garde l'axe de l'aile.
  - Zone neutre verticale du réticule : `MousePitchDeadband` 0,08.
  - Sonde « 45° + piquer » : le nez descend, le cap reste fixe.
- Roulis repassé en vitesse, comme un joystick (signalé par l'utilisateur : « tu appliques une rotation automatique »). En commande d'angle, ramener le réticule vers le centre faisait revenir l'engin vers l'horizontale.
  - Pousser fait rouler (`RollRateDeg` 120 °/s), lâcher garde l'inclinaison.
  - Retour à plat seulement après `AutoLevelDelay` (1,5 s) sans aucune commande, en `AutoLevelTime` 1,5 s.
  - `MaxBankDeg` devient une limite (Zeppelin 30°). `BankResponseTime` supprimé.
  - Clavier : `KeyboardBank` 0,7 de la vitesse de roulis.
  - Sonde : roulis 0,5 s → 44°, inclinaison tenue 1,5 s, puis à plat.
- Rotation parasite au roulis à fond (signalée par l'utilisateur) :
  - **cause** : `GetDesiredTorqueAxis` transmettait aussi le roulis aux gouvernes (profondeur, dérive) et aux moteurs orientables. Des gouvernes dans l'axe ne peuvent pas rouler : elles produisaient du tangage et du lacet, énormes à grande vitesse. Sonde : nez à +82° et cap affolé au roulis à fond, propre au roulis doux ;
  - **correction** : les modules ne reçoivent plus que le tangage, le roulis étant assuré par le couple direct. Le maintien d'assiette agit à toute inclinaison (axe horizontal) ;
  - `RollRateDeg` 120 → 75 °/s, remise à plat plafonnée à 40 °/s ;
  - **sonde** : roulis à fond de 40° à 125°, nez stable à 0°, cap fixe.
- À surveiller : avec les réglages actuels du CloudSurfer (≈ 140 m/s), tirer à fond fait un virage très serré qui écroule la vitesse (144 → 23 m/s en 1,5 s).
- Bas du réticule (demande de l'utilisateur : « un simple piqué vers le bas, pas vers le bas de la carte ») : le piqué tourne de nouveau autour de l'aile (repère de l'engin), symétrique de tirer. L'aérofrein du bas du réticule est désactivé (`MouseBrakeStart` = 1, arc rouge masqué) ; le frein reste sur S. La zone neutre verticale (`MousePitchDeadband` 0,08) est conservée contre le piqué involontaire.
- Retour à plat automatique supprimé (demande de l'utilisateur : « l'assistance pour les débutants est très mauvaise ») : `AutoLevelTime` = 0 par défaut. Le code reste, réactivable par véhicule. Le maintien d'assiette (`PitchHold`) est conservé.

## 2026-11-09 : CloudSurfer à 1 tonne
- Demande de l'utilisateur : l'engin doit peser environ une tonne. À 10 kg, il était instable, et la poussée ne tenait pas le poids réel.
- Ce qui dépend de la masse : les forces en newtons (poussée des moteurs, portance, traînée et contrôle des gouvernes). La traînée de `Vehicle`, le pilotage, le nitro et `HoverAssist` sont des accélérations, indépendantes de la masse.
- Réglage = l'équilibre validé à 20 kg × 50 :
  - `mass` 1 000 ;
  - profil de moteur dédié `Resources/Thrusters/CloudSurfer.tres` (22 500 N par moteur ; Charbon, partagé avec le Rafiot, est inchangé) ;
  - `HullLift` : `LiftCoefficient` 7,5, `MaxLift` 10 500, `DragCoefficient` 1 ;
  - gouvernes : `ControlStrength` 1,5, `DragCoefficient` 0,25 ;
  - `ForwardDrag` 0,012 (vitesse de palier validée).
- Sonde : palier ~58 m/s, altitude tenue ; incliné + tirer ~50 °/s, vitesse tenue ; frein 64 → 40 m/s ; nitro ~95 m/s ; gaz coupés : chute immédiate.
- Note : sans retour à plat automatique, les scénarios de la sonde s'enchaînent avec l'inclinaison héritée (le « looping » part incliné). À reprendre si on a besoin de mesures isolées.

## 2026-11-10 : houle et amortissement des commandes
- Houle (demande de l'utilisateur : « comme si on était posé sur l'eau ») : `Vehicle`, groupe « Houle » :
  - `SwayRollDeg` 3, `SwayPitchDeg` 1,5, `SwayFrequency` 0,12 Hz, bruit simplex normalisé (×2,3, le bruit brut plafonnant à ±0,42) ;
  - le roulis est ajouté à la commande de vitesse de roulis (dérivée de l'angle de houle), le tangage à la cible du maintien d'assiette. Pas de couple brut : l'engin oscille sans dériver ;
  - le bateau oscille vraiment, donc en vue extérieure comme en cockpit.
- Amortissement des commandes : `PlayerVehicleController.InputSmoothing` 0,15 s (lissage exponentiel du roulis et du tangage, après la courbe expo).

## 2026-11-10 : flou de mouvement
- `Scripts/Effects/MotionBlur.cs` (nœud `MotionBlur` dans `Main.tscn`) accroche un `CompositorEffect` (PostTransparent, compute) à la caméra courante : copie de l'image, puis flou le long du déplacement de chaque pixel, reprojeté par la matrice vue-projection de l'image précédente (mouvement de caméra seulement).
- Pas de quad plein écran `hint_screen_texture` : la copie d'écran est prise avant la composition des nuages, qui disparaissaient.
- L'engin reste net : les pixels proches (< `NearClear` 12 m) ne sont pas floutés, et les échantillons qui tombent sur un objet proche sont ignorés (sinon la coque bavait sur le décor).
- Réglages : `Strength` 0,6, `MaxLength` 0,035 (fraction de l'écran), `Samples` 10. `Strength` 0 désactive l'effet.
- Nitro : le flou monte à `NitroStrength` 1,6 / `NitroMaxLength` 0,07 en 0,3 s (`NitroBlendTime`), avec deux fois plus d'échantillons pour ne pas strier.
- Secousse de nitro en cockpit (« trop violente ») : `ChaseCamera.CockpitShakeScale` 0,15 (≈ 5 cm au lieu de 35), et la secousse suit un bruit lisse à 18 Hz (`ShakeFrequency`) au lieu d'un tirage aléatoire par image. La vue poursuite garde son amplitude.

## 2026-11-10 : foudre sur l'engin (traceur puis arc en retour) et résistance des moteurs
- Demande de l'utilisateur : d'abord un filet fin qui cherche son chemin, puis BOOM. Avant, l'éclair partait d'un point au hasard à 40–120 m (même par en dessous), avec un traceur invisible (30 000 m/s) et le choc immédiat.
- Frappe (`CloudEffects.Strike`) :
  - le traceur part 500–900 m devant, 120–250 m au-dessus, ±250 m de côté. Plus haut, le toit du cockpit le cache ;
  - il vise la **proue** : la foudre s'accroche aux extrémités et le courant traverse la coque jusqu'aux moteurs. La proue est visible du cockpit, et le canal ne finit pas dans la caméra ;
  - choc (impulsion, rotation, coupures, secousse), flash, lueur du nuage et tonnerre tombent à l'arc en retour, `StrikeLeaderTime` 0,6 s après.
- **Coup inévitable** (choix de l'utilisateur) : le canal se tord pour suivre l'engin. Poids t² le long du tronc, décalage `target_offset` dans le shader, sans régénérer la géométrie.
- Canal (`LightningBolt.SetupStrike`, `lightning_channel.gdshader`) :
  - traceur **par bonds** (sauts de 35 ms), à 30 % de la largeur ;
  - 6 à 10 filets morts autour du tronc ;
  - à l'arc en retour, les branches retombent à 15 % de l'éclat, et le tronc s'allume en entier ;
  - tronc découpé d'un niveau de plus, pour rester anguleux de près ;
  - `StrikeBoltWidth` 0,35 m.
- Les éclairs nuage-mer d'ambiance (`Setup`) ont le même traceur par bonds (0,25 à 0,5 s selon la longueur) et les mêmes filets : les deux types se ressemblent.
- Flou de mouvement : moyenne de Karis (poids 1 / (1 + luminance)) et `max(flou, centre × 0,5)`. Sans cela, l'éclair HDR s'étalait en large bande blanche pendant la secousse.
- **Résistance des moteurs** (choix de l'utilisateur : par moteur, dans le profil) :
  - `ThrusterProfile.LightningResistance` et `Propeller.Resistance`, de 0 à 1. Les modules sans moteur valent 1 ;
  - à l'impact, chaque moteur se coupe avec la probabilité 1 − r, pendant `StallDuration × (1 − r) × [0,6 ; 1,4]`. L'aléa rend la coupure asymétrique, donc l'engin part en vrille ;
  - impulsion et rotation × (1 − 0,6 × résistance moyenne) ;
  - valeurs : profil CloudSurfer 0,3, autres profils 0 (comportement d'avant).
- Test : `-- --force-strike=S` provoque une frappe à S secondes. Le journal affiche « [Foudre] » (traceur, impact, moteurs coupés).
- Couleur (demande de l'utilisateur : « violet ») : `BoltColor` (0,75 ; 0,45 ; 1), flash (0,7 ; 0,5 ; 1), lueur dans les nuages (`clouds_march.glsl`, 2 occurrences) (0,7 ; 0,5 ; 1). Le cœur HDR sature vers le blanc ; le halo et le flash portent le violet.
- Jonction canal / coque (signalée par l'utilisateur : « forme bizarre au toucher, on voit l'éclair se couper ») :
  - le point d'accroche est la **pointe réelle de la proue**, c'est-à-dire le sommet le plus en avant du modèle (calculé une fois par engin, sans les visuels des moteurs). Avant, il venait de la boîte de collision et l'éclair finissait dans le vide ;
  - largeur du canal plafonnée à 4 px de demi-largeur (`max_core_px`). De près, les rubans larges se découpaient en dents de scie ;
  - petit éclat d'impact (`Shaders/Effects/lightning_impact.gdshader`, disque de 1,2 m, sans test de profondeur) qui suit la proue et masque la jonction.
- Note : les captures `--write-movie` remplissent vite le disque C:. Un disque plein fait planter Godot (signal 11) au premier rendu. Supprimer les captures après usage.
- Pointes libres affinées (demande de l'utilisateur : « extrémités plates », sans toucher au reste) : sur les 9 derniers points de chaque pointe sans suite, la largeur (minimum en pixels compris) descend jusqu'à 0. Le facteur est empaqueté dans `CUSTOM1.z` (centièmes × 10 + largeur). Restent pleines : le point d'impact sur l'engin et la pointe d'un éclair nuage-mer qui touche la mer.

## 2026-11-10 : brouillard proche dans les nuages
- Demande de l'utilisateur : dans un nuage, on devrait peiner à voir le bout du bateau.
- Cause : la marche efface la densité devant la caméra (`NearFadeDistance` 18 m, fondu de 4,5 à 18 m), si bien que l'engin restait net.
- Correction dans `clouds_composite.glsl`, presque gratuite (12 lectures de texels identiques pour tout l'écran) :
  - on rend ce voile manquant, uniforme : densité du nuage à la caméra (CPU, lissée sur 0,25 s) × extinction × longueur effacée ;
  - sa couleur est la moyenne des texels opaques d'une grille 4×3 du rendu des nuages, c'est-à-dire la couleur du nuage autour ;
  - `CloudField.NearFogBoost` 4 : on peine à voir la proue (≈ 6 m) dans un nuage dense. 1 serait la valeur physique, avec une visibilité d'environ 20 m à densité 1.
- Paramètres : un `vec4 near_fog` ajouté en fin de tampon (`ParamsBytes` 464 → 480).
- Test : `-- --in-cloud` place l'engin dans un cœur de nuage dense.

## 2026-11-10 : éclairs fins de loin
- Le halo des éclairs vient du glow de l'environnement (seuil HDR 8). Sa taille est fixe à l'écran, donc de loin il écrasait un canal d'un pixel sous une grosse tache blanche.
- `lightning_channel.gdshader` plafonne la luminosité du canal avec la distance : aucun plafond jusqu'à 300 m (`far_start`), puis un plafond à 7 à partir de 2,5 km (`far_end`, `far_glow_cap`), juste sous le seuil du glow. Le tonemap Filmic sature bien avant 7 : le trait reste aussi éclatant, seul le halo disparaît. Une première version qui baissait toute l'énergie (45 %) ternissait le trait. De près, rien ne change, y compris la frappe sur l'engin.
- Mais l'épaisseur gênante à quelques centaines de mètres venait surtout du ruban lui-même : 2,5 m de demi-largeur, soit environ 7 pixels de large à 400 m. Le plafond de largeur en pixels descend donc lui aussi avec la distance : 4 pixels de demi-largeur jusqu'à 150 m (`thin_start`), puis 1 pixel au-delà de 600 m (`thin_end`, `far_core_px`). La frappe sur l'engin reste épaisse.
- Test : `-- --test-bolts` fait tomber un éclair devant la caméra toutes les 0,8 s, à 400 m, 1,5 km puis 4 km.

## 2026-11-10 : relief et réfraction des gouttes de pluie
- La normal map d'une traînée (`Resources/Textures/WaterDropNormal.png`) est recadrée sur la traînée, la tête de la goutte en bas de l'image, puis appliquée au trait de pluie dans `precip_draw.gdshader`. Elle s'ajoute au cylindre existant. Elle donne des reflets plus vivants et une réfraction : la goutte montre la scène derrière elle, décalée par son relief (`RainRefractionPixels`, `RainRefraction`).
- Trajectoire, largeur, longueur et transparence restent celles d'avant. La grêle garde son rendu.
- Seulement sur les gouttes de plus de 2 pixels de large à l'écran, avec une montée jusqu'à 5 pixels. Sur un trait d'un pixel, le relief faisait scintiller des pixels isolés (pointillés) ; les gouttes lointaines gardent donc exactement leur rendu d'origine.
- La texture classique (`WaterDrop.png`) n'est pas utilisée. En masque de forme, elle changerait l'aspect des gouttes, que l'on veut garder.
- Test : `-- --rain` force une pluie pleine, hors orage.

## 2026-11-10 : gouttes sur l'écran simulées (modèle Bebber)
- Les perles sur rails fixes (`waterDropMap.jpg`) sont remplacées par une simulation de gouttes, `Scripts/Effects/DropletSim.cs`, d'après « RainEffect » de L. Bebber (Codrops). « Heartfelt » de BigWings a été écarté : tout procédural, ses gouttes ne peuvent ni fusionner ni réagir à la physique.
  - **Impact** : la pluie et le nuage déposent des gouttes rondes, d'autant plus vite qu'on vole vite.
  - **Fusion** : deux gouttes qui se touchent fusionnent, en conservant le volume.
  - **Coulure** : une goutte coule quand la force × r² dépasse `StickRadius²`. La force combine la gravité vue par la caméra, qui suit le roulis, et un souffle radial depuis le point de fuite, proportionnel à l'accélération vers l'avant et au carré de la vitesse au-delà de 70 m/s. Une goutte qui coule ondule, prend une forme de larme, sème une traîne de gouttelettes et absorbe les gouttelettes croisées.
- Rendu en deux cartes :
  - **carte d'eau** : une SubViewport HDR où chaque goutte écrit sa hauteur d'eau, en calotte parabolique additive, plus son rayon ;
  - **carte d'essuyage** : une SubViewport en quart de résolution, jamais effacée, où les gouttes qui coulent tracent leur sillon et où un voile translucide fait revenir la buée.
- `screen_droplets.gdshader` garde l'optique d'avant (lentille inversée, bord sombre, reflet, éclat). Le bord sombre et le reflet sont réservés aux gouttes de plus de 2 à 4 px, sinon les gouttelettes faisaient des points noirs et blancs.
- La buée est un voile global, flou et irrégulier, qui monte dans le nuage : des particules d'environ 1 px ne couvraient pas 1 % de l'écran. Les gouttes qui coulent y tracent des sillons nets, qui se referment lentement.
- Coût mesuré : environ 0,4 ms par image (~110 images/s avec, ~115 sans, sans vsync, sous la pluie).
- Tests : `-- --rain` (pluie forcée), `-- --drops-accel=T` (accélération de 20 m/s² de T à T + 1,5 s), `-- --in-cloud`.
- Retouches du même jour, à la demande de l'utilisateur :
  - **Impact écrasé** : une goutte apparaît en disque presque plat (épaisseur 0,35 px), 1,8 fois plus large que sa taille finale, au bord festonné. Elle se rassemble en 0,5 s (`GatherTime`, `SplashSpread`) et ne peut couler qu'une fois rassemblée.
  - **Alignement sur la démo RainEffect** (réglages relevés dans son code source) :
    - gouttes de 9 à 38 px, tirées en puissance 3 ; jusqu'à 900 gouttes ; coulure à partir de 18 px ;
    - gouttes immobiles pendantes : étirées jusqu'à ×1,35 dans le sens de la gravité ;
    - bruine de 1,5 à 4,5 px sous la pluie (`DrizzleRate`) ;
    - traîne de vraies petites gouttes (20 à 45 % du rayon), la goutte perdant 3 % ; glisse par à-coups.
  - **Rendu** : vitre mouillée floue sous la pluie aussi (`RainFog`), nette à travers les gouttes (`lens_blur` 0) ; ombre portée sous les gouttes ; bord sombre allégé (0,3).
  - Coût remesuré : environ 1,1 ms par image (~96 images/s avec, ~108 sans, sous la pluie), au-dessus de la cible de 0,5 ms. Leviers si besoin : `MaxDrops`, `DrizzleRate`, `RainFog`.
  - **Gouttes plus discrètes**, à la demande de l'utilisateur :
    - calotte plus plate : `dome_height` 0,3, au lieu de 0,5 fois le rayon ;
    - bord sombre 0,12 ; reflet 0,8 ; éclat 0,2 ; ombre portée 0,15.
  - **Plus de grosses gouttes qui surgissent bombées** :
    - après une fusion, le rassemblement est la moyenne pondérée par le volume (avant, il était forcé à « rassemblée ») : une goutte écrasée qui tombe sur une autre fait s'étaler le tout, qui se rassemble ensuite ;
    - le rassemblement dure de 0,6 à 1,4 × `GatherTime` selon la taille : les grosses gouttes sont plus lentes.
  - **Rien de peint par-dessus les gouttes**, à la demande de l'utilisateur. Il ne reste que :
    - la **déformation** (lentille) ;
    - un **reflet tiré de l'image** : la goutte reflète la scène autour d'elle (`reflect_extent_px`, légèrement flou), et seules les zones dont la luminance dépasse 0,6 y font un éclat, renforcé sur les bords (Fresnel). Sans lumière dans la scène, il n'y a pas de reflet.
    - Supprimés : le reflet à direction de lumière fixe, l'éclat opposé, le bord sombre, l'ombre portée, la teinte bleutée, le bruit du voile de buée et son éclaircissement.

## 2026-11-10 : coque du CloudSurfer en résine
- La texture de rugosité exportée par Blender est grise uniforme : la coque était traitée comme un métal mat à 98 %, sans reflet.
- Nouveau matériau `Resources/Materials/CloudSurferResin.tres`, appliqué à la coque (`Model/Plane`) dans `CloudSurfer.tscn` :
  - texture de couleur d'origine ;
  - base non métallique, rugosité 0,35 ;
  - vernis (clearcoat) 1, rugosité 0,06 : reflets du ciel et éclat du soleil.
- Choix de l'utilisateur : **rester physique**, plutôt que des reflets stylisés ou une capture des nuages. Au crépuscule, les reflets restent discrets : le ciel est un dégradé uni et la mer est sombre.
- Ciel (`sky_atmosphere.gdshader`) : sous l'horizon, le passage ciel -> mer des reflets est resserré (`under_blend` 0,06, au lieu de 0,25) pour une ligne d'horizon plus nette dans les surfaces vernies. Effet de bord : la lumière ambiante venant d'en dessous est un peu plus sombre.
- Les reflets d'écran (SSR) ont été essayés puis retirés : aucun gain visible sur la coque, pour un coût notable sur toute la scène.
