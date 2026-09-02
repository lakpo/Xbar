using System.Windows.Threading;
using Xbar.Core;
using Xbar.Interop;

namespace Xbar.ExplorerIntegration;

/// <summary>
/// Suit en continu les fenetres de l'Explorateur : ouverture, fermeture, deplacement,
/// redimensionnement, minimisation, changement de premier plan.
///
/// Le suivi repose sur SetWinEventHook (evenements pousses par Windows). Un rapprochement
/// periodique tres leger (toutes les 5 s, une simple EnumWindows) sert uniquement de filet de
/// securite, notamment quand explorer.exe redemarre.
/// </summary>
public sealed class ExplorerTracker : IDisposable
{
    private readonly WinEventHook _hook = new();
    private readonly DispatcherTimer _reconcile;
    private readonly HashSet<IntPtr> _windows = new();

    public event Action<IntPtr>? WindowOpened;
    public event Action<IntPtr>? WindowClosed;
    public event Action<IntPtr>? WindowBoundsChanged;
    public event Action<IntPtr>? WindowStateChanged;
    public event Action<IntPtr>? ForegroundChanged;

    /// <summary>
    /// Emis a chaque rapprochement periodique. Permet de reverifier le placement des barres :
    /// une fenetre qui n'a jamais bouge n'emet aucun evenement, et une barre dont le placement
    /// initial a echoue resterait sinon invisible indefiniment.
    /// </summary>
    public event Action? Reconciled;

    /// <summary>
    /// Le titre d'une fenetre Explorer a change. L'Explorateur renomme sa fenetre a chaque
    /// changement de dossier ou d'onglet actif : c'est notre signal de navigation.
    /// </summary>
    public event Action<IntPtr>? WindowFolderChanged;

    public IReadOnlyCollection<IntPtr> Windows => _windows;

    public ExplorerTracker()
    {
        _hook.EventReceived += OnWinEvent;
        _reconcile = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _reconcile.Tick += (_, _) => Reconcile();
    }

    public void Start()
    {
        _hook.Hook(WinEventHook.EVENT_SYSTEM_FOREGROUND, WinEventHook.EVENT_SYSTEM_FOREGROUND);
        _hook.Hook(WinEventHook.EVENT_SYSTEM_MOVESIZESTART, WinEventHook.EVENT_SYSTEM_MOVESIZEEND);
        _hook.Hook(WinEventHook.EVENT_SYSTEM_MINIMIZESTART, WinEventHook.EVENT_SYSTEM_MINIMIZEEND);
        _hook.Hook(WinEventHook.EVENT_OBJECT_CREATE, WinEventHook.EVENT_OBJECT_HIDE);
        _hook.Hook(WinEventHook.EVENT_OBJECT_LOCATIONCHANGE, WinEventHook.EVENT_OBJECT_LOCATIONCHANGE);
        _hook.Hook(WinEventHook.EVENT_OBJECT_NAMECHANGE, WinEventHook.EVENT_OBJECT_NAMECHANGE);

        Reconcile(); // fenetres deja ouvertes au demarrage
        _reconcile.Start();
        Log.Info($"Suivi de l'Explorateur demarre ({_windows.Count} fenetre(s) detectee(s))");
    }

    private void OnWinEvent(uint eventType, IntPtr hwnd, int objectId, int childId)
    {
        if (objectId != WinEventHook.OBJID_WINDOW || childId != WinEventHook.CHILDID_SELF) return;

        switch (eventType)
        {
            case WinEventHook.EVENT_OBJECT_CREATE:
            case WinEventHook.EVENT_OBJECT_SHOW:
                if (ExplorerWindows.IsExplorerWindow(hwnd)) Add(hwnd);
                break;

            case WinEventHook.EVENT_OBJECT_DESTROY:
            case WinEventHook.EVENT_OBJECT_HIDE:
                if (_windows.Contains(hwnd) && (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)))
                    Remove(hwnd);
                break;

            case WinEventHook.EVENT_OBJECT_LOCATIONCHANGE:
            case WinEventHook.EVENT_SYSTEM_MOVESIZEEND:
                if (_windows.Contains(hwnd)) WindowBoundsChanged?.Invoke(hwnd);
                break;

            case WinEventHook.EVENT_SYSTEM_MINIMIZESTART:
            case WinEventHook.EVENT_SYSTEM_MINIMIZEEND:
                if (_windows.Contains(hwnd)) WindowStateChanged?.Invoke(hwnd);
                break;

            case WinEventHook.EVENT_OBJECT_NAMECHANGE:
                if (_windows.Contains(hwnd)) WindowFolderChanged?.Invoke(hwnd);
                break;

            case WinEventHook.EVENT_SYSTEM_FOREGROUND:
                ForegroundChanged?.Invoke(hwnd);
                break;
        }
    }

    /// <summary>Rapprochement entre les fenetres connues et la realite (filet de securite).</summary>
    private void Reconcile()
    {
        var live = ExplorerWindows.Enumerate();

        foreach (var hwnd in live)
            if (!_windows.Contains(hwnd)) Add(hwnd);

        foreach (var hwnd in _windows.ToArray())
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) Remove(hwnd);

        Reconciled?.Invoke();
    }

    private void Add(IntPtr hwnd)
    {
        if (!_windows.Add(hwnd)) return;
        Log.Debug($"Fenetre Explorer detectee : {hwnd:X}");
        WindowOpened?.Invoke(hwnd);
        WindowFolderChanged?.Invoke(hwnd);
    }

    private void Remove(IntPtr hwnd)
    {
        if (!_windows.Remove(hwnd)) return;
        Log.Debug($"Fenetre Explorer fermee : {hwnd:X}");
        WindowClosed?.Invoke(hwnd);
    }

    public void Dispose()
    {
        _reconcile.Stop();
        _hook.Dispose();
        _windows.Clear();
    }
}
