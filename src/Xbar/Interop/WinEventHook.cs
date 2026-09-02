using System.Runtime.InteropServices;
using Xbar.Core;

namespace Xbar.Interop;

/// <summary>
/// Abonnement aux evenements Windows (SetWinEventHook hors contexte : aucune DLL n'est injectee
/// dans explorer.exe). Permet de suivre creations, destructions, deplacements et changements
/// d'etat des fenetres sans polling.
/// </summary>
public sealed class WinEventHook : IDisposable
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    public const int OBJID_WINDOW = 0;
    public const int OBJID_CLIENT = unchecked((int)0xFFFFFFFC);
    public const int CHILDID_SELF = 0;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
        WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private readonly List<IntPtr> _hooks = new();
    private readonly WinEventProc _callback; // conserve pour eviter le ramasse-miettes

    /// <summary>(eventType, hwnd, objectId, childId). Appele sur le thread UI.</summary>
    public event Action<uint, IntPtr, int, int>? EventReceived;

    public WinEventHook() => _callback = OnWinEvent;

    public void Hook(uint min, uint max)
    {
        var h = SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (h == IntPtr.Zero) Log.Warn($"SetWinEventHook({min:X}-{max:X}) a echoue");
        else _hooks.Add(h);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        try { EventReceived?.Invoke(eventType, hwnd, objectId, childId); }
        catch (Exception ex) { Log.Error("Traitement d'un evenement Windows", ex); }
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
