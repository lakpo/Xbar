using System.Windows;
using Xbar.Core;
using Xbar.Localization;

namespace Xbar.Update;

public enum UpdateOutcome
{
    UpToDate,
    /// <summary>Une version est disponible et a ete signalee (ou refusee), sans etre installee.</summary>
    Available,
    /// <summary>La nouvelle version est en place et Xbar redemarre.</summary>
    Installed,
    /// <summary>La nouvelle version est en place ; elle servira au prochain lancement.</summary>
    Pending,
    Failed
}

/// <summary>
/// Recherche des nouvelles versions et, selon UpdateMode, proposition, installation ou simple
/// signalement. Lancee peu apres le demarrage, puis a intervalle regulier, et a la demande
/// depuis les parametres.
/// </summary>
public static class UpdateService
{
    /// <summary>Intervalle entre deux recherches pendant que Xbar reste ouvert.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static int _running;

    /// <summary>Version refusee ("Plus tard") : on ne la repropose pas avant le prochain lancement.</summary>
    private static string? _declinedVersion;

    /// <summary>
    /// Version deja installee sans redemarrage. L'executable en cours reste l'ancien : sans ce
    /// rappel, chaque recherche suivante la retrouverait et la retelechargerait.
    /// </summary>
    public static string? PendingVersion { get; private set; }

    /// <summary>Message a montrer a l'utilisateur (bulle de la zone de notification).</summary>
    public static event Action<string>? Notification;

    /// <summary>
    /// <paramref name="manual"/> : recherche demandee par l'utilisateur, faite meme si la
    /// recherche automatique est desactivee, et qui repropose une version refusee.
    /// </summary>
    public static async Task<UpdateOutcome> RunAsync(bool manual)
    {
        var settings = SettingsStore.Current;
        if (!manual && !settings.CheckForUpdates) return UpdateOutcome.UpToDate;
        if (Interlocked.Exchange(ref _running, 1) == 1) return UpdateOutcome.UpToDate;

        try
        {
            var update = await UpdateChecker.CheckAsync().ConfigureAwait(false);
            if (update is null) return UpdateOutcome.UpToDate;
            if (update.Version == PendingVersion) return UpdateOutcome.Pending;

            var mode = settings.UpdateMode;
            if (update.Executable is null) mode = UpdateMode.NotifyOnly;
            else if (mode != UpdateMode.NotifyOnly && Updater.CannotInstallReason() is { } reason)
            {
                Log.Info($"Installation de la mise a jour impossible : {reason}");
                mode = UpdateMode.NotifyOnly;
            }

            switch (mode)
            {
                case UpdateMode.Ask:
                {
                    if (!manual && update.Version == _declinedVersion) return UpdateOutcome.Available;

                    var accepted = await Application.Current.Dispatcher.InvokeAsync(
                        () => UI.UpdatePromptWindow.Ask(update.Version, update.Url));
                    if (!accepted)
                    {
                        Log.Info($"Mise a jour {update.Version} reportee par l'utilisateur");
                        _declinedVersion = update.Version;
                        return UpdateOutcome.Available;
                    }

                    if (await Updater.InstallAsync(update).ConfigureAwait(false) && Updater.Restart(update.Version))
                    {
                        _ = Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
                        return UpdateOutcome.Installed;
                    }
                    break;
                }

                case UpdateMode.Automatic:
                    // Pas de redemarrage : l'utilisateur n'est jamais interrompu. Windows lancera
                    // la nouvelle version a la prochaine ouverture de session.
                    if (await Updater.InstallAsync(update).ConfigureAwait(false))
                    {
                        PendingVersion = update.Version;
                        Notification?.Invoke(Strings.Format("Msg_UpdatePending", update.Version));
                        return UpdateOutcome.Pending;
                    }
                    break;
            }

            Notification?.Invoke(Strings.Format("Msg_UpdateAvailable", update.Version));
            return UpdateOutcome.Available;
        }
        catch (Exception ex)
        {
            Log.Debug($"Verification des mises a jour impossible : {ex.Message}");
            return UpdateOutcome.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
