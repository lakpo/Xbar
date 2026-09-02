using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xbar.Core;
using Xbar.Shortcuts;

namespace Xbar.Icons;

/// <summary>
/// Recuperation des icones via le Shell (SHGetFileInfo + listes d'images systeme), avec cache.
/// Les icones personnalisees sont copiees dans %AppData%\Xbar\icons\.
/// </summary>
public static class IconService
{
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    public static void ClearCache()
    {
        lock (Gate) Cache.Clear();
    }

    /// <summary>
    /// Icone a afficher pour un raccourci (icone personnalisee si definie).
    /// <paramref name="desiredPixelSize"/> est la taille de rendu reelle, en pixels ecran.
    /// </summary>
    public static ImageSource? GetIcon(ShortcutItem item, int desiredPixelSize = 32)
    {
        if (!string.IsNullOrWhiteSpace(item.IconOverride))
        {
            var path = ResolveIconOverride(item.IconOverride);
            if (path is not null)
            {
                var custom = LoadImageFile(path) ?? GetShellIcon(path, false, desiredPixelSize);
                if (custom is not null) return custom;
            }
        }

        return item.Kind switch
        {
            ShortcutKind.Folder => GetShellIcon(item.Target, true, desiredPixelSize),
            ShortcutKind.Application or ShortcutKind.File =>
                GetShellIcon(item.Target, false, desiredPixelSize),
            _ => null
        };
    }

    public static string? ResolveIconOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Path.IsPathRooted(value)) return File.Exists(value) ? value : null;
        var local = Path.Combine(AppPaths.IconsDirectory, value);
        return File.Exists(local) ? local : null;
    }

    /// <summary>Copie une icone choisie par l'utilisateur dans le dossier icons\ et renvoie son nom.</summary>
    public static string? ImportIcon(string sourcePath)
    {
        try
        {
            AppPaths.EnsureCreated();
            var name = $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
            var dest = Path.Combine(AppPaths.IconsDirectory, name);
            File.Copy(sourcePath, dest, overwrite: true);
            return name;
        }
        catch (Exception ex)
        {
            Log.Error($"Import de l'icone {sourcePath}", ex);
            return null;
        }
    }

    private static ImageSource? LoadImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico")) return null;

        var key = "file:" + path + File.GetLastWriteTimeUtc(path).Ticks;
        lock (Gate) if (Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 96;
            bmp.EndInit();
            bmp.Freeze();
            lock (Gate) Cache[key] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn($"Chargement de l'image {path} : {ex.Message}");
            return null;
        }
    }

    /// <summary>Icone systeme d'un fichier ou dossier, a la resolution la mieux adaptee.</summary>
    public static ImageSource? GetShellIcon(string path, bool isDirectory = false, int desiredPixelSize = 32)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var key = $"shell:{path}|{(isDirectory ? "d" : "f")}|{desiredPixelSize}";
        lock (Gate) if (Cache.TryGetValue(key, out var cached)) return cached;

        var image = ExtractIcon(path, isDirectory, desiredPixelSize);
        if (image is null) return null;

        image.Freeze();
        lock (Gate) Cache[key] = image;
        return image;
    }

    /// <summary>
    /// Proportion minimale du canevas que le dessin doit occuper pour qu'une liste soit retenue.
    /// </summary>
    private const double MinContentRatio = 0.55;

    private static readonly (int Shil, int Size)[] ImageLists =
    {
        (SHIL_SMALL, 16), (SHIL_LARGE, 32), (SHIL_EXTRALARGE, 48), (SHIL_JUMBO, 256)
    };

    /// <summary>
    /// Listes d'images a essayer, de la mieux adaptee a la moins bonne : d'abord celles dont le
    /// format couvre la taille demandee, de la plus proche a la plus grande, puis les plus petites.
    /// </summary>
    private static IEnumerable<int> CandidateLists(int desiredPixelSize) =>
        ImageLists
            .OrderBy(l => l.Size < desiredPixelSize ? 1 : 0)
            .ThenBy(l => Math.Abs(l.Size - desiredPixelSize))
            .Select(l => l.Shil);

    private static ImageSource? ExtractIcon(string path, bool isDirectory, int desiredPixelSize)
    {
        var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var flags = SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES;
        var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        if (exists) flags &= ~SHGFI_USEFILEATTRIBUTES;

        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero) return FallbackIcon(path);

        // Une icone sans variante dans le format demande n'est pas agrandie par Windows : elle est
        // CENTREE dans un canevas plus grand. Une icone de dossier personnalisee tiree de la liste
        // JUMBO n'occupe ainsi que 48 px sur 256, et parait minuscule une fois reduite au bouton.
        // On retient donc la premiere liste ou le dessin remplit vraiment son canevas.
        ImageSource? fallback = null;
        foreach (var shil in CandidateLists(desiredPixelSize))
        {
            var image = GetFromImageList(shil, info.iIcon);
            if (image is null) continue;

            fallback ??= image;
            if (ContentRatio(image) >= MinContentRatio) return image;
        }

        return fallback ?? FallbackIcon(path);
    }

    /// <summary>Part du canevas reellement occupee par le dessin (pixels non transparents).</summary>
    private static double ContentRatio(ImageSource source)
    {
        if (source is not BitmapSource bitmap) return 1;

        try
        {
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            if (width == 0 || height == 0) return 1;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            int minX = width, maxX = -1, minY = height, maxY = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (pixels[y * stride + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }

            if (maxX < 0) return 0; // entierement transparent
            return Math.Max(maxX - minX + 1, maxY - minY + 1) / (double)Math.Max(width, height);
        }
        catch (Exception ex)
        {
            Log.Debug($"Mesure du remplissage d'une icone : {ex.Message}");
            return 1;
        }
    }

    private static ImageSource? GetFromImageList(int shil, int index)
    {
        try
        {
            var iid = IID_IImageList;
            if (SHGetImageList(shil, ref iid, out var list) != 0 || list is null) return null;
            try
            {
                if (list.GetIcon(index, ILD_TRANSPARENT, out var hIcon) != 0 || hIcon == IntPtr.Zero) return null;
                try { return FromHIcon(hIcon); }
                finally { DestroyIcon(hIcon); }
            }
            finally { Marshal.ReleaseComObject(list); }
        }
        catch { return null; }
    }

    private static ImageSource? FallbackIcon(string path)
    {
        var info = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_LARGEICON;
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero) return null;
        try { return FromHIcon(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    private static ImageSource? FromHIcon(IntPtr hIcon)
    {
        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch (Exception ex)
        {
            Log.Warn($"Conversion d'icone : {ex.Message}");
            return null;
        }
    }

    // --- Interop ---

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    private const int SHIL_LARGE = 0;
    private const int SHIL_SMALL = 1;
    private const int SHIL_EXTRALARGE = 2;
    private const int SHIL_JUMBO = 4;
    private const int ILD_TRANSPARENT = 1;

    private static Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

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
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IImageList? ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }
}
