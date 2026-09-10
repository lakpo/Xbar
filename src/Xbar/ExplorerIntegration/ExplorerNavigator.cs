using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xbar.Core;
using Xbar.Interop;
using Xbar.Shortcuts;

namespace Xbar.ExplorerIntegration;

/// <summary>
/// Dialogue avec l'Explorateur via ses objets COM publics.
///
/// Point cle sous Windows 11 : tous les onglets d'une fenetre partagent le meme HWND, chacun a
/// son propre IShellBrowser, et l'enumeration ShellWindows n'indique pas lequel est affiche.
/// L'onglet actif est identifie par l'ordre Z de son conteneur ShellTabWindowClass (voir
/// GetTabRank) ; le dossier et la navigation passent ensuite par IShellBrowser, ce qui evite
/// toute simulation de frappe -- sauf pour la creation d'un onglet, que Windows n'expose pas.
/// </summary>
public static class ExplorerNavigator
{
    private static object? _shell;

    private static dynamic? Shell
    {
        get
        {
            if (_shell is not null) return _shell;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) return null;
                _shell = Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Log.Warn($"Shell.Application indisponible : {ex.Message}");
                _shell = null;
            }
            return _shell;
        }
    }

    /// <summary>
    /// IShellBrowser de l'onglet ACTIF de la fenetre indiquee. A liberer par l'appelant.
    ///
    /// Chaque onglet possede son propre IShellBrowser, mais rien dans l'enumeration ShellWindows
    /// n'indique lequel est affiche : leurs vues sont toutes marquees visibles et ont la meme
    /// geometrie. Le seul discriminant fiable observe est l'ordre Z : le conteneur
    /// ShellTabWindowClass de l'onglet actif est le premier de sa fratrie.
    /// </summary>
    private static IShellBrowser? GetActiveBrowser(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        IShellBrowser? best = null;
        var bestRank = int.MaxValue;

        try
        {
            dynamic? shell = Shell;
            if (shell is null) return null;

            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                object? entry = null;
                try { entry = windows.Item(i); } catch { }
                if (entry is null) continue;

                try
                {
                    dynamic w = entry;
                    if (new IntPtr((long)w.HWND) != hwnd) continue;
                }
                catch { continue; }

                if (entry is not IServiceProviderCom sp) continue;
                var sid = ShellGuids.SID_STopLevelBrowser;
                var iid = ShellGuids.IID_IShellBrowser;
                if (sp.QueryService(ref sid, ref iid, out var obj) != 0 || obj is not IShellBrowser sb)
                    continue;

                var rank = GetTabRank(GetViewWindow(sb));
                if (rank < bestRank)
                {
                    Release(best);
                    best = sb;
                    bestRank = rank;
                }
                else Release(sb);

                if (bestRank == 0) break; // onglet actif trouve
            }
        }
        catch (COMException ex)
        {
            Log.Warn($"Acces au navigateur de la fenetre {hwnd:X} : {ex.Message}");
            _shell = null; // explorer.exe a peut-etre redemarre
        }
        catch (Exception ex)
        {
            Log.Warn($"Acces au navigateur de la fenetre {hwnd:X} : {ex.Message}");
        }

        return best;
    }

    /// <summary>HWND de la vue (SHELLDLL_DefView) rattachee a un navigateur d'onglet.</summary>
    private static IntPtr GetViewWindow(IShellBrowser browser)
    {
        object? viewObj = null;
        try
        {
            if (browser.QueryActiveShellView(out viewObj) != 0 || viewObj is not IShellView view)
                return IntPtr.Zero;
            return view.GetWindow(out var h) == 0 ? h : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
        finally { Release(viewObj); }
    }

    /// <summary>
    /// Rang de l'onglet dans l'ordre Z de sa fratrie : 0 = onglet actif.
    /// Renvoie 0 egalement quand la fenetre n'a pas d'onglets (Explorateur sans ShellTabWindowClass).
    /// </summary>
    private static int GetTabRank(IntPtr viewWindow)
    {
        if (viewWindow == IntPtr.Zero) return int.MaxValue;

        var current = viewWindow;
        for (var depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
        {
            if (NativeMethods.GetWindowClassName(current)
                .Equals("ShellTabWindowClass", StringComparison.OrdinalIgnoreCase))
            {
                var parent = NativeMethods.GetParent(current);
                if (parent == IntPtr.Zero) return 0;

                var rank = 0;
                for (var sibling = NativeMethods.GetWindow(parent, NativeMethods.GW_CHILD);
                     sibling != IntPtr.Zero;
                     sibling = NativeMethods.GetWindow(sibling, NativeMethods.GW_HWNDNEXT))
                {
                    if (!NativeMethods.GetWindowClassName(sibling)
                            .Equals("ShellTabWindowClass", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (sibling == current) return rank;
                    rank++;
                }
                return int.MaxValue;
            }
            current = NativeMethods.GetParent(current);
        }

        return 0; // pas d'onglets : la vue est forcement celle affichee
    }

    /// <summary>Chemin du dossier affiche par l'onglet ACTIF, ou null (dossier virtuel, Ce PC...).</summary>
    public static string? GetCurrentFolder(IntPtr hwnd)
    {
        var browser = GetActiveBrowser(hwnd);
        if (browser is null) return null;
        try { return GetActiveFolder(browser); }
        finally { Release(browser); }
    }

    private static string? GetActiveFolder(IShellBrowser browser)
    {
        object? viewObj = null;
        object? folderObj = null;
        try
        {
            if (browser.QueryActiveShellView(out viewObj) != 0 || viewObj is not IFolderView view)
                return null;

            var iid = typeof(IPersistFolder2).GUID;
            if (view.GetFolder(ref iid, out folderObj) != 0 || folderObj is not IPersistFolder2 persist)
                return null;

            if (persist.GetCurFolder(out var pidl) != 0 || pidl == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                if (!ShellNative.SHGetPathFromIDList(pidl, sb)) return null;
                var path = sb.ToString();
                return Directory.Exists(path) ? path : null;
            }
            finally { ShellNative.ILFree(pidl); }
        }
        catch (Exception ex)
        {
            Log.Warn($"Lecture du dossier de l'onglet actif : {ex.Message}");
            return null;
        }
        finally
        {
            Release(folderObj);
            Release(viewObj);
        }
    }

    /// <summary>Navigation de l'onglet ACTIF vers un dossier, via IShellBrowser::BrowseObject.</summary>
    public static bool NavigateSameTab(IntPtr hwnd, string path)
    {
        var browser = GetActiveBrowser(hwnd);
        if (browser is null) return false;
        try { return Browse(browser, path); }
        finally { Release(browser); }
    }

    private static bool Browse(IShellBrowser browser, string path)
    {
        var hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        if (hr != 0 || pidl == IntPtr.Zero)
        {
            Log.Warn($"Chemin non reconnu par le Shell : {path}");
            return false;
        }
        try
        {
            hr = browser.BrowseObject(pidl, SBSP.SBSP_SAMEBROWSER | SBSP.SBSP_ABSOLUTE);
            if (hr != 0) Log.Warn($"BrowseObject a echoue pour {path} (hr=0x{hr:X8})");
            return hr == 0;
        }
        finally { ShellNative.ILFree(pidl); }
    }

    /// <summary>
    /// Depose des fichiers dans un dossier, comme un glisser-deposer de l'Explorateur.
    ///
    /// On passe par Folder.MoveHere / CopyHere de Shell.Application : c'est le Shell lui-meme
    /// qui effectue l'operation, avec sa boite de progression, sa gestion des conflits de noms
    /// et son annulation (Ctrl+Z). L'appel est asynchrone : il rend la main immediatement.
    /// </summary>
    public static bool DropInto(string destination, IReadOnlyList<string> sources, bool copy)
    {
        if (!Directory.Exists(destination) || sources.Count == 0) return false;

        try
        {
            dynamic? shell = Shell;
            if (shell is null) return false;

            dynamic folder = shell.NameSpace(destination);
            if (folder is null)
            {
                Log.Warn($"Dossier de destination inaccessible : {destination}");
                return false;
            }

            var done = 0;
            foreach (var source in sources)
            {
                // Deposer un element sur lui-meme ou dans son propre dossier n'a aucun sens.
                var parent = Path.GetDirectoryName(source.TrimEnd(Path.DirectorySeparatorChar));
                if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), destination.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!copy && string.Equals(parent, destination.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase)) continue;

                if (copy) folder.CopyHere(source, 0);
                else folder.MoveHere(source, 0);
                done++;
            }

            if (done > 0)
                Log.Info($"{(copy ? "Copie" : "Deplacement")} de {done} element(s) vers {destination}");
            return done > 0;
        }
        catch (Exception ex)
        {
            Log.Error($"Depot de fichiers vers {destination}", ex);
            return false;
        }
    }

    /// <summary>Ouvre le dossier dans une nouvelle fenetre de l'Explorateur.</summary>
    public static bool OpenInNewWindow(string path)
    {
        try
        {
            // Une adresse Shell (shell:MyComputerFolder) n'est pas un chemin : la mettre entre
            // guillemets ou lui retirer une barre finale la rendrait illisible pour explorer.exe.
            var argument = ShellLocations.IsShellAddress(path)
                ? path
                : $"\"{path.TrimEnd('\\')}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", argument)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Ouverture d'une nouvelle fenetre pour {path}", ex);
            return false;
        }
    }

    /// <summary>
    /// Ouvre le dossier dans un nouvel onglet de la fenetre indiquee.
    /// Windows n'expose aucune API publique pour les onglets de l'Explorateur : l'onglet est
    /// cree avec Ctrl+T (seul mecanisme disponible), puis la navigation se fait par BrowseObject,
    /// qui s'applique a l'onglet nouvellement actif.
    ///
    /// La creation est constatee en comptant les fenetres ShellTabWindowClass, et non en
    /// surveillant le dossier affiche : un onglet neuf s'ouvre sur un dossier virtuel (Accueil,
    /// Ce PC) dont le chemin vaut null, indiscernable d'un depart lui aussi virtuel -- la barre
    /// ouvrait alors une fenetre EN PLUS de l'onglet deja cree.
    ///
    /// Repli sur une nouvelle fenetre uniquement si aucun onglet n'a ete cree.
    /// </summary>
    public static async Task<bool> OpenInNewTabAsync(IntPtr hwnd, string path)
    {
        var before = GetTabWindows(hwnd);
        var tabCreated = false;

        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
            await Task.Delay(80);
            if (NativeMethods.GetForegroundWindow() != hwnd)
            {
                Log.Debug("Nouvel onglet : la fenetre Explorer n'a pas pu passer au premier plan");
                return OpenInNewWindow(path);
            }

            NativeMethods.SendCtrlKey(NativeMethods.VK_T);

            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(50);

                var now = GetTabWindows(hwnd);
                if (now.Count <= before.Count) continue;
                tabCreated = true;

                // On attend que le nouvel onglet soit aussi celui affiche (premier dans l'ordre
                // Z) : naviguer trop tot enverrait l'ancien onglet sur le dossier demande.
                if (before.Contains(now[0])) continue;

                return NavigateSameTab(hwnd, path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture d'un nouvel onglet", ex);
        }

        // Un onglet a bien ete cree : ouvrir en plus une fenetre donnerait deux ouvertures pour
        // un seul clic. Ctrl+T rend toujours le nouvel onglet actif du point de vue de
        // l'Explorateur ; si l'ordre Z ne l'a pas encore reflete, c'est notre indicateur qui
        // est en retard, pas l'onglet. On tente donc la navigation plutot que de renoncer.
        if (tabCreated || GetTabWindows(hwnd).Count > before.Count)
        {
            Log.Warn("Nouvel onglet non confirme par l'ordre Z : navigation tentee malgre tout");
            return NavigateSameTab(hwnd, path);
        }

        Log.Debug("Nouvel onglet non detecte, ouverture d'une nouvelle fenetre");
        return OpenInNewWindow(path);
    }

    /// <summary>
    /// HWND des onglets de la fenetre, dans l'ordre Z : le premier est l'onglet affiche.
    /// Liste vide sur un Explorateur sans onglets.
    /// </summary>
    private static List<IntPtr> GetTabWindows(IntPtr hwnd)
    {
        var tabs = new List<IntPtr>();
        if (hwnd == IntPtr.Zero) return tabs;

        for (var child = NativeMethods.GetWindow(hwnd, NativeMethods.GW_CHILD);
             child != IntPtr.Zero;
             child = NativeMethods.GetWindow(child, NativeMethods.GW_HWNDNEXT))
        {
            if (NativeMethods.GetWindowClassName(child)
                    .Equals("ShellTabWindowClass", StringComparison.OrdinalIgnoreCase))
                tabs.Add(child);
        }

        return tabs;
    }

    /// <summary>
    /// Remonte au dossier parent dans l'onglet actif.
    /// Ne fait rien a la racine d'un disque, conformement au comportement attendu.
    /// </summary>
    public static bool GoUp(IntPtr hwnd)
    {
        var browser = GetActiveBrowser(hwnd);
        if (browser is null) return false;
        try
        {
            var current = GetActiveFolder(browser);
            if (string.IsNullOrEmpty(current)) return false;

            var trimmed = current.TrimEnd(Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(trimmed);
            if (string.IsNullOrEmpty(root)) return false;
            if (string.Equals(trimmed, root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return false; // deja a la racine du disque

            var hr = browser.BrowseObject(IntPtr.Zero, SBSP.SBSP_PARENT | SBSP.SBSP_SAMEBROWSER);
            if (hr != 0)
            {
                Log.Warn($"Remontee au parent impossible depuis {current} (hr=0x{hr:X8})");
                return false;
            }

            Log.Debug($"Double-clic dans le vide : {current} -> parent");
            return true;
        }
        finally { Release(browser); }
    }

    private static void Release(object? comObject)
    {
        if (comObject is null) return;
        try { if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject); } catch { }
    }
}
