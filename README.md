# Multicompte Dofus

Application Windows (WPF, .NET 10) qui détecte les instances de Dofus ouvertes et facilite le jeu en multicompte.

## Fonctionnalités

- **Détection automatique** : chaque fenêtre `Dofus.exe` apparaît dès son ouverture. La détection combine le hook shell de Windows (création, fermeture et renommage de fenêtres) et un balayage toutes les secondes.
- **Infos du personnage** :
  - le **pseudo** et la **classe** sont lus dans le titre de la fenêtre (`Tikoko - Zobal - 3.7.13.17 - Release`) ;
  - l'**image** est un aperçu en direct de la fenêtre, rafraîchi toutes les 2,5 s, même quand elle est cachée derrière d'autres ;
  - le **niveau** n'est exposé nulle part par le client : il se saisit sur la carte et reste mémorisé pour ce pseudo.
- **Meneur** : le bouton « Définir meneur » d'une carte, mémorisé par pseudo.
- **Au début du tour d'un personnage** :
  - « Mettre sa fenêtre au premier plan » : la fenêtre du personnage qui doit jouer passe devant ;
  - « Passer le tour de tous, sauf le meneur » ;
  - « Passer aussi le tour du meneur » ;
  - la case « Inclure dans les automatisations » d'une carte exclut ce personnage de tout automatisme.
- **Lancement des instances** :
  - chaque personnage déjà vu reste dans la liste quand son instance est fermée (carte « Hors ligne », avec sa classe et sa dernière image) ;
  - « Lancer » sur une carte hors ligne, « Tout lancer » pour ouvrir une instance par personnage hors ligne (avec une pause de 4 s entre chaque), « Lancer une instance » pour en ouvrir une de plus ;
  - le lancement passe par Ankama Launcher (lien `zaap://actual/games/game/dofus/dofus3?launch`, la même action que son bouton « Jouer ») : l'application ne lit ni ne transmet aucun identifiant ;
  - « Retirer » oublie un personnage hors ligne ;
  - option « Entrer en jeu automatiquement » (activée par défaut) : si une instance lancée depuis l'application s'arrête à l'écran de choix du personnage, l'application repère le bouton « Jouer » dans l'image de la fenêtre et clique dessus. Le personnage qui entre en jeu est celui présélectionné par Dofus (le dernier joué sur ce compte). Une instance ouverte autrement, ou un retour volontaire au choix du personnage, n'est jamais concerné.
- **Raccourcis globaux** : personnage suivant, précédent, retour au meneur (par défaut `Ctrl+→`, `Ctrl+←`, `Ctrl+↑`). Ils suivent l'ordre des cartes, réglable avec ↑ et ↓.
- **Journal** des détections, des tours et des actions.

## Réglages à faire dans Dofus

1. **Signal de début de tour.** L'application sait que c'est le tour d'un personnage parce que Dofus fait **clignoter sa fenêtre dans la barre des tâches** quand elle n'est pas au premier plan. Active la notification externe de début de tour dans les options de Dofus (rubrique Notifications). Pour vérifier : en combat, le journal doit afficher « Début de tour : <pseudo> » ou l'action effectuée.
2. **Touche « Passer son tour ».** Vérifie le raccourci dans *Options › Raccourcis* et reporte-le dans le champ « Touche » de l'application (F1 par défaut). Le bouton « Passer le tour » d'une carte envoie la touche immédiatement, pour tester.

## Limites connues

- Le signal de tour, c'est le clignotement de la fenêtre. Dofus ne fait pas clignoter une fenêtre déjà au premier plan : le tour du personnage affiché à l'écran n'est donc pas détecté. Cela touche surtout « Passer aussi le tour du meneur », qui ne fonctionne que si la fenêtre du meneur n'est pas active.
- Dofus fait aussi clignoter sa fenêtre pour d'autres événements (message privé, invitation…). Si le passage automatique est actif, la touche est alors envoyée hors de ton tour ; en dehors d'un combat, elle n'a normalement pas d'effet.
- Le launcher ne permet pas de choisir le compte depuis un lien : chaque instance s'ouvre sur le **compte actif d'Ankama Launcher**, à l'écran de choix du personnage (ou directement en jeu selon le réglage « Connexion » du launcher). Pour un personnage d'un autre compte, change de compte dans le launcher ou dans la fenêtre qui s'ouvre.
- En mode normal, la touche est envoyée en activant la fenêtre une fraction de seconde, puis la main revient à la fenêtre précédente. Une frappe faite pendant cet instant peut atterrir dans la mauvaise fenêtre. Le mode « arrière-plan » évite ce basculement, mais le client Unity peut ignorer la touche.

## Avertissement

Ankama tolère les outils qui aident à **passer d'une fenêtre à l'autre**. Le **passage de tour automatique** fait jouer une action sans intervention humaine, ce que les conditions d'utilisation de Dofus interdisent. L'utiliser expose les comptes à une sanction. Ces options sont désactivées par défaut.

## Compiler et lancer

Prérequis : SDK .NET 10.

```bash
dotnet build DofusMulticompte.slnx
```

```bash
dotnet test DofusMulticompte.slnx
```

```bash
dotnet run --project src/DofusMulticompte.App
```

Pour un exécutable autonome :

```bash
dotnet publish src/DofusMulticompte.App -c Release -r win-x64 --self-contained false -o publish
```

Les réglages sont enregistrés dans `%APPDATA%\DofusMulticompte\settings.json`, et les dernières images des personnages dans `%APPDATA%\DofusMulticompte\portraits`. Rien n'est envoyé ailleurs.

## Organisation du code

| Projet | Rôle |
|---|---|
| `src/DofusMulticompte.Core` | Logique pure et testée : lecture du titre, classes, règles d'automatisation (`TurnPolicy`), anti-rebond des signaux, raccourcis, réglages JSON. |
| `src/DofusMulticompte.App` | Application WPF : interop Win32 (`Interop/`), services (scan des fenêtres, hook shell et raccourcis, capture, envoi de touches, automatisation des tours), ViewModels et interface. |
| `tests/DofusMulticompte.Core.Tests` | Tests xUnit du cœur. |
