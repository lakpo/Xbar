using Xbar.Core;
using Xbar.ExplorerIntegration;
using Xbar.Shortcuts;

namespace Xbar.Overlay;

/// <summary>
/// Cree et maintient une barre par fenetre de l'Explorateur, en s'appuyant sur ExplorerTracker.
/// Toutes les barres partagent la meme configuration et la meme liste de raccourcis.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly ExplorerTracker _tracker = new();
    private readonly Dictionary<IntPtr, OverlayWindow> _overlays = new();

    public event Action? SettingsRequested;
    public event Action? ReloadRequested;
    public event Action? LogsRequested;
    public event Action? QuitRequested;
    public event Action? AutoHideToggled;

    public void Start()
    {
        _tracker.WindowOpened += OnWindowOpened;
        _tracker.WindowClosed += OnWindowClosed;
        _tracker.WindowBoundsChanged += OnBoundsChanged;
        _tracker.WindowStateChanged += OnBoundsChanged;
        _tracker.Reconciled += RefreshPlacement;
        _tracker.WindowFolderChanged += OnFolderChanged;

        ShortcutStore.Changed += RebuildAll;
        SettingsStore.Changed += _ => RebuildAll();

        _tracker.Start();
    }

    private void OnWindowOpened(IntPtr hwnd)
    {
        if (_overlays.ContainsKey(hwnd)) return;
        try
        {
            var overlay = new OverlayWindow(hwnd);
            overlay.SettingsRequested += () => SettingsRequested?.Invoke();
            overlay.ReloadRequested += () => ReloadRequested?.Invoke();
            overlay.LogsRequested += () => LogsRequested?.Invoke();
            overlay.QuitRequested += () => QuitRequested?.Invoke();
            overlay.AutoHideToggled += () => AutoHideToggled?.Invoke();

            _overlays[hwnd] = overlay;
            overlay.Show();
            overlay.ApplyAutoHide();
            // Apres Show() : WPF a pu repositionner la fenetre, on refixe la geometrie.
            overlay.UpdatePlacement();
        }
        catch (Exception ex)
        {
            Log.Error($"Creation de la barre pour la fenetre {hwnd:X}", ex);
        }
    }

    private void OnWindowClosed(IntPtr hwnd)
    {
        if (!_overlays.Remove(hwnd, out var overlay)) return;
        try { overlay.Close(); }
        catch (Exception ex) { Log.Warn($"Fermeture d'une barre : {ex.Message}"); }
    }

    private void OnBoundsChanged(IntPtr hwnd)
    {
        if (_overlays.TryGetValue(hwnd, out var overlay)) overlay.UpdatePlacement();
    }

    /// <summary>
    /// Une fenetre Explorer a change de dossier (ou d'onglet actif) : on note le dossier
    /// affiche dans l'historique. Les dossiers virtuels (Ce PC, Reseau...) renvoient null
    /// et ne sont donc pas enregistres.
    /// </summary>
    private void OnFolderChanged(IntPtr hwnd)
    {
        try { FolderHistory.Record(ExplorerNavigator.GetCurrentFolder(hwnd)); }
        catch (Exception ex) { Log.Debug($"Historique des dossiers : {ex.Message}"); }

        // Le nouveau dossier n'a pas forcement le meme besoin de defilement que le precedent.
        if (_overlays.TryGetValue(hwnd, out var overlay)) overlay.OnExplorerFolderChanged();
    }

    /// <summary>
    /// Reverifie la geometrie de toutes les barres. Sans effet quand rien n'a change : la barre
    /// ne repositionne sa fenetre que si ses coordonnees different des precedentes.
    /// </summary>
    private void RefreshPlacement()
    {
        foreach (var overlay in _overlays.Values) overlay.UpdatePlacement();
    }

    /// <summary>Reconstruit toutes les barres (changement de configuration ou de raccourcis).</summary>
    public void RebuildAll()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Rebuild();
            overlay.ApplyAutoHide();
        }
    }

    /// <summary>Affiche ou masque toutes les barres.</summary>
    public void SetVisible(bool visible)
    {
        SettingsStore.Update(s => s.BarEnabled = visible);
        foreach (var overlay in _overlays.Values) overlay.UpdatePlacement();
    }

    public void Dispose()
    {
        ShortcutStore.Changed -= RebuildAll;
        _tracker.Dispose();
        foreach (var overlay in _overlays.Values)
        {
            try { overlay.Close(); } catch { }
        }
        _overlays.Clear();
    }
}
