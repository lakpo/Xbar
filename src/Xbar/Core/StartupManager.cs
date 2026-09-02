using System.IO;
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
            return key?.GetValue(ValueName) is string s && PointsToThisExe(s);
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
    /// La valeur Run est unique pour tout Xbar : si deux copies coexistent (portable et installee),
    /// celle qui n'est pas inscrite doit se voir "desactivee" plutot que de croire l'entree sienne
    /// et laisser l'autre demarrer a sa place.
    /// </summary>
    private static bool PointsToThisExe(string value)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self)) return false;

        var registered = value.Trim().Trim('"');
        if (registered.Length == 0) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(registered), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn($"Comparaison du chemin de demarrage : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Premier lancement : la version installee demarre avec Windows, la version portable non.
    /// </summary>
    public static void ApplyFirstRunDefault()
    {
        if (AppPaths.IsPortable) return;
        if (IsEnabled()) return;

        // Une entree deja presente appartient a une autre copie de Xbar : elle a ete inscrite
        // volontairement, souvent par l'installation dont l'utilisateur se sert vraiment. Un
        // premier lancement ne doit pas la lui prendre en silence.
        var existing = ReadRawValue();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            Log.Info($"Demarrage avec Windows : deja confie a une autre copie ({existing}), inchange.");
            return;
        }

        SetEnabled(true);
    }

    private static string? ReadRawValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            Log.Warn($"Lecture de l'entree de demarrage : {ex.Message}");
            return null;
        }
    }
}
