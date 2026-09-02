using System.IO;
using System.Text.Json;

namespace Xbar.Core;

/// <summary>Chargement/sauvegarde de settings.json, avec écriture atomique.</summary>
public static class SettingsStore
{
    private static readonly Lock Gate = new();

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Déclenché après un changement de configuration (appliqué sur le thread UI par l'appelant).</summary>
    public static event Action<AppSettings>? Changed;

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    Current = JsonStore.Deserialize(json, JsonContext.Default.AppSettings) ?? new AppSettings();
                }
                else
                {
                    Current = new AppSettings();
                    Normalize(Current);
                    JsonStore.Write(AppPaths.SettingsFile, Current, JsonContext.Default.AppSettings);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Lecture de settings.json impossible, valeurs par défaut utilisées", ex);
                Current = new AppSettings();
            }
            Normalize(Current);
        }
        Log.MinimumLevel = Current.VerboseLogging ? LogLevel.Debug : LogLevel.Info;
    }

    public static void Save()
    {
        lock (Gate)
        {
            Normalize(Current);
            JsonStore.Write(AppPaths.SettingsFile, Current, JsonContext.Default.AppSettings);
        }
        Log.MinimumLevel = Current.VerboseLogging ? LogLevel.Debug : LogLevel.Info;
    }

    /// <summary>Remplace la configuration courante et notifie les abonnés.</summary>
    public static void Apply(AppSettings settings)
    {
        lock (Gate) { Current = settings; }
        Save();
        Changed?.Invoke(Current);
    }

    public static void Update(Action<AppSettings> mutate)
    {
        var copy = Current.Clone();
        mutate(copy);
        Apply(copy);
    }

    private static void Normalize(AppSettings s)
    {
        s.ButtonSize = Math.Clamp(s.ButtonSize, 20, 64);
        s.Opacity = Math.Clamp(s.Opacity, 0.2, 1.0);
        s.EdgeMargin = Math.Clamp(s.EdgeMargin, 0, 64);
        s.JpegQuality = Math.Clamp(s.JpegQuality, 30, 100);
        s.MaxHistoryFolders = Math.Clamp(s.MaxHistoryFolders, 5, 50);
        if (s.VerticalRatio is { } ratio) s.VerticalRatio = Math.Clamp(ratio, 0, 1);
        s.MinExplorerWidth = Math.Clamp(s.MinExplorerWidth, 200, 2000);
        s.MinExplorerHeight = Math.Clamp(s.MinExplorerHeight, 120, 2000);
    }
}

/// <summary>Utilitaires JSON communs (écriture atomique + options partagées).</summary>
public static class JsonStore
{
    public static T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        => JsonSerializer.Deserialize(json, info);

    public static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(value, info);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Écriture de {Path.GetFileName(path)} impossible", ex);
        }
    }
}
