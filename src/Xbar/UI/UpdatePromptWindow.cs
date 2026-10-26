using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Xbar.Core;
using Xbar.Localization;

namespace Xbar.UI;

/// <summary>
/// Propose d'installer une nouvelle version. Xbar vit en arriere-plan : la fenetre passe au
/// premier plan, sans quoi elle risquerait de rester cachee derriere l'Explorateur.
/// </summary>
public sealed class UpdatePromptWindow : Window
{
    private UpdatePromptWindow(string version, string releaseUrl)
    {
        Title = Strings.Get("UpdatePrompt_Title");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = true;

        var message = new TextBlock
        {
            Text = Strings.Format("UpdatePrompt_Message", version, AppInfo.Version),
            TextWrapping = TextWrapping.Wrap
        };

        var notes = new Hyperlink(new Run(Strings.Get("UpdatePrompt_Notes")));
        notes.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn($"Ouverture de la page de version : {ex.Message}"); }
        };
        var notesLine = new TextBlock(notes) { Margin = new Thickness(0, 10, 0, 16) };

        var install = new Button
        {
            Content = Strings.Get("UpdatePrompt_Install"),
            IsDefault = true,
            Style = FindStyle("DialogButtonStyle")
        };
        install.Click += (_, _) => DialogResult = true;

        var later = new Button
        {
            Content = Strings.Get("UpdatePrompt_Later"),
            IsCancel = true,
            Style = FindStyle("DialogButtonStyle")
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(later);
        buttons.Children.Add(install);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(message);
        if (!string.IsNullOrWhiteSpace(releaseUrl)) panel.Children.Add(notesLine);
        else message.Margin = new Thickness(0, 0, 0, 16);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => Activate();
    }

    private static Style? FindStyle(string key) => Application.Current.Resources[key] as Style;

    /// <summary>True si l'utilisateur accepte d'installer maintenant.</summary>
    public static bool Ask(string version, string releaseUrl) =>
        new UpdatePromptWindow(version, releaseUrl).ShowDialog() == true;
}
