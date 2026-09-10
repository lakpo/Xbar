namespace Xbar.Actions;

/// <summary>Une entree du menu qu'une action peut ouvrir depuis son bouton.</summary>
public sealed record ActionMenuEntry(string Header, string? ToolTip, Action Invoke)
{
    /// <summary>Entree grisee (cible absente, liste vide...).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Trait de separation affiche avant cette entree.</summary>
    public bool SeparatorBefore { get; init; }
}

/// <summary>Contexte d'execution d'une action integree.</summary>
public sealed record ActionContext(IntPtr ExplorerWindow, string? CurrentFolder)
{
    /// <summary>Retour visuel discret (message, succes). Fourni par la barre appelante.</summary>
    public Action<string, bool>? Feedback { get; init; }

    /// <summary>
    /// Ouvre un menu au niveau du bouton. Fourni par la barre : une action n'a pas a connaitre
    /// la fenetre qui l'affiche.
    /// </summary>
    public Action<IReadOnlyList<ActionMenuEntry>>? ShowMenu { get; init; }

    public void Report(string message, bool success = true) => Feedback?.Invoke(message, success);
}

/// <summary>Action integree a Xbar. Le systeme est volontairement minimal mais extensible.</summary>
public abstract class XbarAction
{
    public abstract string Id { get; }
    public abstract string Title { get; }

    /// <summary>Glyphe Segoe Fluent Icons utilise comme icone par defaut.</summary>
    public abstract string Glyph { get; }

    public abstract Task ExecuteAsync(ActionContext context);
}

/// <summary>Catalogue des actions disponibles.</summary>
public static class ActionRegistry
{
    private static readonly List<XbarAction> Actions =
        [new PasteClipboardImageAction(), new RecentFoldersAction(), new DuplicateTabAction()];

    public static IReadOnlyList<XbarAction> All => Actions;

    public static XbarAction? Find(string id) =>
        Actions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
}
