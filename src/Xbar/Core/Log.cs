using System.IO;
using System.Text;

namespace Xbar.Core;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Journalisation locale uniquement (aucun envoi réseau).
/// Un fichier par jour, rotation par suppression des fichiers de plus de 7 jours.
/// </summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _file;
    private static readonly List<string> Recent = new(512);

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void Initialize()
    {
        lock (Gate)
        {
            AppPaths.EnsureCreated();
            _file = Path.Combine(AppPaths.LogsDirectory, $"xbar-{DateTime.Now:yyyy-MM-dd}.log");
            TryCleanup();
        }
        Info($"--- Xbar {AppInfo.Version} démarré (portable={AppPaths.IsPortable}) ---");
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(LogLevel.Error, $"{message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {message}";
        lock (Gate)
        {
            Recent.Add(line);
            if (Recent.Count > 500) Recent.RemoveRange(0, 100);
            if (_file is null) return;
            try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* la journalisation ne doit jamais faire échouer l'application */ }
        }
    }

    /// <summary>Dernières lignes en mémoire, pour le bouton « Copier un diagnostic ».</summary>
    public static string GetRecent()
    {
        lock (Gate) return string.Join(Environment.NewLine, Recent);
    }

    private static void TryCleanup()
    {
        try
        {
            var limit = DateTime.Now.AddDays(-7);
            foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDirectory, "xbar-*.log"))
                if (File.GetLastWriteTime(f) < limit) File.Delete(f);
        }
        catch { }
    }
}
