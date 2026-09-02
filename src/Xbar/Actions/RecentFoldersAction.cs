using System.IO;
using System.Windows.Input;
using Xbar.Core;
using Xbar.ExplorerIntegration;
using Xbar.Localization;

namespace Xbar.Actions;

/// <summary>
/// Ouvre un menu listant les derniers dossiers parcourus, du plus recent au plus ancien.
///
/// Clic : navigue dans l'onglet courant. Molette : nouvel onglet. Ctrl+clic : nouvelle fenetre.
/// L'historique est alimente par FolderHistory, a partir des changements de titre des fenetres
/// de l'Explorateur.
/// </summary>
public sealed class RecentFoldersAction : XbarAction
{
    public const string ActionId = "history.recent-folders";

    public override string Id => ActionId;
    public override string Title => Strings.Get("Action_RecentFolders");
    public override string Glyph => ""; // Segoe Fluent Icons : History

    public override Task ExecuteAsync(ActionContext context)
    {
        if (context.ShowMenu is null)
        {
            context.Report(Strings.Get("Msg_MenuUnavailable"), false);
            return Task.CompletedTask;
        }

        var folders = FolderHistory.Folders;
        var entries = new List<ActionMenuEntry>();

        if (folders.Count == 0)
        {
            entries.Add(new ActionMenuEntry(Strings.Get("Action_NoRecentFolder"), null, () => { })
            {
                Enabled = false
            });
        }
        else
        {
            var currentFolder = context.CurrentFolder;

            foreach (var folder in folders)
            {
                var path = folder;
                var isCurrent = string.Equals(path, currentFolder?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);

                entries.Add(new ActionMenuEntry(
                    Header: BuildHeader(path, isCurrent),
                    ToolTip: path,
                    Invoke: () => Navigate(context.ExplorerWindow, path))
                {
                    // Un dossier supprime ou un lecteur debranche reste visible mais inactif.
                    Enabled = Directory.Exists(path)
                });
            }

            entries.Add(new ActionMenuEntry(Strings.Get("Action_ClearHistory"), null, FolderHistory.Clear)
            {
                SeparatorBefore = true
            });
        }

        context.ShowMenu(entries);
        return Task.CompletedTask;
    }

    /// <summary>Nom du dossier, suivi de son emplacement pour lever les homonymes.</summary>
    private static string BuildHeader(string path, bool isCurrent)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path; // racine de disque

        var parent = Path.GetDirectoryName(path);
        var header = string.IsNullOrEmpty(parent) ? name : $"{name}   —   {parent}";
        return isCurrent ? $"• {header}" : header;
    }

    private static void Navigate(IntPtr explorerWindow, string path)
    {
        if (!Directory.Exists(path))
        {
            Log.Warn($"Dossier de l'historique introuvable : {path}");
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ExplorerNavigator.OpenInNewWindow(path);
            return;
        }

        if (!ExplorerNavigator.NavigateSameTab(explorerWindow, path))
            ExplorerNavigator.OpenInNewWindow(path);
    }
}
