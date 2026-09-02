using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xbar.Localization;

namespace Xbar.UI;

/// <summary>Petite boite de saisie (renommage, nom de fichier).</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _input;

    private InputDialog(string title, string prompt, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _input = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 14), Padding = new Thickness(4) };
        _input.SelectAll();

        var ok = new Button { Content = Strings.Get("Common_Ok"), IsDefault = true, Style = FindStyle("DialogButtonStyle") };
        ok.Click += (_, _) => { DialogResult = true; };

        var cancel = new Button { Content = Strings.Get("Common_Cancel"), IsCancel = true, Style = FindStyle("DialogButtonStyle") };
        cancel.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_input);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    private static Style? FindStyle(string key) => Application.Current.Resources[key] as Style;

    /// <summary>Renvoie la valeur saisie, ou null si l'utilisateur annule.</summary>
    public static string? Ask(string title, string prompt, string initial = "")
    {
        var dialog = new InputDialog(title, prompt, initial);
        return dialog.ShowDialog() == true ? dialog._input.Text.Trim() : null;
    }
}
