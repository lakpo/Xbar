using System.Windows;
using Xbar.Core;
using Xbar.Localization;

namespace Xbar.Update;

public enum UpdateOutcome
{
    UpToDate,
    /// <summary>Une version est disponible et a ete signalee, sans etre installee.</summary>
    Available,
    /// <summary>La nouvelle version est en place et Xbar redemarre.</summary>
    Installed,
    Failed
}

/// <summary>
/// Recherche puis, selon les parametres, installation automatique des nouvelles versions.
/// Lancee peu apres le demarrage, puis a intervalle regulier, et a la demande depuis les
/// parametres.
/// </summary>
public static class UpdateService
{
    /// <summary>Intervalle entre deux recherches pendant que Xbar reste ouvert.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static int _running;

    /// <summary>Message a montrer a l'utilisateur (bulle de la zone de notification).</summary>
    public static event Action<string>? Notification;

    /// <summary>
    /// <paramref name="manual"/> : recherche demandee par l'utilisateur, faite meme si la
    /// recherche automatique est desactivee.
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

            if (settings.AutoInstallUpdates && update.Executable is not null)
            {
                if (Updater.CannotInstallReason() is { } reason)
                    Log.Info($"Mise a jour automatique impossible : {reason}");
                else if (await Updater.InstallAsync(update).ConfigureAwait(false) && Updater.Restart(update.Version))
                {
                    _ = Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
                    return UpdateOutcome.Installed;
                }
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
