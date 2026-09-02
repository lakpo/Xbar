using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using Xbar.Core;
using Xbar.Interop;

namespace Xbar.ExplorerIntegration;

/// <summary>
/// Double-clic dans une zone vide de la vue des fichiers = remonter au dossier parent.
///
/// Detection en trois etapes, du moins couteux au plus couteux :
///   1. reconstruction du double-clic depuis un hook souris bas niveau (hors processus) ;
///   2. verification de la chaine des classes de fenetres : la vue des fichiers est toujours
///      contenue dans "SHELLDLL_DefView", ce qui exclut d'emblee l'arbre de navigation,
///      la barre d'adresse, la recherche, la barre d'outils et le Bureau ;
///   3. UI Automation (ElementFromPoint) pour distinguer une zone vide d'un element de liste.
///
/// Le clic n'est jamais consomme : l'Explorateur continue de le traiter normalement.
/// </summary>
public sealed class EmptyAreaDoubleClick : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;

    private const int DragTolerance = 4;

    /// <summary>Delai maximal accorde a UI Automation pour repondre entre les deux clics.</summary>
    private const int ProbeTimeoutMs = 400;

    /// <summary>Ce qui se trouvait sous le curseur au premier clic.</summary>
    private readonly record struct ProbeResult(IntPtr Window, bool IsEmptyArea);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private readonly HookProc _callback; // conserve pour eviter le ramasse-miettes
    private readonly Dispatcher _dispatcher;
    private IntPtr _hook;

    private uint _lastDownTime;
    private POINT _lastDownPoint;
    private POINT _pressPoint;
    private bool _movedWhilePressed;
    private volatile bool _busy;

    private Task<ProbeResult?>? _probe;
    private POINT _probePoint;

    public EmptyAreaDoubleClick(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnMouseEvent;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _callback, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero) Log.Warn("Installation du hook souris impossible");
        else Log.Info("Double-clic dans le vide : actif");
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        Log.Info("Double-clic dans le vide : inactif");
    }

    private IntPtr OnMouseEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);

        try
        {
            var msg = (int)wParam;
            switch (msg)
            {
                case WM_MOUSEMOVE:
                    if (_pressPoint.X != 0 || _pressPoint.Y != 0)
                    {
                        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        if (Math.Abs(data.pt.X - _pressPoint.X) > DragTolerance ||
                            Math.Abs(data.pt.Y - _pressPoint.Y) > DragTolerance)
                            _movedWhilePressed = true;
                    }
                    break;

                case WM_LBUTTONUP:
                    _pressPoint = default;
                    break;

                case WM_LBUTTONDOWN:
                    HandleButtonDown(Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Hook souris : {ex.Message}");
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void HandleButtonDown(MSLLHOOKSTRUCT data)
    {
        var isDoubleClick =
            data.time - _lastDownTime <= GetDoubleClickTime() &&
            Math.Abs(data.pt.X - _lastDownPoint.X) <= DragTolerance &&
            Math.Abs(data.pt.Y - _lastDownPoint.Y) <= DragTolerance &&
            !_movedWhilePressed;

        _lastDownTime = data.time;
        _lastDownPoint = data.pt;
        _pressPoint = data.pt;
        _movedWhilePressed = false;

        if (!SettingsStore.Current.DoubleClickGoUp) return;

        if (!isDoubleClick)
        {
            // Premier clic : c'est le SEUL moment ou l'on peut savoir ce qui se trouve sous le
            // curseur. Au second clic l'Explorateur a deja ouvert le dossier, et la vue examinee
            // serait celle du dossier ouvert -- ce qui ferait remonter a tort.
            StartProbe(data.pt);
            return;
        }

        _lastDownTime = 0; // evite qu'un triple clic ne declenche deux remontees
        if (_busy) return;

        var probe = _probe;
        if (probe is null) return;

        // Le sondage doit correspondre au point reellement double-clique.
        if (Math.Abs(data.pt.X - _probePoint.X) > DragTolerance ||
            Math.Abs(data.pt.Y - _probePoint.Y) > DragTolerance) return;

        _probe = null;
        _busy = true;
        _ = ConsumeProbeAsync(probe);
    }

    /// <summary>
    /// Analyse (hors du hook, qui doit rester instantane) ce qui se trouve sous le curseur au
    /// premier clic : fenetre Explorer concernee, et zone vide ou non.
    /// </summary>
    private void StartProbe(POINT point)
    {
        _probePoint = point;
        _probe = Task.Run<ProbeResult?>(() =>
        {
            try
            {
                var hwnd = ResolveExplorerWindow(point);
                if (hwnd == IntPtr.Zero) return null;
                return new ProbeResult(hwnd, IsEmptyFileViewArea(point));
            }
            catch (Exception ex)
            {
                Log.Debug($"Sondage du point cliquable : {ex.Message}");
                return null;
            }
        });
    }

    private async Task ConsumeProbeAsync(Task<ProbeResult?> probe)
    {
        try
        {
            // Si UI Automation n'a pas repondu a temps, on prefere ne rien faire plutot que
            // de risquer une remontee non voulue.
            if (await Task.WhenAny(probe, Task.Delay(ProbeTimeoutMs)).ConfigureAwait(false) != probe)
            {
                Log.Debug("Sondage trop lent : double-clic ignore");
                return;
            }

            if (await probe.ConfigureAwait(false) is not { IsEmptyArea: true } result) return;
            await _dispatcher.InvokeAsync(() => ExplorerNavigator.GoUp(result.Window));
        }
        catch (Exception ex)
        {
            Log.Warn($"Detection du double-clic : {ex.Message}");
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Renvoie la fenetre Explorer concernee si le point est bien dans sa vue de fichiers
    /// (presence de SHELLDLL_DefView dans la chaine des parents), sinon IntPtr.Zero.
    /// </summary>
    private static IntPtr ResolveExplorerWindow(POINT point)
    {
        var hwnd = NativeMethods.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero) return IntPtr.Zero;

        // Le Bureau utilise aussi SHELLDLL_DefView : on exige une vraie fenetre de dossier.
        if (!ExplorerWindows.IsExplorerWindow(root)) return IntPtr.Zero;

        var inDefView = false;
        for (var current = hwnd; current != IntPtr.Zero && current != root;
             current = NativeMethods.GetParent(current))
        {
            if (NativeMethods.GetWindowClassName(current)
                .Equals("SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase))
            {
                inDefView = true;
                break;
            }
        }

        return inDefView ? root : IntPtr.Zero;
    }

    /// <summary>Vrai si le point n'est sur aucun element de la liste (donc dans le vide).</summary>
    private static bool IsEmptyFileViewArea(POINT point)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            if (element is null) return false;

            var walker = TreeWalker.RawViewWalker;
            var current = element;

            for (var depth = 0; depth < 6 && current is not null; depth++)
            {
                var type = current.Current.ControlType;

                if (type == ControlType.ListItem || type == ControlType.DataItem ||
                    type == ControlType.TreeItem || type == ControlType.Edit ||
                    type == ControlType.Button || type == ControlType.Hyperlink)
                    return false;

                // La vue elle-meme : le clic est donc dans une zone vide.
                if (type == ControlType.List || type == ControlType.DataGrid)
                    return true;

                current = walker.GetParent(current);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"UI Automation indisponible pour ce point : {ex.Message}");
        }
        return false;
    }

    public void Dispose() => Stop();
}
