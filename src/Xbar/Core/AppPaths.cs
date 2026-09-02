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
    public static string ShortcutsFile => Path.Combine(Root, "shortcuts.json");
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
