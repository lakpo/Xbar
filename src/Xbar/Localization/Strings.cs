using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using Xbar.Core;

namespace Xbar.Localization;

/// <summary>
/// Textes de l'interface. Les traductions vivent dans Resources\Strings.resx (anglais, langue
/// neutre) et Resources\Strings.fr.resx ; le .NET SDK compile la seconde en assembly satellite.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Xbar.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>
    /// Texte associe a une cle. En cas de cle absente on renvoie la cle elle-meme : une
    /// traduction manquante doit se voir, jamais faire planter l'interface.
    /// </summary>
    public static string Get(string key)
    {
        try { return Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key; }
        catch (Exception ex)
        {
            Log.Debug($"Texte introuvable pour « {key} » : {ex.Message}");
            return key;
        }
    }

    /// <summary>Texte formate : Format("Msg_X", valeur).</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), args);

    /// <summary>Applique la langue choisie dans les parametres.</summary>
    public static void Apply(AppLanguage language)
    {
        var culture = language switch
        {
            AppLanguage.French => new CultureInfo("fr"),
            AppLanguage.English => new CultureInfo("en"),
            // Auto : on suit la langue de Windows, avec repli sur l'anglais si elle n'est
            // pas traduite (le fichier neutre est en anglais).
            _ => CultureInfo.InstalledUICulture
        };

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Log.Info($"Langue de l'interface : {culture.Name} (reglage : {language})");
    }
}

/// <summary>
/// Extension XAML : Text="{loc:Loc Settings_Theme}".
///
/// La resolution a lieu a l'analyse du XAML, donc au moment ou la fenetre est construite.
/// Changer de langue suppose de reconstruire les fenetres concernees, ce que fait Xbar.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
