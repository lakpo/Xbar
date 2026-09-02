using System.Text.Json.Serialization;

namespace Xbar.Core;

public enum BarTheme { Auto, Light, Dark }

/// <summary>Langue de l'interface. Auto suit celle de Windows, avec repli sur l'anglais.</summary>
public enum AppLanguage { Auto, French, English }
public enum LabelMode
{
    IconsOnly,
    IconAndText,
    /// <summary>Icones seules, le texte n'apparait que lorsque la souris survole la barre.</summary>
    IconAndTextOnHover,
    /// <summary>
    /// Icones seules ; survoler un bouton fait sortir son nom dans une etiquette horizontale,
    /// a cote de lui. La barre elle-meme ne change pas de largeur.
    /// </summary>
    LabelOnItemHover
}
public enum ClipboardImageFormat { Png, Jpg, WebP }
public enum BarAnchor { Top, Center, Bottom }

/// <summary>Configuration globale (pas de profils en V1).</summary>
public sealed class AppSettings
{
    // --- Apparence ---
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
    public BarTheme Theme { get; set; } = BarTheme.Auto;
    public LabelMode Labels { get; set; } = LabelMode.LabelOnItemHover;

    /// <summary>Taille d'un bouton en pixels logiques (DIP).</summary>
    public double ButtonSize { get; set; } = 24;

    /// <summary>Opacité de la barre, 0.2 – 1.0.</summary>
    public double Opacity { get; set; } = 0.92;

    public BarAnchor Anchor { get; set; } = BarAnchor.Center;

    /// <summary>
    /// Position verticale libre, exprimee en fraction de la hauteur de la fenetre Explorer
    /// (0 = haut, 1 = bas). Definie en faisant glisser la poignee de la barre ; null tant que
    /// l'utilisateur s'en tient a l'un des presets d'ancrage.
    /// </summary>
    public double? VerticalRatio { get; set; }

    /// <summary>Marge entre le bord droit de la zone client et la barre (DIP), en plus de la scrollbar.</summary>
    public double EdgeMargin { get; set; } = 6;

    /// <summary>Décaler la barre vers la gauche de la largeur d'une scrollbar verticale.</summary>
    public bool AvoidScrollbar { get; set; } = true;

    // --- Comportement ---
    public bool AutoHide { get; set; }
    public bool BarEnabled { get; set; } = true;

    /// <summary>Largeur/hauteur minimales de la fenêtre Explorer (DIP) en dessous desquelles la barre est masquée.</summary>
    public double MinExplorerWidth { get; set; } = 480;
    public double MinExplorerHeight { get; set; } = 260;

    /// <summary>Double-clic dans le vide de la vue des fichiers = remonter au dossier parent.</summary>
    public bool DoubleClickGoUp { get; set; } = true;

    // --- Action « coller l'image du presse-papiers » ---
    public ClipboardImageFormat ClipboardFormat { get; set; } = ClipboardImageFormat.Png;
    public bool ClipboardAskFileName { get; set; }
    public int JpegQuality { get; set; } = 90;

    /// <summary>Nombre de dossiers conserves dans l'historique.</summary>
    public int MaxHistoryFolders { get; set; } = 20;

    // --- Divers ---
    public bool CheckForUpdates { get; set; } = true;
    public bool VerboseLogging { get; set; }

    /// <summary>Largeur de la barre en mode icones seules.</summary>
    [JsonIgnore]
    public double CompactBarWidth => ButtonSize + 12;

    /// <summary>Largeur de la barre lorsque le texte est affiche.</summary>
    [JsonIgnore]
    public double ExpandedBarWidth => Math.Max(ButtonSize * 4.5, 140);

    [JsonIgnore]
    public double BarWidth => Labels == LabelMode.IconAndText ? ExpandedBarWidth : CompactBarWidth;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
