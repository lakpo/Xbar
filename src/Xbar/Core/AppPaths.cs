using System.IO;

namespace Xbar.Core;

/// <summary>
/// Emplacement des données de Xbar.
/// Mode installé  : %AppData%\Xbar\
/// Mode portable  : &lt;dossier de Xbar.exe&gt;\Data\  (activé par un fichier "portable" à côté de l'exe)
/// </summary>
public static class AppPaths
{
    public static string ExeDirectory { get; } =
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(ExeDirectory, "portable")) ||
        File.Exists(Path.Combine(ExeDirectory, "portable.txt"));

    public static string Root { get; } = IsPortable
        ? Path.Combine(ExeDirectory, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Xbar");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    /// <summary>
    /// Liste de raccourcis d'une barre. La premiere garde le nom historique
    /// shortcuts.json : une installation existante la retrouve telle quelle.
    /// </summary>
    public static string ShortcutsFileFor(int profile) =>
        Path.Combine(Root, profile <= 0 ? "shortcuts.json" : $"shortcuts-{profile + 1}.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");
    public static string IconsDirectory => Path.Combine(Root, "icons");
    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(IconsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
