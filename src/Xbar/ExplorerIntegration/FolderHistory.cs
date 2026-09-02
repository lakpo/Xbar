using System.IO;
using Xbar.Core;

namespace Xbar.ExplorerIntegration;

/// <summary>Racine de history.json.</summary>
public sealed class FolderHistoryFile
{
    public int Version { get; set; } = 1;
    public List<string> Folders { get; set; } = new();
}

/// <summary>
/// Historique des derniers dossiers parcourus, tous Explorateurs confondus.
///
/// L'alimentation vient du titre des fenetres : l'Explorateur ne signale pas ses navigations,
/// mais renomme sa fenetre a chaque changement de dossier (voir ExplorerTracker). Aucun polling.
/// </summary>
public static class FolderHistory
{
    private static readonly Lock Gate = new();
    private static FolderHistoryFile _file = new();

    /// <summary>Declenche apres tout ajout, sur le thread appelant.</summary>
    public static event Action? Changed;

    /// <summary>Du plus recent au plus ancien.</summary>
    public static IReadOnlyList<string> Folders
    {
        get { lock (Gate) return _file.Folders.ToArray(); }
    }

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.HistoryFile)) { _file = new FolderHistoryFile(); return; }
                var json = File.ReadAllText(AppPaths.HistoryFile);
                _file = JsonStore.Deserialize(json, JsonContext.Default.FolderHistoryFile) ?? new FolderHistoryFile();
                Trim();
            }
            catch (Exception ex)
            {
                Log.Warn($"Lecture de history.json impossible : {ex.Message}");
                _file = new FolderHistoryFile();
            }
        }
    }

    /// <summary>
    /// Enregistre un dossier. Un chemin deja present remonte en tete plutot que d'etre duplique.
    /// </summary>
    public static void Record(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var normalized = path.TrimEnd(Path.DirectorySeparatorChar);
        if (normalized.Length == 0) normalized = path; // racine de disque : "C:\"

        bool changed;
        lock (Gate)
        {
            var existing = _file.Folders.FindIndex(
                f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));

            if (existing == 0) return; // deja en tete : rien a faire
            if (existing > 0) _file.Folders.RemoveAt(existing);

            _file.Folders.Insert(0, normalized);
            Trim();
            changed = true;
        }

        if (!changed) return;
        Save();
        Log.Debug($"Historique : {normalized}");
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate) _file.Folders.Clear();
        Save();
        Changed?.Invoke();
    }

    private static void Trim()
    {
        var max = SettingsStore.Current.MaxHistoryFolders;
        if (_file.Folders.Count > max)
            _file.Folders.RemoveRange(max, _file.Folders.Count - max);
    }

    private static void Save()
    {
        lock (Gate) JsonStore.Write(AppPaths.HistoryFile, _file, JsonContext.Default.FolderHistoryFile);
    }
}
