using System.Runtime.InteropServices;
using Xbar.Core;
using Xbar.Interop;

namespace Xbar.Shortcuts;

/// <summary>
/// Emplacements Windows sans chemin de fichier : Ce PC, Accueil, Reseau, Corbeille, Galerie.
///
/// Ces dossiers n'existent que dans l'espace de noms du Shell. SHGetPathFromIDList ne leur rend
/// aucun chemin, donc tout ce qui repose sur Directory.Exists les rejette : ils passent par une
/// sorte de raccourci distincte (ShortcutKind.ShellFolder) dont la cible est une adresse Shell
/// ("shell:MyComputerFolder"), resolue par SHParseDisplayName comme n'importe quel dossier.
///
/// Le nom affiche est demande a Windows plutot que code en dur : il arrive deja traduit dans la
/// langue du systeme.
/// </summary>
public static class ShellLocations
{
    /// <summary>Adresses Shell proposees a l'ajout. Verifiees une a une sur Windows 11.</summary>
    public static readonly IReadOnlyList<string> Known =
    [
        "shell:MyComputerFolder",                              // Ce PC
        "shell:::{F874310E-B6B7-47DC-BC84-B9E6B38F5903}",      // Accueil
        "shell:::{E88865EA-0E1C-4E20-9AA6-EDCD0212C87C}",      // Galerie
        "shell:NetworkPlacesFolder",                           // Reseau
        "shell:RecycleBinFolder"                               // Corbeille
    ];

    /// <summary>Vrai si la cible designe l'espace de noms du Shell et non un chemin de disque.</summary>
    public static bool IsShellAddress(string? target) =>
        !string.IsNullOrWhiteSpace(target) &&
        (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
         target.StartsWith("::{", StringComparison.Ordinal));

    /// <summary>Nom donne par Windows a cet emplacement, deja traduit. Null s'il est inconnu.</summary>
    public static string? DisplayName(string target)
    {
        var pidl = Parse(target);
        if (pidl == IntPtr.Zero) return null;

        try
        {
            var info = new SHFILEINFO();
            var ok = SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_PIDL | SHGFI_DISPLAYNAME);
            return ok != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.szDisplayName)
                ? info.szDisplayName
                : null;
        }
        finally { ShellNative.ILFree(pidl); }
    }

    /// <summary>PIDL de l'emplacement, ou IntPtr.Zero. A liberer par ILFree.</summary>
    public static IntPtr Parse(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return IntPtr.Zero;
        try
        {
            var hr = ShellNative.SHParseDisplayName(target, IntPtr.Zero, out var pidl, 0, out _);
            return hr == 0 ? pidl : IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Log.Warn($"Adresse Shell non reconnue ({target}) : {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>Raccourci pret a ajouter, portant le nom que Windows donne a l'emplacement.</summary>
    public static ShortcutItem CreateShortcut(string target) => new()
    {
        Kind = ShortcutKind.ShellFolder,
        Target = target,
        Name = DisplayName(target) ?? target,
        AcceptsDrops = false // rien ne se depose dans "Ce PC" ou "Reseau"
    };

    internal const uint SHGFI_PIDL = 0x000000008;
    internal const uint SHGFI_DISPLAYNAME = 0x000000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes,
        ref SHFILEINFO info, uint size, uint flags);
}
