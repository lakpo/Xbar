using System.Runtime.InteropServices;
using System.Windows.Automation;
using Xbar.Interop;

namespace Xbar.ExplorerIntegration;

/// <summary>Identification et enumeration des fenetres de l'Explorateur Windows.</summary>
public static class ExplorerWindows
{
    /// <summary>Fenetres de dossier. "CabinetWClass" = Explorateur moderne, "ExploreWClass" = ancien mode.</summary>
    private static readonly string[] FolderClasses = ["CabinetWClass", "ExploreWClass"];

    /// <summary>Classes du Bureau : jamais gerees par Xbar.</summary>
    private static readonly string[] DesktopClasses = ["Progman", "WorkerW", "SHELLDLL_DefView"];

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    public static bool IsExplorerWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;
        var cls = NativeMethods.GetWindowClassName(hwnd);
        return Array.IndexOf(FolderClasses, cls) >= 0;
    }

    public static bool IsDesktopWindow(IntPtr hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var cls = NativeMethods.GetWindowClassName(root == IntPtr.Zero ? hwnd : root);
        return Array.IndexOf(DesktopClasses, cls) >= 0;
    }

    /// <summary>
    /// La vue des fichiers affiche-t-elle une barre de defilement verticale ?
    ///
    /// La vue moderne dessine sa barre en DirectUI : ni WS_VSCROLL ni GetScrollBarInfo ne la
    /// voient (meme un dossier de plusieurs milliers de fichiers les renvoie « invisible »).
    /// Seule UI Automation repond correctement, via le ScrollPattern de la liste. L'appel coute
    /// environ 45 ms : il est fait hors du thread UI et son resultat est mis en cache.
    /// </summary>
    public static bool ViewHasVerticalScrollbar(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null) return true;

            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid));

            var view = root.FindFirst(TreeScope.Descendants, condition);
            if (view is null) return true;

            return view.GetCurrentPattern(ScrollPattern.Pattern) is not ScrollPattern scroll
                || scroll.Current.VerticallyScrollable;
        }
        catch (Exception ex)
        {
            // Dans le doute on suppose qu'une barre est presente : mieux vaut un petit
            // decalage inutile que de recouvrir la barre de defilement.
            Core.Log.Debug($"Detection de la barre de defilement : {ex.Message}");
            return true;
        }
    }

    public static List<IntPtr> Enumerate()
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsExplorerWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd))
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
