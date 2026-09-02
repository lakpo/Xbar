using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Xbar.Core;
using Xbar.UI;
using Xbar.Localization;

namespace Xbar.Actions;

/// <summary>
/// Enregistre l'image du presse-papiers dans le dossier affiche par l'Explorateur.
/// Seule action integree de la V1.
/// </summary>
public sealed class PasteClipboardImageAction : XbarAction
{
    public const string ActionId = "clipboard.paste-image";

    public override string Id => ActionId;
    public override string Title => Strings.Get("Action_PasteImage");
    public override string Glyph => ""; // Segoe Fluent Icons : Paste

    public override async Task ExecuteAsync(ActionContext context)
    {
        var folder = context.CurrentFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            context.Report(Strings.Get("Msg_UnknownFolder"), false);
            return;
        }

        var image = GetClipboardImage();
        if (image is null)
        {
            context.Report(Strings.Get("Msg_NoClipboardImage"), false);
            return;
        }

        var settings = SettingsStore.Current;
        var format = settings.ClipboardFormat;
        var (encoder, extension) = CreateEncoder(format, settings.JpegQuality);

        var baseName = $"Clipboard_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        if (settings.ClipboardAskFileName)
        {
            var chosen = InputDialog.Ask(Strings.Get("Action_FileNameTitle"), Strings.Get("Action_FileNamePrompt"), baseName);
            if (chosen is null) return; // annule
            baseName = SanitizeFileName(chosen);
            if (baseName.Length == 0) baseName = $"Clipboard_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        }

        var path = MakeUniquePath(folder, baseName, extension);

        try
        {
            encoder.Frames.Add(BitmapFrame.Create(image));
            await using (var stream = File.Create(path))
                encoder.Save(stream);

            Log.Info($"Image du presse-papiers enregistree : {path}");
            context.Report(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Log.Error($"Enregistrement de l'image dans {path}", ex);
            context.Report(Strings.Get("Msg_SaveFailed"), false);
        }
    }

    /// <summary>
    /// Prefere le format PNG present dans le presse-papiers (transparence conservee),
    /// sinon retombe sur le bitmap standard.
    /// </summary>
    private static BitmapSource? GetClipboardImage()
    {
        try
        {
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream png)
            {
                png.Position = 0;
                var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0) return decoder.Frames[0];
            }

            if (Clipboard.ContainsImage()) return Clipboard.GetImage();
        }
        catch (Exception ex)
        {
            Log.Warn($"Lecture du presse-papiers : {ex.Message}");
        }
        return null;
    }

    private static (BitmapEncoder Encoder, string Extension) CreateEncoder(ClipboardImageFormat format, int quality)
    {
        switch (format)
        {
            case ClipboardImageFormat.Jpg:
                return (new JpegBitmapEncoder { QualityLevel = quality }, ".jpg");

            case ClipboardImageFormat.WebP:
                // Windows fournit un decodeur WebP, mais l'encodeur n'est pas toujours installe.
                try
                {
                    var webp = BitmapEncoder.Create(new Guid("e094b0e2-67f2-45b3-b0ea-115337ca7cf3"));
                    return (webp, ".webp");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Encodeur WebP indisponible sur ce systeme, repli sur PNG : {ex.Message}");
                    return (new PngBitmapEncoder(), ".png");
                }

            default:
                return (new PngBitmapEncoder(), ".png");
        }
    }

    private static string MakeUniquePath(string folder, string baseName, string extension)
    {
        var path = Path.Combine(folder, baseName + extension);
        var i = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, $"{baseName} ({i++}){extension}");
        return path;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }
}
