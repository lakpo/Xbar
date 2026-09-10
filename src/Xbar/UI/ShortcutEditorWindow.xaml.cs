using System.IO;
using System.Windows;
using Xbar.Shortcuts;
using Xbar.Localization;

namespace Xbar.UI;

/// <summary>Editeur d'un raccourci (application ou dossier).</summary>
public partial class ShortcutEditorWindow : Window
{
    private readonly ShortcutItem _item;

    private ShortcutEditorWindow(ShortcutItem item)
    {
        _item = item.Clone();
        InitializeComponent();

        NameBox.Text = _item.Name;
        TargetBox.Text = _item.Target;
        ArgumentsBox.Text = _item.Arguments;
        WorkCustomBox.Text = _item.CustomWorkingDirectory;
        AcceptDropsBox.IsChecked = _item.AcceptsDrops;

        switch (_item.WorkingDirectory)
        {
            case WorkingDirectoryMode.CurrentExplorerFolder: WorkCurrent.IsChecked = true; break;
            case WorkingDirectoryMode.Custom: WorkCustom.IsChecked = true; break;
            default: WorkDefault.IsChecked = true; break;
        }

        if (_item.Kind == ShortcutKind.Folder)
        {
            Title = Strings.Get("Editor_TitleFolder");
            TargetLabel.Text = Strings.Get("Editor_TargetFolder");
            ApplicationOptions.Visibility = Visibility.Collapsed;
        }
        else if (_item.Kind == ShortcutKind.File)
        {
            Title = Strings.Get("Editor_TitleFile");
            TargetLabel.Text = Strings.Get("Editor_TargetFile");
            ApplicationOptions.Visibility = Visibility.Collapsed;
        }
        else if (_item.Kind == ShortcutKind.ShellFolder)
        {
            // L'adresse ("shell:MyComputerFolder") ne veut rien dire pour qui la lit et ne se
            // choisit pas dans un explorateur de fichiers : seuls le nom et l'icone se modifient.
            Title = Strings.Get("Editor_TitleShellLocation");
            TargetLabel.Visibility = Visibility.Collapsed;
            TargetRow.Visibility = Visibility.Collapsed;
            ApplicationOptions.Visibility = Visibility.Collapsed;
        }
        else if (_item.Kind == ShortcutKind.Action)
        {
            Title = Strings.Get("Editor_TitleAction");
            TargetLabel.Visibility = Visibility.Collapsed;
            TargetRow.Visibility = Visibility.Collapsed;
            ApplicationOptions.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Ouvre l'editeur et enregistre les modifications si l'utilisateur valide.</summary>
    public static void Edit(ShortcutItem item)
    {
        if (item.IsSeparator) return;
        var window = new ShortcutEditorWindow(item);
        if (window.ShowDialog() == true) ShortcutStore.Replace(window._item);
    }

    private void OnBrowseTarget(object sender, RoutedEventArgs e)
    {
        if (_item.Kind == ShortcutKind.Folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.Get("Dialog_ChooseFolder") };
            if (dialog.ShowDialog() == true) TargetBox.Text = dialog.FolderName;
        }
        else
        {
            var estFichier = _item.Kind == ShortcutKind.File;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.Get(estFichier ? "Dialog_ChooseFile" : "Dialog_ChooseApplication"),
                Filter = estFichier
                    ? Strings.Get("Common_AllFiles")
                    : Strings.Get("Dialog_FilterApps") + "|" + Strings.Get("Common_AllFiles")
            };
            if (dialog.ShowDialog() == true) TargetBox.Text = dialog.FileName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var target = TargetBox.Text.Trim();

        // Un emplacement du Shell n'a pas de chemin a verifier sur le disque.
        if (_item.Kind is not (ShortcutKind.Action or ShortcutKind.ShellFolder))
        {
            var expanded = Environment.ExpandEnvironmentVariables(target);
            var exists = _item.Kind == ShortcutKind.Folder ? Directory.Exists(expanded) : File.Exists(expanded);
            if (!exists)
            {
                var message = Strings.Get(_item.Kind == ShortcutKind.Folder
                    ? "Editor_MissingFolder"
                    : "Editor_MissingFile");
                if (MessageBox.Show(this, message, "Xbar", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    != MessageBoxResult.Yes) return;
            }
        }

        _item.Name = NameBox.Text.Trim();
        _item.Target = target;
        _item.Arguments = ArgumentsBox.Text.Trim();
        _item.AcceptsDrops = AcceptDropsBox.IsChecked == true;
        _item.CustomWorkingDirectory = WorkCustomBox.Text.Trim();
        _item.WorkingDirectory =
            WorkCurrent.IsChecked == true ? WorkingDirectoryMode.CurrentExplorerFolder :
            WorkCustom.IsChecked == true ? WorkingDirectoryMode.Custom :
            WorkingDirectoryMode.Default;

        DialogResult = true;
    }
}
