# Split

Divise un écran ultra-large en deux « écrans » côte à côte — ou en trois (25 / 50 / 25) : agrandir une fenêtre ou la
passer en plein écran ne remplit plus que sa moitié (sa zone).

Split range les fenêtres ; il ne crée pas de vrais écrans. Windows et les programmes continuent de voir un seul
écran (voir *Limites*).

## Lancer

Double-cliquez sur `Split.exe`. Une icône bleue et orange apparaît près de l'horloge (parfois derrière la flèche `^`).
Clic droit dessus : options, répartition (50/50, 60/40, 67/33… ou trois écrans : 25/50/25), aide, lancement au
démarrage de Windows, quitter.

Les fenêtres déjà agrandies ou en plein écran au moment du lancement ne sont pas touchées.

## Ce que fait Split

| Action | Résultat |
|---|---|
| Agrandir (bouton, double-clic sur le titre, Win+Haut, glisser en haut de l'écran) | La fenêtre remplit sa moitié. Un second clic la restaure. |
| Agrandir en maintenant **Maj** | Vraie maximisation sur tout l'écran. |
| Plein écran (F11, vidéo YouTube, lecteur vidéo, jeu « fenêtré sans bordure ») | Limité à la moitié où se trouve la fenêtre, par-dessus la barre des tâches de cette moitié. |
| Fenêtre sans bordure à la taille d'une moitié (jeu réglé en 1920 × 1080) | Reconnue comme plein écran d'une moitié, et rangée dedans quand le jeu se pose au milieu de l'écran (à son lancement, ou en changeant de résolution). |

## Raccourcis (modifiables)

| Raccourci | Action |
|---|---|
| Ctrl+Alt+Gauche / Droite | Envoyer la fenêtre active dans l'écran voisin, à gauche / à droite (elle garde son état) |
| Ctrl+Alt+Haut | Agrandir dans la moitié, ou restaurer |
| Ctrl+Alt+Bas | Restaurer |
| Ctrl+Alt+Entrée | Plein écran sans bordure dans la moitié (pour les jeux en mode fenêtré), ou en sortir |
| Ctrl+Alt+L | Bloquer / libérer la souris dans la moitié |
| Ctrl+Alt+S | Activer / désactiver Split |

## Trois écrans (25 / 50 / 25)

Clic droit sur l'icône > *Répartition* > *25 / 50 / 25 (trois écrans)* : sur un écran de 3840 × 1080, cela donne
960 px à gauche, 1920 px au centre et 960 px à droite. D'autres répartitions à trois sont proposées (33/34/33,
20/60/20, 30/40/30) ; n'importe laquelle s'écrit dans la configuration (`split=30/45/25`).

- Tout fonctionne comme à deux écrans, zone par zone : agrandir, plein écran, blocage de la souris.
- Ctrl+Alt+Gauche / Droite fait passer la fenêtre d'une zone à sa voisine.
- Un jeu sans bordure en 1920 × 1080 occupe exactement la zone centrale : Split le reconnaît sans le déplacer.
- Une fenêtre ancrée par Windows contre un bord de l'écran (Win+Gauche / Droite) est recoupée à la zone de ce bord.
- En changeant de répartition, les fenêtres agrandies ou en plein écran prennent la zone où elles se trouvent ; un
  plein écran choisit de préférence une zone à sa taille.

## Jeux

- Le plein écran **exclusif** prend tout l'écran physique : aucun logiciel ne peut le couper en deux.
  Réglez le jeu sur **Fenêtré** ou **Fenêtré sans bordure**.
- Choisissez dans le jeu la résolution d'une moitié (1920 × 1080 sur un écran 3840 × 1080), sinon l'image est écrasée.
- Un jeu sans bordure à cette résolution se centre sur l'écran entier, à cheval sur les deux moitiés : Split le range
  dans une moitié (celle où il était, sinon celle du curseur). Ctrl+Alt+Gauche / Droite pour changer de côté ; Split
  retient ce côté pour les prochains lancements.
- Un jeu qui revient de lui-même au milieu à chaque fois est laissé tranquille après trois essais (une bulle le
  signale) : passez-le en mode fenêtré, puis Ctrl+Alt+Entrée.
- Jeu fenêtré avec bordures : Ctrl+Alt+Entrée retire les bordures et remplit la moitié.
- La souris sort du jeu vers l'autre moitié : Ctrl+Alt+L la bloque dans la moitié (et la libère).
- Jeu lancé en administrateur : clic droit sur l'icône > *Redémarrer en administrateur*.
- Split n'injecte rien dans les autres programmes : il observe les fenêtres et les déplace, comme Windows le fait.

## Chrome, Edge, Opera… en plein écran

Ces navigateurs (moteur Chromium) redonnent d'eux-mêmes à leur fenêtre plein écran la taille de l'écran physique
entier à chaque fois qu'elle perd ou retrouve le focus. Split la ramène aussitôt dans sa moitié (en 3 à 5 ms), et
les autres fenêtres du même navigateur peuvent s'activer sans la déranger.

Ce retour ne peut pas être empêché de l'extérieur : un bref éclair sur l'autre moitié reste possible au moment où
l'on clique dans la vidéo ou où l'on en sort. Pour le réduire :

- ancrez la fenêtre du navigateur avec Win+Gauche / Win+Droite avant de lancer la vidéo : d'après le code de
  Chromium, une fenêtre ancrée ne se redimensionne plus qu'en perdant le focus, et elle est alors derrière la
  fenêtre où l'on vient de cliquer ;
- ou décochez *Le plein écran recouvre la barre des tâches* : la vidéo s'arrête au-dessus de la barre et ne passe
  plus devant les autres fenêtres.

## Limites

- Ce ne sont pas de vrais écrans : Windows n'en voit toujours qu'un. Un programme qui se centre, s'ouvre en plein
  écran ou choisit sa résolution le fait d'après l'écran entier ; Split le replace ensuite, ce qui peut se voir un
  court instant. Pour deux écrans réellement séparés (y compris en plein écran exclusif), il faut le mode
  PBP / « Picture by Picture » du moniteur, avec deux câbles vers la carte graphique : Split ne sert alors plus.
- La barre des tâches reste unique, sur toute la largeur.
- Win+Gauche / Win+Droite et le glisser contre un bord restent ceux de Windows, qui ne connaît que l'écran entier.
  Si la fenêtre ainsi ancrée déborde sur l'autre moitié (répartition autre que 50/50, ou option Windows « remplir
  l'espace disponible » à côté d'une autre fenêtre ancrée), Split la recoupe à la limite. Un bord tiré à la souris
  ensuite est respecté. Pour remplir exactement une moitié, le plus sûr reste « Agrandir ».
- Une application qui réimpose sans cesse sa taille est laissée tranquille après quelques essais (une bulle le signale).
- Pas testé avec Firefox.

## Configuration

`%APPDATA%\Split\config.ini`, rechargé à chaque enregistrement (menu *Modifier la configuration…*) :
répartition (`split=50/50`, `split=25/50/25`…), raccourcis, applications ignorées (`exclude`), écran préféré par
application (`remember`, numéroté de gauche à droite).
Avec `debug=true`, Split écrit ses décisions dans `split.log` au même endroit.

## Développement

- `build.bat` ferme proprement Split s'il tourne (`Split.exe --quit`), puis le recompile avec le compilateur C#
  fourni avec Windows (rien à installer).
- `tools\sandbox.ps1` lance un test sur un bureau Windows caché, avec une instance de Split dédiée : rien n'apparaît
  à l'écran et le focus n'est jamais pris. `-Exe` essaie une autre compilation sans remplacer le Split qui tourne ;
  `-Ini tools\test3.ini` fait tourner les tests à trois zones.
- `tools\test-ghost.ps1` : fenêtres ordinaires (plein écran, agrandissement, commandes, « jeux » qui se centrent,
  changent de résolution ou refusent leur moitié, fenêtre déplacée d'une moitié à l'autre).
- `tools\test-chrome.ps1` : plein écran d'un navigateur Chromium, avec de vrais changements de focus. `-Taskbar`
  ajoute une fausse barre des tâches (le bureau caché n'en a pas) et vérifie qu'aucune fenêtre ne reste « toujours
  visible » après le plein écran ; `-Yield` la fait s'effacer devant un plein écran actif, comme la vraie.
- `src\Engine.Chromium.cs` explique en tête de fichier le comportement de Chromium et la parade.
