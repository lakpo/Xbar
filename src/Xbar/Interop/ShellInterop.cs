using System.Runtime.InteropServices;

namespace Xbar.Interop;

/// <summary>
/// Interfaces COM du Shell utilisees pour dialoguer proprement avec l'Explorateur
/// (aucune injection : tout passe par les objets COM publics exposes par Explorer).
/// </summary>
public static class ShellGuids
{
    public static Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    public static Guid IID_IFolderView = new("cde725b0-ccc9-4519-917e-325d72fab4ce");
    public static Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
}

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IServiceProviderCom
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellBrowser
{
    // --- IOleWindow ---
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    // --- IShellBrowser ---
    [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
    [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
    [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
    [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string pszStatusText);
    [PreserveSig] int EnableModelessSB(bool fEnable);
    [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
    [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
    [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
    [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
    [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
    [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? ppshv);
    [PreserveSig] int OnViewWindowActive([MarshalAs(UnmanagedType.IUnknown)] object pshv);
    [PreserveSig] int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellView
{
    // --- IOleWindow ---
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    // --- IShellView (seules les premieres entrees de la vtable nous interessent) ---
    [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
    [PreserveSig] int EnableModeless(bool fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
}

/// <summary>Vue de dossier. Seules les trois premieres entrees de la vtable nous servent.</summary>
[ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFolderView
{
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint ViewMode);
    [PreserveSig] int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[ComImport, Guid("1AC3D9F0-175C-11d1-95BE-00609797EA4F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPersistFolder2
{
    // --- IPersist ---
    [PreserveSig] int GetClassID(out Guid pClassID);
    // --- IPersistFolder ---
    [PreserveSig] int Initialize(IntPtr pidl);
    // --- IPersistFolder2 ---
    [PreserveSig] int GetCurFolder(out IntPtr ppidl);
}

/// <summary>Flags de IShellBrowser::BrowseObject.</summary>
public static class SBSP
{
    public const uint SBSP_ABSOLUTE = 0x0000;
    public const uint SBSP_SAMEBROWSER = 0x0001;
    public const uint SBSP_NEWBROWSER = 0x0002;
    public const uint SBSP_DEFBROWSER = 0x0000;
    /// <summary>Navigue vers le dossier parent. Echoue proprement a la racine.</summary>
    public const uint SBSP_PARENT = 0x2000;
}

public static class ShellNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int SHParseDisplayName(string name, IntPtr bindingContext,
        out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SHGetPathFromIDList(IntPtr pidl,
        [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl,
        IntPtr[]? apidl, uint dwFlags);

    // --- Nom affiche par le Shell ---

    private const uint SHGFI_DISPLAYNAME = 0x00000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes,
        ref SHFILEINFO info, uint size, uint flags);

    /// <summary>
    /// Nom tel que l'Explorateur l'affiche, et non le nom sur le disque : les dossiers systeme
    /// portent un nom localise (« Images » pour le dossier Pictures du profil). Renvoie null si
    /// le Shell ne sait pas repondre.
    /// </summary>
    public static string? GetDisplayName(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_DISPLAYNAME);
            return result == IntPtr.Zero || string.IsNullOrWhiteSpace(info.szDisplayName)
                ? null
                : info.szDisplayName;
        }
        catch { return null; }
    }
}
