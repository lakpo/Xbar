using System.Diagnostics;
using System.IO;
using System.Text;
using Xbar.Actions;
using Xbar.Core;
using Xbar.ExplorerIntegration;
using Xbar.Localization;

namespace Xbar.Shortcuts;

public enum ActivationMode
{
    /// <summary>Clic gauche.</summary>
    Default,
    /// <summary>Clic molette.</summary>
    NewTab,
    /// <summary>Ctrl + clic.</summary>
    NewWindow
}

/// <summary>Execution d'un raccourci : application, dossier ou action integree.</summary>
public static class ShortcutLauncher
{
    public const string CurrentFolderToken = "{currentFolder}";

    public static async Task ActivateAsync(ShortcutItem item, IntPtr explorerHwnd,
        ActivationMode mode, IReadOnlyList<string>? droppedPaths = null,
        Action<string, bool>? feedback = null,
        Action<IReadOnlyList<ActionMenuEntry>>? showMenu = null)
    {
        var currentFolder = ExplorerNavigator.GetCurrentFolder(explorerHwnd);

        switch (item.Kind)
        {
            case ShortcutKind.Folder:
                await ActivateFolderAsync(item, explorerHwnd, mode, feedback);
                break;

            case ShortcutKind.Application:
                LaunchApplication(item, currentFolder, droppedPaths, feedback);
                break;

            case ShortcutKind.File:
                OpenFile(item, feedback);
                break;

            case ShortcutKind.Action:
                var action = ActionRegistry.Find(item.ActionId);
                if (action is null)
                {
                    Log.Warn($"Action inconnue : {item.ActionId}");
                    feedback?.Invoke(Strings.Get("Msg_UnknownAction"), false);
                    return;
                }
                await action.ExecuteAsync(new ActionContext(explorerHwnd, currentFolder)
                {
                    Feedback = feedback,
                    ShowMenu = showMenu
                });
                break;
        }
    }

    private static async Task ActivateFolderAsync(ShortcutItem item, IntPtr hwnd, ActivationMode mode,
        Action<string, bool>? feedback)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        if (!Directory.Exists(target))
        {
            Log.Warn($"Dossier introuvable : {target}");
            feedback?.Invoke(Strings.Get("Msg_FolderNotFound"), false);
            return;
        }

        switch (mode)
        {
            case ActivationMode.NewWindow:
                ExplorerNavigator.OpenInNewWindow(target);
                break;
            case ActivationMode.NewTab:
                await ExplorerNavigator.OpenInNewTabAsync(hwnd, target);
                break;
            default:
                if (!ExplorerNavigator.NavigateSameTab(hwnd, target))
                    ExplorerNavigator.OpenInNewWindow(target);
                break;
        }
    }

    private static void LaunchApplication(ShortcutItem item, string? currentFolder,
        IReadOnlyList<string>? droppedPaths, Action<string, bool>? feedback)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        if (string.IsNullOrWhiteSpace(target))
        {
            feedback?.Invoke(Strings.Get("Msg_NoPath"), false);
            return;
        }

        var arguments = BuildArguments(item, currentFolder, droppedPaths);
        var workingDirectory = ResolveWorkingDirectory(item, currentFolder, target);

        try
        {
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = arguments
            };
            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            Process.Start(psi);
            Log.Debug($"Lancement : {target} {arguments}");
        }
        catch (Exception ex)
        {
            Log.Error($"Lancement de {target}", ex);
            feedback?.Invoke(Strings.Get("Msg_LaunchFailed"), false);
        }
    }

    /// <summary>
    /// Ouvre un fichier avec l'application qui lui est associee, exactement comme un
    /// double-clic dans l'Explorateur : c'est le Shell qui choisit le verbe par defaut.
    /// </summary>
    private static void OpenFile(ShortcutItem item, Action<string, bool>? feedback)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        if (!File.Exists(target))
        {
            Log.Warn($"Fichier introuvable : {target}");
            feedback?.Invoke(Strings.Get("Msg_FileNotFound"), false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Log.Debug($"Ouverture : {target}");
        }
        catch (Exception ex)
        {
            Log.Error($"Ouverture de {target}", ex);
            feedback?.Invoke(Strings.Get("Msg_OpenFailed"), false);
        }
    }

    public static string BuildArguments(ShortcutItem item, string? currentFolder,
        IReadOnlyList<string>? droppedPaths)
    {
        var sb = new StringBuilder();
        var template = item.Arguments ?? "";

        if (template.Contains(CurrentFolderToken, StringComparison.OrdinalIgnoreCase))
            template = ReplaceToken(template, CurrentFolderToken, currentFolder ?? "");

        sb.Append(Environment.ExpandEnvironmentVariables(template));

        if (item.AcceptsDrops && droppedPaths is { Count: > 0 })
            foreach (var p in droppedPaths)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"').Append(p.TrimEnd('\\')).Append('"');
            }

        return sb.ToString().Trim();
    }

    private static string ReplaceToken(string input, string token, string value)
    {
        var idx = input.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            input = input[..idx] + value + input[(idx + token.Length)..];
            idx = input.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        }
        return input;
    }

    /// <summary>Un fichier que l'on lance, par opposition a un document que l'on ouvre.</summary>
    private static bool IsExecutable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".com" or ".bat" or ".cmd";

    private static string? ResolveWorkingDirectory(ShortcutItem item, string? currentFolder, string target) =>
        item.WorkingDirectory switch
        {
            WorkingDirectoryMode.CurrentExplorerFolder => currentFolder,
            WorkingDirectoryMode.Custom => Environment.ExpandEnvironmentVariables(item.CustomWorkingDirectory),
            _ => Path.GetDirectoryName(target)
        };

    /// <summary>
    /// Cree un raccourci a partir d'un element depose sur la barre : dossier, executable,
    /// raccourci .lnk (resolu vers sa cible), ou n'importe quel fichier — celui-ci s'ouvrira
    /// avec l'application qui lui est associee.
    /// </summary>
    public static ShortcutItem? CreateFromDroppedPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return new ShortcutItem
                {
                    Kind = ShortcutKind.Folder,
                    Name = new DirectoryInfo(path.TrimEnd('\\')).Name,
                    Target = path
                };

            if (!File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".lnk")
            {
                var link = ShellLinkResolver.Resolve(path);
                if (link is null) return null;

                if (Directory.Exists(link.Path))
                    return new ShortcutItem
                    {
                        Kind = ShortcutKind.Folder,
                        Name = Path.GetFileNameWithoutExtension(path),
                        Target = link.Path
                    };

                if (!File.Exists(link.Path)) return null;

                if (!IsExecutable(link.Path))
                    return new ShortcutItem
                    {
                        Kind = ShortcutKind.File,
                        Name = Path.GetFileNameWithoutExtension(path),
                        Target = link.Path
                    };

                return new ShortcutItem
                {
                    Kind = ShortcutKind.Application,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Target = link.Path,
                    Arguments = link.Arguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(link.WorkingDirectory)
                        ? WorkingDirectoryMode.Default
                        : WorkingDirectoryMode.Custom,
                    CustomWorkingDirectory = link.WorkingDirectory
                };
            }

            if (IsExecutable(path))
                return new ShortcutItem
                {
                    Kind = ShortcutKind.Application,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Target = path
                };

            return new ShortcutItem
            {
                Kind = ShortcutKind.File,
                Name = Path.GetFileNameWithoutExtension(path),
                Target = path
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Creation d'un raccourci depuis {path}", ex);
            return null;
        }
    }
}
