using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using Xbar.Core;

namespace Xbar.Icons;

/// <summary>
/// Bibliotheque d'icones vectorielles integree : Material Design Icons (Pictogrammers,
/// licence Apache 2.0, voir Icons\MDI-LICENSE.txt). Chaque icone est un chemin unique dans un
/// carre de 24 unites, que l'on peut donc peindre dans n'importe quelle couleur.
///
/// Un raccourci qui en utilise une porte "mdi:&lt;nom&gt;" dans IconOverride, et sa couleur
/// dans IconColor (absente : la couleur du texte de la barre, qui suit le theme).
/// Les donnees sont regenerees par build\icons\generate-icons.mjs.
/// </summary>
public static class IconLibrary
{
    public const string Prefix = "mdi:";

    /// <summary>Cote du carre dans lequel les icones sont dessinees.</summary>
    private const double ViewBox = 24;

    private static readonly Lazy<Dictionary<string, string>> Paths = new(LoadPaths);
    private static readonly Dictionary<string, Geometry?> Geometries = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>Noms de toutes les icones, par ordre alphabetique.</summary>
    public static IReadOnlyList<string> Names => _names ??= Paths.Value.Keys.Order(StringComparer.Ordinal).ToList();
    private static List<string>? _names;

    public static bool IsLibraryIcon(string? iconOverride) =>
        iconOverride is not null && iconOverride.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Nom de l'icone designee par une valeur "mdi:nom", ou null.</summary>
    public static string? NameOf(string? iconOverride) =>
        IsLibraryIcon(iconOverride) ? iconOverride![Prefix.Length..] : null;

    public static bool Contains(string name) => Paths.Value.ContainsKey(name);

    /// <summary>
    /// Icones dont le nom contient tous les mots recherches. Les noms qui commencent par le
    /// premier mot passent devant : "folder" donne folder, folder-open... avant home-folder.
    /// </summary>
    public static IEnumerable<string> Search(string query)
    {
        var words = query.Trim().ToLowerInvariant()
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Names;

        return Names
            .Where(n => words.All(w => n.Contains(w, StringComparison.Ordinal)))
            .OrderBy(n => n.StartsWith(words[0], StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(n => n.Length);
    }

    /// <summary>
    /// Image de l'icone, peinte avec <paramref name="brush"/>. Un carre transparent fixe les
    /// limites du dessin a 24 x 24 : l'icone garde ainsi ses marges d'origine et toutes ont la
    /// meme taille apparente, quelle que soit l'etendue de leur trace.
    /// </summary>
    public static ImageSource? Render(string name, Brush brush)
    {
        var geometry = GetGeometry(name);
        if (geometry is null) return null;

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
            new RectangleGeometry(new Rect(0, 0, ViewBox, ViewBox))));
        group.Children.Add(new GeometryDrawing(brush, null, geometry));

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Pinceau d'une couleur "#RRGGBB", ou null si la valeur est absente ou invalide.</summary>
    public static Brush? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is not Color color) return null;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch (Exception) { return null; } // FormatException ou NotSupportedException selon la saisie
    }

    /// <summary>Couleur du texte de la barre dans le theme courant.</summary>
    public static Brush ThemeBrush() =>
        Application.Current?.Resources["BarForeground"] as Brush ?? Brushes.Gray;

    private static Geometry? GetGeometry(string name)
    {
        lock (Gate)
        {
            if (Geometries.TryGetValue(name, out var cached)) return cached;
        }

        Geometry? geometry = null;
        if (Paths.Value.TryGetValue(name, out var data))
        {
            try
            {
                geometry = Geometry.Parse(data);
                geometry.Freeze();
            }
            catch (Exception ex)
            {
                Log.Warn($"Icone {name} illisible : {ex.Message}");
            }
        }

        lock (Gate) Geometries[name] = geometry;
        return geometry;
    }

    private static Dictionary<string, string> LoadPaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var resource = typeof(IconLibrary).Assembly.GetManifestResourceStream("Xbar.Icons.mdi.txt.gz");
            if (resource is null)
            {
                Log.Warn("Bibliotheque d'icones absente de l'executable");
                return paths;
            }

            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            while (reader.ReadLine() is { } line)
            {
                var tab = line.IndexOf('\t');
                if (tab > 0) paths[line[..tab]] = line[(tab + 1)..];
            }
            Log.Debug($"Bibliotheque d'icones : {paths.Count} icones");
        }
        catch (Exception ex)
        {
            Log.Error("Lecture de la bibliotheque d'icones", ex);
        }
        return paths;
    }
}
