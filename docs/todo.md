# TODO (non prioritaire)

## Cirrus plus réalistes dans le ciel
- **Constat (utilisateur, 2026-11-08)** : les traits de cirrus du ciel (`Shaders/Sky/sky_dusk.gdshader`, bruit FBM étiré) ne ressemblent pas à de vrais cirrus. On vise des « queues de cheval » : filaments fibreux et fins, avec une tête un peu plus dense d'où partent de longues traînées courbes et effilées.
- **Pas de volumétrique** : ils restent dans le shader de ciel (2D, coût négligeable).
- **Deux options** :
  1. **Texture fournie par l'utilisateur** (recommandé, simple) : une texture de cirrus en niveaux de gris, avec transparence et tuilable, échantillonnée dans le shader de ciel sur la projection plane existante (`EYEDIR.xz / max(EYEDIR.y, 0.05)`). On la teinte en bleu clair, lavande ou saumon selon la hauteur et le côté du soleil, comme aujourd'hui, et on la fait défiler lentement avec le vent.
  2. **Procédural** : un bruit de flux (domain warping) fortement anisotrope pour les fibres, multiplié par un masque de « têtes » (Worley seuillé) étiré dans la direction du vent.
- **Si délégué** : une mission Haiku courte, avec un seul fichier à modifier (`sky_dusk.gdshader`) et une vérification par capture des plans 0 et 5 de `CloudShots`.
