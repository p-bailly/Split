# Split

Divise un écran ultra-large en deux « écrans » côte à côte — ou en trois (25 / 50 / 25) : agrandir une fenêtre ou la
passer en plein écran ne remplit plus que sa zone. Split range les fenêtres ; Windows continue de ne voir qu'un seul
écran (voir *Limites*).

## Lancer

Double-cliquez sur `Split.exe`. Une icône bleue et orange apparaît près de l'horloge. Clic droit dessus : options,
répartition (50/50, 60/40, 25/50/25…), aide, lancement au démarrage, quitter. Les fenêtres déjà agrandies au
lancement ne sont pas touchées.

## Ce que fait Split

| Action | Résultat |
|---|---|
| Agrandir (bouton, double-clic sur le titre, Win+Haut) | La fenêtre remplit sa zone. Un second clic la restaure. |
| Agrandir avec **Maj** | Vraie maximisation sur tout l'écran. |
| Plein écran (F11, vidéo, jeu sans bordure) | Limité à la zone de la fenêtre, par-dessus sa barre des tâches. |
| Jeu sans bordure à la taille d'une zone | Reconnu comme plein écran de cette zone, et rangé dedans s'il se centre. |

## Raccourcis (modifiables)

| Raccourci | Action |
|---|---|
| Ctrl+Alt+Gauche / Droite | Envoyer la fenêtre active dans la zone voisine |
| Ctrl+Alt+Haut / Bas | Agrandir dans la zone / restaurer |
| Ctrl+Alt+Entrée | Plein écran sans bordure dans la zone, ou en sortir |
| Ctrl+Alt+L | Bloquer / libérer la souris dans la zone |
| Ctrl+Alt+S | Activer / désactiver Split |

## Trois écrans

*Répartition* > *25 / 50 / 25* : sur 3840 × 1080, 960 / 1920 / 960 px. Autres choix : 33/34/33, 20/60/20, 30/40/30 ;
toute répartition s'écrit dans la configuration (`split=30/45/25`). Tout fonctionne zone par zone, et un jeu sans
bordure en 1920 × 1080 occupe exactement la zone centrale.

## Jeux

- Le plein écran **exclusif** prend tout l'écran physique et ne peut pas être coupé : choisissez **Fenêtré** ou
  **Fenêtré sans bordure**, avec la résolution d'une zone (1920 × 1080 sur 3840 × 1080).
- Un jeu qui se centre à cheval sur deux zones est rangé dans l'une d'elles ; Ctrl+Alt+Gauche / Droite change de côté
  et Split s'en souvient.
- Un jeu qui revient sans cesse au milieu est laissé tranquille après trois essais (une bulle le signale) : passez-le
  en fenêtré, puis Ctrl+Alt+Entrée.
- La souris sort du jeu ? Ctrl+Alt+L la bloque dans la zone.
- Jeu lancé en administrateur : clic droit sur l'icône > *Redémarrer en administrateur*.
- Split n'injecte rien dans les autres programmes : il observe les fenêtres et les déplace.

## Chrome, Edge, Opera…

En plein écran, ces navigateurs (Chromium) reprennent la taille de l'écran entier à chaque changement de focus ; Split
les ramène aussitôt dans leur zone. Un bref éclair sur l'autre moitié reste possible. Pour le réduire : ancrez la
fenêtre (Win+Gauche / Droite) avant la vidéo, ou décochez *Le plein écran recouvre la barre des tâches*.
Détails en tête de `src\Engine.Chromium.cs`.

## Limites

- Ce ne sont pas de vrais écrans : un programme qui se centre ou choisit sa résolution se base sur l'écran entier, et
  Split le replace ensuite (visible un court instant). Pour de vrais écrans séparés, il faut le mode PBP du moniteur.
- La barre des tâches reste unique. Win+Gauche / Droite restent ceux de Windows ; Split recoupe la fenêtre à la limite
  de la zone si elle déborde. Pour remplir exactement une zone, préférez « Agrandir ».
- Une application qui réimpose sans cesse sa taille est laissée tranquille après quelques essais.
- Pas testé avec Firefox.

## Configuration

`%APPDATA%\Split\config.ini`, rechargé à l'enregistrement (menu *Modifier la configuration…*) : répartition,
raccourcis, applications ignorées (`exclude`), écran préféré par application (`remember`). Avec `debug=true`, Split
écrit `split.log` au même endroit.

## Développement

- `build.bat` ferme Split s'il tourne, puis le recompile avec le compilateur C# fourni avec Windows.
- `tools\sandbox.ps1` lance les tests sur un bureau Windows caché (aucune fenêtre, aucun vol de focus) ; `-Exe`
  essaie une autre compilation, `-Ini tools\test3.ini` teste les trois zones.
- `tools\test-ghost.ps1` : fenêtres ordinaires et « jeux ». `tools\test-chrome.ps1` : plein écran Chromium
  (`-Taskbar`, `-Yield` simulent la barre des tâches).
