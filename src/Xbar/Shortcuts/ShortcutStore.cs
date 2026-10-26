using System.IO;
using Xbar.Core;
using Xbar.Localization;

namespace Xbar.Shortcuts;

/// <summary>
/// Liste des raccourcis de la configuration active, persistee dans shortcuts.json (ou
/// shortcuts-2.json, shortcuts-3.json pour les configurations suivantes).
/// </summary>
public static class ShortcutStore
{
    private static readonly Lock Gate = new();

    public static ShortcutList List { get; private set; } = new();

    public static IReadOnlyList<ShortcutItem> Items => List.Items;

    /// <summary>Declenche apres toute modification de la liste.</summary>
    public static event Action? Changed;

    /// <summary>Nombre de configurations entre lesquelles la barre peut basculer.</summary>
    public const int ProfileCount = 3;

    /// <summary>Configuration chargee, de 0 a ProfileCount - 1.</summary>
    public static int Profile { get; private set; }

    private static string CurrentFile => AppPaths.ShortcutsFileFor(Profile);

    /// <summary>Charge la configuration designee par les parametres.</summary>
    public static void Load() => Load(SettingsStore.Current.ActiveProfile);

    private static void Load(int profile)
    {
        lock (Gate)
        {
            Profile = Math.Clamp(profile, 0, ProfileCount - 1);
            try
            {
                if (File.Exists(CurrentFile))
                {
                    var json = File.ReadAllText(CurrentFile);
                    List = JsonStore.Deserialize(json, JsonContext.Default.ShortcutList) ?? new ShortcutList();
                    if (Migrate(List))
                        JsonStore.Write(CurrentFile, List, JsonContext.Default.ShortcutList);
                }
                else
                {
                    // Une configuration jamais ouverte demarre avec le contenu par defaut.
                    List = CreateDefault();
                    JsonStore.Write(CurrentFile, List, JsonContext.Default.ShortcutList);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Lecture de {Path.GetFileName(CurrentFile)} impossible", ex);
                List = CreateDefault();
            }
        }
    }

    /// <summary>
    /// Bascule sur une autre configuration : sa liste remplace la liste courante dans toutes les
    /// barres, et le choix est memorise pour le prochain lancement.
    /// </summary>
    public static void SwitchProfile(int profile)
    {
        profile = Math.Clamp(profile, 0, ProfileCount - 1);
        if (profile == Profile) return;

        Load(profile);
        // Sans notification des parametres : Changed ci-dessous reconstruit deja les barres.
        SettingsStore.UpdateQuietly(s => s.ActiveProfile = profile);
        Log.Info($"Configuration {profile + 1} active");
        Changed?.Invoke();
    }

    /// <summary>Format courant de shortcuts.json.</summary>
    private const int CurrentVersion = 2;

    /// <summary>
    /// Mises a jour du format. Renvoie true si le fichier doit etre reecrit.
    ///
    /// Version 2 : le depot de fichiers sur un raccourci d'application devient actif par defaut,
    /// pour se comporter comme un depot sur l'executable lui-meme. Les listes ecrites en
    /// version 1 portaient un "acceptsDrops": false explicite qu'il faut donc relever.
    /// </summary>
    private static bool Migrate(ShortcutList list)
    {
        if (list.Version >= CurrentVersion) return false;

        var changed = 0;
        foreach (var item in list.Items)
        {
            if (item.Kind != ShortcutKind.Application || item.AcceptsDrops) continue;
            item.AcceptsDrops = true;
            changed++;
        }

        list.Version = CurrentVersion;
        Log.Info($"shortcuts.json migre en version {CurrentVersion} ({changed} raccourci(s) mis a jour)");
        return true;
    }

    /// <summary>
    /// Contenu de la barre au premier lancement : le dossier Images de l'utilisateur, un
    /// separateur, puis les deux actions integrees.
    /// </summary>
    private static ShortcutList CreateDefault()
    {
        var list = new ShortcutList();

        // Chemin localise par Windows : « Images » en francais, « Pictures » en anglais.
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrEmpty(pictures))
        {
            list.Items.Add(new ShortcutItem
            {
                Kind = ShortcutKind.Folder,
                Name = Interop.ShellNative.GetDisplayName(pictures)
                       ?? Path.GetFileName(pictures.TrimEnd(Path.DirectorySeparatorChar)),
                Target = pictures
            });
            list.Items.Add(new ShortcutItem
            {
                Kind = ShortcutKind.Separator,
                Name = Strings.Get("Common_Separator")
            });
        }

        list.Items.Add(new ShortcutItem
        {
            Kind = ShortcutKind.Action,
            ActionId = Actions.RecentFoldersAction.ActionId,
            Name = Strings.Get("Action_RecentFolders")
        });
        list.Items.Add(new ShortcutItem
        {
            Kind = ShortcutKind.Action,
            ActionId = Actions.PasteClipboardImageAction.ActionId,
            Name = Strings.Get("Action_PasteImage")
        });

        return list;
    }

    public static void Save()
    {
        lock (Gate) JsonStore.Write(CurrentFile, List, JsonContext.Default.ShortcutList);
        Changed?.Invoke();
    }

    public static void Add(ShortcutItem item, int index = -1)
    {
        lock (Gate)
        {
            if (index < 0 || index > List.Items.Count) List.Items.Add(item);
            else List.Items.Insert(index, item);
        }
        Save();
    }

    public static void Remove(string id)
    {
        lock (Gate) List.Items.RemoveAll(i => i.Id == id);
        Save();
    }

    public static void Replace(ShortcutItem item)
    {
        lock (Gate)
        {
            var idx = List.Items.FindIndex(i => i.Id == item.Id);
            if (idx >= 0) List.Items[idx] = item;
        }
        Save();
    }

    public static void Move(string id, int newIndex)
    {
        lock (Gate)
        {
            var idx = List.Items.FindIndex(i => i.Id == id);
            if (idx < 0) return;
            var item = List.Items[idx];
            List.Items.RemoveAt(idx);
            newIndex = Math.Clamp(newIndex, 0, List.Items.Count);
            List.Items.Insert(newIndex, item);
        }
        Save();
    }

    public static int IndexOf(string id)
    {
        lock (Gate) return List.Items.FindIndex(i => i.Id == id);
    }

    public static ShortcutItem? Find(string id)
    {
        lock (Gate) return List.Items.FirstOrDefault(i => i.Id == id);
    }

    public static void ReplaceAll(IEnumerable<ShortcutItem> items)
    {
        lock (Gate) List.Items = items.ToList();
        Save();
    }
}
