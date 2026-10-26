using System.Text.Json.Serialization;

namespace Xbar.Shortcuts;

public enum ShortcutKind
{
    /// <summary>Lancement d'un exécutable, avec arguments optionnels.</summary>
    Application,
    /// <summary>Ouverture d'un fichier avec l'application qui lui est associée.</summary>
    File,
    /// <summary>Navigation vers un dossier.</summary>
    Folder,
    /// <summary>
    /// Emplacement Windows sans chemin de fichier (Ce PC, Accueil, Réseau…) : la cible est une
    /// adresse du Shell. Voir ShellLocations.
    /// </summary>
    ShellFolder,
    /// <summary>Séparateur visuel.</summary>
    Separator,
    /// <summary>Action intégrée à Xbar (voir Xbar.Actions).</summary>
    Action
}

public enum WorkingDirectoryMode
{
    /// <summary>Dossier de l'exécutable.</summary>
    Default,
    /// <summary>Dossier affiché par la fenêtre Explorer d'où part le clic.</summary>
    CurrentExplorerFolder,
    Custom
}

/// <summary>Un élément de la barre. Persisté dans shortcuts.json.</summary>
public sealed class ShortcutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ShortcutKind Kind { get; set; } = ShortcutKind.Application;
    public string Name { get; set; } = "";

    /// <summary>Chemin de l'exécutable ou du dossier cible.</summary>
    public string Target { get; set; } = "";

    /// <summary>Arguments ; peut contenir le jeton {currentFolder}.</summary>
    public string Arguments { get; set; } = "";

    public WorkingDirectoryMode WorkingDirectory { get; set; } = WorkingDirectoryMode.Default;
    public string CustomWorkingDirectory { get; set; } = "";

    /// <summary>
    /// Autorise le dépôt de fichiers/dossiers sur ce bouton : les chemins déposés sont passés
    /// en arguments à l'application, exactement comme lorsqu'on lâche des fichiers sur son
    /// exécutable. Actif par défaut ; désactivé, un dépôt crée un raccourci à la place.
    /// </summary>
    public bool AcceptsDrops { get; set; } = true;

    /// <summary>Identifiant d'action intégrée (Kind == Action).</summary>
    public string ActionId { get; set; } = "";

    /// <summary>
    /// Icône personnalisée : nom de fichier dans %AppData%\Xbar\icons\, chemin absolu, ou
    /// "mdi:&lt;nom&gt;" pour une icône de la bibliothèque intégrée (voir IconLibrary).
    /// </summary>
    public string? IconOverride { get; set; }

    /// <summary>
    /// Couleur "#RRGGBB" d'une icône de la bibliothèque, ou du glyphe d'une action. Absente,
    /// l'icône prend la couleur du texte de la barre et suit le thème.
    /// </summary>
    public string? IconColor { get; set; }

    [JsonIgnore]
    public bool IsSeparator => Kind == ShortcutKind.Separator;

    public ShortcutItem Clone() => (ShortcutItem)MemberwiseClone();
}

/// <summary>Racine de shortcuts.json (objet plutôt que tableau, pour rester extensible).</summary>
public sealed class ShortcutList
{
    /// <summary>Format du fichier ; voir ShortcutStore.Migrate.</summary>
    public int Version { get; set; } = 2;
    public List<ShortcutItem> Items { get; set; } = new();
}
