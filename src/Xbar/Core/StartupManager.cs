using Microsoft.Win32;

namespace Xbar.Core;

/// <summary>
/// Demarrage automatique via la cle Run de l'utilisateur courant (aucun droit administrateur,
/// aucune tache planifiee).
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Xbar";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Contains("Xbar", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"Lecture du demarrage automatique : {ex.Message}");
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            Log.Info($"Demarrage avec Windows : {(enabled ? "active" : "desactive")}");
        }
        catch (Exception ex)
        {
            Log.Error("Modification du demarrage automatique", ex);
        }
    }

    /// <summary>
    /// Premier lancement : la version installee demarre avec Windows, la version portable non.
    /// </summary>
    public static void ApplyFirstRunDefault()
    {
        if (AppPaths.IsPortable) return;
        if (IsEnabled()) return;
        SetEnabled(true);
    }
}
