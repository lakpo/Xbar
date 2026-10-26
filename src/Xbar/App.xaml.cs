using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Xbar.Core;
using Xbar.ExplorerIntegration;
using Xbar.Localization;
using Xbar.Overlay;
using Xbar.Shortcuts;
using Xbar.Tray;
using Xbar.Update;
using System.Linq;

namespace Xbar;

public partial class App : Application
{
    private const string CommandShowSettings = "show-settings";

    private SingleInstance? _instance;
    private OverlayManager? _overlays;
    private TrayIcon? _tray;
    private EmptyAreaDoubleClick? _doubleClick;
    private DispatcherTimer? _updateTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Relance apres une mise a jour : l'ancienne instance est en train de se fermer, on
        // attend qu'elle ait rendu la main plutot que de lui renvoyer une commande.
        var afterUpdate = e.Args.Any(
            a => string.Equals(a, Updater.UpdatedSwitch, StringComparison.OrdinalIgnoreCase));
        _instance = afterUpdate ? WaitForPreviousInstance() : new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            // Une instance existe deja. Relancer Xbar a la main veut dire "montre-toi" ; un
            // lancement automatique au demarrage, lui, ne doit rien afficher.
            var launchedByWindows = e.Args.Any(
                a => string.Equals(a, StartupManager.AutostartSwitch, StringComparison.OrdinalIgnoreCase));
            if (!launchedByWindows && !afterUpdate) SingleInstance.SendCommand(CommandShowSettings);
            Shutdown();
            return;
        }

        var firstRun = !File.Exists(AppPaths.SettingsFile);

        Log.Initialize();
        DispatcherUnhandledException += OnUnhandledException;

        SettingsStore.Load();
        // La langue doit etre fixee avant toute construction de fenetre : le XAML resout ses
        // textes a l'analyse.
        Strings.Apply(SettingsStore.Current.Language);
        ShortcutStore.Load();
        FolderHistory.Load();
        ThemeManager.Apply();

        _instance.CommandReceived += OnCommandReceived;
        _instance.StartServer();

        _tray = new TrayIcon();
        _tray.ToggleBarRequested += ToggleBar;
        _tray.SettingsRequested += UI.SettingsWindow.ShowSingleton;
        _tray.QuitRequested += Quit;
        _tray.SystemThemeChanged += OnSystemThemeChanged;
        _tray.IsBarVisible = () => SettingsStore.Current.BarEnabled;

        _overlays = new OverlayManager();
        _overlays.SettingsRequested += UI.SettingsWindow.ShowSingleton;
        _overlays.ReloadRequested += Reload;
        _overlays.LogsRequested += OpenLogs;
        _overlays.QuitRequested += Quit;
        _overlays.AutoHideToggled += ToggleAutoHide;
        _overlays.Start();

        _doubleClick = new EmptyAreaDoubleClick(Dispatcher);
        _doubleClick.Start();

        if (firstRun)
        {
            StartupManager.ApplyFirstRunDefault();
            Log.Info("Premier lancement : configuration par defaut creee");
        }

        UpdateService.Notification += message =>
            Dispatcher.BeginInvoke(() => _tray?.ShowBalloon("Xbar", message));

        if (afterUpdate)
        {
            _tray.ShowBalloon("Xbar", Strings.Format("Msg_Updated", AppInfo.Version));
            Log.Info($"Xbar mis a jour en version {AppInfo.Version}");
        }
        _ = Updater.CleanupAsync();
        _ = CheckForUpdatesAsync();

        Log.Info("Xbar est pret");
    }

    private void OnCommandReceived(string command)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Log.Info($"Commande recue d'une autre instance : {command}");
            if (command == CommandShowSettings) UI.SettingsWindow.ShowSingleton();
        });
    }

    private void OnSystemThemeChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ThemeManager.Apply();
            _overlays?.RebuildAll();
        });
    }

    private void ToggleBar()
    {
        var visible = !SettingsStore.Current.BarEnabled;
        _overlays?.SetVisible(visible);
        Log.Info($"Barre {(visible ? "affichee" : "masquee")}");
    }

    private void ToggleAutoHide()
    {
        SettingsStore.Update(s => s.AutoHide = !s.AutoHide);
        _overlays?.RebuildAll();
    }

    private void Reload()
    {
        Log.Info("Rechargement de Xbar");
        SettingsStore.Load();
        Strings.Apply(SettingsStore.Current.Language);
        ShortcutStore.Load();
        FolderHistory.Load();
        Icons.IconService.ClearCache();
        ThemeManager.Apply();
        _overlays?.RebuildAll();
    }

    private static void OpenLogs()
    {
        try
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Error("Ouverture du dossier des logs", ex); }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            await UpdateService.RunAsync(manual: false);
        }
        catch (Exception ex) { Log.Debug($"Verification des mises a jour : {ex.Message}"); }

        // Xbar reste ouvert des jours durant : on recommence regulierement.
        _updateTimer = new DispatcherTimer { Interval = UpdateService.CheckInterval };
        _updateTimer.Tick += async (_, _) => await UpdateService.RunAsync(manual: false);
        _updateTimer.Start();
    }

    /// <summary>
    /// Attend que l'instance remplacee par une mise a jour ait libere le verrou d'instance unique.
    /// </summary>
    private static SingleInstance WaitForPreviousInstance()
    {
        var instance = new SingleInstance();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!instance.IsFirstInstance && DateTime.UtcNow < deadline)
        {
            instance.Dispose();
            Thread.Sleep(250);
            instance = new SingleInstance();
        }
        return instance;
    }

    private void Quit()
    {
        Log.Info("Fermeture de Xbar");
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Exception non geree", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateTimer?.Stop();
        _doubleClick?.Dispose();
        _overlays?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
