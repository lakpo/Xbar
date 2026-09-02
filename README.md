# Xbar

Barre de raccourcis en surimpression de l'Explorateur Windows 11.

Xbar ajoute une petite barre verticale **à l'intérieur du bord droit de chaque fenêtre de
l'Explorateur**. Elle suit sa fenêtre, partage la même configuration partout, et ne modifie
rien dans `explorer.exe` : **aucune injection de DLL, aucun patch, aucun droit administrateur**.

- C# / .NET 10 / WPF
- Interface en **français et en anglais** (suit la langue de Windows par défaut)
- Win32 + COM du Shell + UI Automation
- Windows 11 (build 22000+), x64

## Fonctionnalités

**La barre**
- une barre par fenêtre Explorer, positionnée à l'intérieur du bord droit ;
- suit les déplacements, redimensionnements, maximisation, minimisation ;
- reste au-dessus de son Explorer sans jamais passer devant les autres applications ;
- se masque quand la fenêtre devient trop petite ;
- l''écart au bord droit vaut ''max(marge, largeur de la scrollbar)'' : la marge et l''évitement
  de la barre de défilement ne s''additionnent pas, et ce dernier ne joue que lorsque la marge
  choisie est insuffisante — une marge de 0 colle donc vraiment au bord si la vue ne défile pas ;
- thème clair/sombre automatique, opacité, taille des boutons, masquage automatique ;
- poignée en haut de la barre : la faire glisser verticalement repositionne la barre, et la
  position est mémorisée en fraction de la hauteur de la fenêtre (elle tient donc quelle que
  soit la taille de l''Explorateur) ; les presets Haut/Centre/Bas reprennent la main ;
- trois modes d'affichage : icônes seules, icône + texte, ou **icônes avec le texte au survol**
  (mode par défaut : la barre s'élargit en 200 ms quand la souris passe dessus).

**Raccourcis**
- glisser un **dossier**, un **fichier**, un **`.exe`** ou un **`.lnk`** sur la barre pour
  l'ajouter (les `.lnk` sont résolus vers leur cible) ; un fichier s'ouvre avec
  l'application qui lui est associée, comme un double-clic dans l'Explorateur ;
- réorganisation par glisser-déposer, séparateurs, renommage, icône personnalisée, suppression ;
- éditeur : nom, chemin, arguments, dossier de travail (défaut / dossier Explorer courant /
  personnalisé), acceptation des fichiers déposés ;
- jeton `{currentFolder}` dans les arguments, par exemple `cursor.exe "{currentFolder}"` ;
- un fichier déposé sur un bouton d'**application** lui est passé en argument, comme sur son
  exécutable ;
- un fichier déposé sur un bouton de **dossier** y est déplacé (copié entre volumes, Ctrl pour
  forcer la copie, Maj le déplacement) ;
- pendant le survol, l'icône du bouton visé grandit de 20 % et une étiquette annonce l'effet
  (« Copier vers : *dossier* », « Ouvrir avec : *application* »).

**Dossiers**
- clic gauche : naviguer dans l'onglet courant ;
- clic molette : nouvel onglet ;
- Ctrl + clic : nouvelle fenêtre.

**Actions intégrées**
- *Coller l'image du presse-papiers* dans le dossier affiché, nommée
  `Clipboard_2026-09-01_13-42-18.png` ; format PNG / JPG / WebP, nom demandé en option,
  retour visuel discret sans boîte de dialogue ;
- *Dossiers récents* : menu des 20 derniers dossiers parcourus (clic pour y aller, Ctrl+clic
  pour une nouvelle fenêtre), persisté dans `history.json`. La taille de l'historique se règle
  avec `maxHistoryFolders` dans `settings.json`.

**Double-clic dans le vide**
- double-clic dans une zone vide de la vue des fichiers → dossier parent ;
- ignoré sur un fichier, un dossier, le panneau de navigation, la barre d'adresse, la
  recherche, la barre elle-même, le Bureau, et pendant un glisser-déposer ;
- ne fait rien à la racine d'un disque.

**Divers**
- icône dans la zone de notification, fenêtre de paramètres, menu contextuel sur la barre ;
- instance unique (une deuxième exécution ouvre les paramètres de la première) ;
- vérification discrète des mises à jour via GitHub Releases, désactivable, jamais automatique ;
- aucune télémétrie, aucun envoi réseau hors vérification de version, journaux locaux.

## Configuration

| Mode | Emplacement |
|---|---|
| Installé | `%AppData%\Xbar\` |
| Portable | `Data\` à côté de `Xbar.exe` (marqueur : fichier `portable`) |

Contenu : `settings.json`, `shortcuts.json`, `history.json`, `icons\`, `logs\`.

Au premier lancement, la barre contient le dossier Images de l'utilisateur (avec le nom
localisé par Windows), un séparateur, puis les deux actions intégrées.

Le démarrage avec Windows est activé par défaut en version installée, désactivé en portable
(clé `HKCU\...\CurrentVersion\Run`, donc sans UAC).

## Compilation

```bash
dotnet build src/Xbar/Xbar.csproj
```

## Livrables

```powershell
pwsh build\publish.ps1
```

Produit dans `dist\` la version portable (dossier + zip) et, si
[Inno Setup 6](https://jrsoftware.org/isdl.php) est installé, l'installateur
`Xbar-<version>-setup.exe` (installation dans le profil utilisateur, sans UAC).

Les deux versions sont **autonomes** : aucun runtime .NET n'est requis chez l'utilisateur.

## Architecture

```
src/Xbar/
  Core/                 chemins, journaux, configuration, thème, instance unique, démarrage Windows
  Interop/              P/Invoke Win32, hooks WinEvent, interfaces COM du Shell
  ExplorerIntegration/  détection des fenêtres, navigation, double-clic dans le vide
  Overlay/              fenêtre de la barre, placement, glisser-déposer, menus, retour visuel
  Shortcuts/            modèle, stockage, lancement, résolution des .lnk
  Actions/              actions intégrées (collage d'image, dossiers récents)
  Icons/                icônes du Shell et icônes personnalisées
  Tray/                 icône de la zone de notification
  UI/                   paramètres, éditeur de raccourci, saisie
  Update/               vérification GitHub Releases
  Localization/         textes de l'interface (Resources/Strings.resx + .fr.resx)
```

## Notes techniques

- **Détection des fenêtres** : `SetWinEventHook` hors processus (création, destruction,
  déplacement, minimisation, premier plan) et une réconciliation légère toutes les 5 s qui sert
  de filet de sécurité, notamment quand `explorer.exe` redémarre.
- **Superposition** : la barre est une fenêtre WPF `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
  déclarée *possédée* par la fenêtre Explorer (`GWLP_HWNDPARENT`). C'est ce qui la maintient
  au-dessus de son Explorer sans la rendre Always-On-Top.
- **Dossier courant et navigation** : objets COM publics de l'Explorateur
  (`ShellWindows` / `IShellBrowser` / `IFolderView`). Sous Windows 11 les onglets d'une même
  fenêtre partagent un HWND et possèdent chacun leur `IShellBrowser`, sans qu'aucune propriété
  n'indique lequel est affiché — leurs vues sont toutes marquées visibles, avec la même
  géométrie. L'onglet actif est donc identifié par l'**ordre Z** : son conteneur
  `ShellTabWindowClass` est le premier de sa fratrie. La navigation passe ensuite par
  `IShellBrowser::BrowseObject` (`SBSP_SAMEBROWSER`, et `SBSP_PARENT` pour remonter).
- **Nouvel onglet** : Windows n'expose aucune API publique pour les onglets de l'Explorateur.
  Xbar crée l'onglet avec `Ctrl+T` puis y navigue par COM ; si l'onglet n'apparaît pas, il
  ouvre une nouvelle fenêtre. C'est le seul endroit où une frappe est simulée.
- **Historique des dossiers** : l'Explorateur ne signale pas ses navigations, mais renomme sa
  fenêtre à chaque changement de dossier ou d'onglet actif. Xbar écoute donc
  `EVENT_OBJECT_NAMECHANGE` et relit le dossier affiché — toujours sans polling.
- **Barre de défilement** : la vue moderne la dessine en DirectUI — ni WS_VSCROLL ni
  GetScrollBarInfo ne la voient (un dossier de plusieurs milliers de fichiers est rapporté
  « invisible »). Xbar interroge donc le ScrollPattern par UI Automation, hors du thread UI,
  avec mise en cache et revérification à la navigation ou au redimensionnement.
- **Taille des icônes** : Windows n''agrandit pas une icône absente d''un format donné, il la
  **centre** dans le canevas — une icône de dossier personnalisée n''occupe ainsi que 48 px sur
  les 256 de la liste JUMBO. Xbar choisit la liste d''images adaptée à la taille de rendu et
  mesure le remplissage réel, en retombant sur un format plus petit si le dessin est trop noyé.
- **Langues** : les textes vivent dans `Resources\Strings.resx` (anglais, langue neutre) et
  `Strings.fr.resx`, compilé en assembly satellite et embarqué dans l'exécutable unique.
  Le XAML les résout à l'analyse via `{loc:Loc Cle}` : changer de langue rouvre donc la
  fenêtre des paramètres. Ajouter une langue = un fichier `.resx` de plus.
- **Double-clic dans le vide** : hook souris bas niveau (hors processus), puis vérification de
  la chaîne des classes de fenêtres (`SHELLDLL_DefView`) et enfin UI Automation pour distinguer
  une zone vide d'un élément de liste. Le clic n'est jamais consommé.

## Hors périmètre V1

Profils, plugins, scripts personnalisés, gestion multi-écrans avancée, DPI par écran,
remplacement des onglets natifs.
