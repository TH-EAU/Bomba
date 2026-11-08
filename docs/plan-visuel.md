# Plan : phase visuelle (nuages, ciel, mer)

## Contexte
Le POC de vol est validé par l'utilisateur. La phase visuelle doit apporter :
- **Les nuages volumétriques**, priorité absolue et **élément de gameplay**. Méthode de référence : *Rendering volumetric clouds using signed distance fields* (uhawk). Elle repose sur un SDF précalculé dans une texture 3D, un ray marching qui fait de grands pas hors du nuage et des pas fixes dedans, une atténuation de Beer-Lambert et des rayons d'ombre.
- **L'ambiance crépuscule rose et bleu** de l'image de référence 4 : ciel dégradé bleu nuit, puis bleu, lavande et rose à l'horizon, cirrus peints en traits, mer sombre avec une traînée de reflet lumineuse et des points lumineux.
- **Les véhicules restent en graybox.** L'utilisateur fournira les modèles plus tard (style « réaliste et patiné »).

Contraintes : le GPU de l'utilisateur est une **GTX 1050 Ti**, donc les nuages sont rendus en **demi-résolution**. La cible est Godot 4.5, renderer Forward+.

## Gameplay des nuages (les quatre rôles retenus)
1. **Cachette** : un observateur (projecteur, tourelle, ennemi) ne voit pas une cible si la transmittance du nuage sur la ligne de vue est faible.
2. **Perturbation du vol** : dans le nuage, traînée accrue, turbulences (forces et couples bruités) et visibilité réduite.
3. **Courants d'air** : ascendances sous les tours de cumulus et sur leurs flancs. Les nuages sont une `IWindSource`.
4. **Orage** : des cœurs d'orage dans certaines tours, avec éclairs aléatoires (flash, secousse, impulsion). Il n'existe pas encore de système de dégâts, donc l'éclair coupe brièvement les moteurs.

## Architecture
### CloudField (`Scripts/Clouds/CloudField.cs`), source de vérité unique
Il est calculé une fois au chargement, sur le CPU et en parallèle. Les mêmes données servent au rendu (texture 3D) et au gameplay (échantillonnage CPU) : ce que le joueur voit est ce qui le cache.
- Grille de 128 × 48 × 128, qui se répète à l'horizontale (période d'environ 2 km), sur une altitude d'environ -10 à 230 m.
- **Densité** : une couche (profil vertical × couverture 2D tuilable), plus des tours de cumulus (colonnes, sommet en enclume), le tout érodé par du bruit Perlin-Worley tuilable.
- **SDF** : transformée de distance euclidienne exacte (Felzenszwalb), séparable, en mètres, avec des cellules anisotropes. Valeur négative à l'intérieur.
- Canaux de la texture 3D (RGBA demi-flottant) : R = SDF (m), G = densité, B = orage, A = ascendance.
- **Dérive** : décalage horizontal qui défile avec le vent, partagé entre le shader et le CPU.
- API : `Sample(pos)`, `GetTransmittance(from, to)`, `GetWindContribution(pos)`, `TriggerLightning(pos)`.

### CloudRenderer (`Scripts/Clouds/CloudRenderer.cs` + `Shaders/Clouds/*.glsl`)
Un `CompositorEffect` attaché à la caméra, en deux passes compute :
1. **Ray marching en demi-résolution** : grands pas guidés par le SDF hors du nuage, pas fixes dedans, bruit de détail 3D près des bords, gigue de départ (bruit de gradient entrelacé), arrêt à la profondeur de la scène opaque et sortie anticipée. Éclairage : marche vers le soleil (Beer-Lambert), phase Henyey-Greenstein double, terme « powder », ambiance ciel en haut et mer en bas, assombrissement des orages, flash des éclairs.
2. **Composition en pleine résolution** : agrandissement de l'image demi-résolution en tenant compte de la profondeur, puis fusion `couleur × T + diffusion`.

### Le reste (délégué)
- Ciel crépuscule (shader de ciel) et mer stylisée (shader d'océan), dans les tons de l'image 4. Les îlots passent en roche sombre.
- Effets du nuage sur le vol (composant `CloudInteraction`), éclairs, HUD (« DANS LE NUAGE », « CACHÉ », « ORAGE ») et effets de caméra.
- **Projecteur** de test sur un îlot : faisceau visible qui balaie l'espace et passe en alerte s'il voit le joueur hors des nuages. C'est la démonstration de la cachette.

## Ordre d'exécution
| # | Mission | Qui | Dépend de |
|---|---|---|---|
| V1 | CloudField : calcul, SDF, échantillonnage, vent, API | Chef de projet | — |
| V2 | CloudRenderer : CompositorEffect et shaders de nuages | Chef de projet | V1 |
| M8 | Ciel crépuscule, mer, roche des îlots, lumière | Haiku | — |
| M9 | CloudInteraction : effets sur le vol, éclairs, HUD, caméra | Haiku | V1 |
| M10 | Projecteur détecteur (cachette) | Haiku | V1 |
| V3 | Réglage artistique, performance, captures | Chef de projet | tout |

## Vérification
- `dotnet build`, ouverture des scènes dans l'éditeur sans erreur, sonde de vol inchangée hors des nuages.
- Test headless du CloudField : temps de calcul, proportion de cellules couvertes, cohérence du SDF (signe, gradient).
- Captures d'écran en jeu (`--write-movie`) depuis plusieurs points de vue : au-dessus de la couche, dedans, dessous, près d'une tour et dans un orage.
- Mesure des FPS sur la GTX 1050 Ti (cible ≥ 60 FPS à 1600 × 900, nuages en demi-résolution).
