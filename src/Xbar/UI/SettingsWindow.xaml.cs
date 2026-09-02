using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Xbar.Actions;
using Xbar.Core;
using Xbar.Icons;
using Xbar.Shortcuts;
using Xbar.Localization;

namespace Xbar.UI;

/// <summary>Fenetre de configuration. Les changements sont appliques immediatement.</summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private bool _loading = true;

    private sealed record ShortcutRow(ShortcutItem Item)
    {
        public string Display => Item.Kind switch
        {
            ShortcutKind.Separator => "──────────",
            ShortcutKind.Action => $"{Strings.Get("Settings_KindAction")} {(string.IsNullOrWhiteSpace(Item.Name) ? Item.ActionId : Item.Name)}",
            ShortcutKind.Folder => $"{Strings.Get("Settings_KindFolder")} {Item.Name}   {Item.Target}",
            ShortcutKind.File => $"{Strings.Get("Settings_KindFile")} {Item.Name}   {Item.Target}",
            _ => $"{Strings.Get("Settings_KindApp")} {Item.Name}   {Item.Target}"
        };
    }

    private SettingsWindow()
    {
        InitializeComponent();
        Icon = null;
        LoadValues();
        WireEvents();
        _loading = false;
        ShortcutStore.Changed += RefreshShortcuts;
        Closed += (_, _) =>
        {
            ShortcutStore.Changed -= RefreshShortcuts;
            _instance = null;
        };
    }

    /// <summary>Ouvre la fenetre, ou la ramene au premier plan si elle est deja ouverte.</summary>
    public static void ShowSingleton()
    {
        if (_instance is not null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }
        _instance = new SettingsWindow();
        _instance.Show();
    }

    // ------------------------------------------------------------- chargement

    private void LoadValues()
    {
        var s = SettingsStore.Current;

        LanguageBox.SelectedIndex = (int)s.Language;
        ThemeBox.SelectedIndex = (int)s.Theme;
        LabelBox.SelectedIndex = (int)s.Labels;
        AnchorBox.SelectedIndex = (int)s.Anchor;
        ButtonSizeSlider.Value = s.ButtonSize;
        OpacitySlider.Value = s.Opacity;
        EdgeMarginSlider.Value = s.EdgeMargin;
        AvoidScrollbarBox.IsChecked = s.AvoidScrollbar;

        AutoHideBox.IsChecked = s.AutoHide;
        DoubleClickBox.IsChecked = s.DoubleClickGoUp;
        MinWidthBox.Text = s.MinExplorerWidth.ToString(CultureInfo.InvariantCulture);
        MinHeightBox.Text = s.MinExplorerHeight.ToString(CultureInfo.InvariantCulture);

        ClipboardFormatBox.SelectedIndex = (int)s.ClipboardFormat;
        JpegQualityBox.Text = s.JpegQuality.ToString(CultureInfo.InvariantCulture);
        AskFileNameBox.IsChecked = s.ClipboardAskFileName;

        StartupBox.IsChecked = StartupManager.IsEnabled();
        UpdateBox.IsChecked = s.CheckForUpdates;
        VerboseBox.IsChecked = s.VerboseLogging;

        VersionText.Text = $"Xbar {AppInfo.Version}";
        ConfigPathText.Text = Strings.Format("Settings_ConfigPath", AppPaths.Root) +
                              (AppPaths.IsPortable ? Strings.Get("Settings_PortableSuffix") : "");

        UpdateValueLabels();
        RefreshShortcuts();
    }

    private void WireEvents()
    {
        LanguageBox.SelectionChanged += (_, _) => OnLanguageChanged();
        ThemeBox.SelectionChanged += (_, _) => Apply();
        LabelBox.SelectionChanged += (_, _) => Apply();
        AnchorBox.SelectionChanged += (_, _) => Apply();
        ClipboardFormatBox.SelectionChanged += (_, _) => Apply();

        ButtonSizeSlider.ValueChanged += (_, _) => { UpdateValueLabels(); Apply(); };
        OpacitySlider.ValueChanged += (_, _) => { UpdateValueLabels(); Apply(); };
        EdgeMarginSlider.ValueChanged += (_, _) => { UpdateValueLabels(); Apply(); };

        AvoidScrollbarBox.Click += (_, _) => Apply();
        AutoHideBox.Click += (_, _) => Apply();
        DoubleClickBox.Click += (_, _) => Apply();
        AskFileNameBox.Click += (_, _) => Apply();
        UpdateBox.Click += (_, _) => Apply();
        VerboseBox.Click += (_, _) => Apply();

        MinWidthBox.LostFocus += (_, _) => Apply();
        MinHeightBox.LostFocus += (_, _) => Apply();
        JpegQualityBox.LostFocus += (_, _) => Apply();

        StartupBox.Click += (_, _) => StartupManager.SetEnabled(StartupBox.IsChecked == true);
    }

    private void UpdateValueLabels()
    {
        ButtonSizeText.Text = $"{ButtonSizeSlider.Value:0} px";
        OpacityText.Text = $"{OpacitySlider.Value * 100:0} %";
        EdgeMarginText.Text = $"{EdgeMarginSlider.Value:0} px";
    }

    private void Apply()
    {
        if (_loading) return;

        SettingsStore.Update(s =>
        {
            s.Language = (AppLanguage)Math.Max(0, LanguageBox.SelectedIndex);
            s.Theme = (BarTheme)Math.Max(0, ThemeBox.SelectedIndex);
            s.Labels = (LabelMode)Math.Max(0, LabelBox.SelectedIndex);
            var anchor = (BarAnchor)Math.Max(0, AnchorBox.SelectedIndex);
            // Choisir un preset annule la position definie a la poignee.
            if (s.Anchor != anchor) s.VerticalRatio = null;
            s.Anchor = anchor;
            s.ButtonSize = ButtonSizeSlider.Value;
            s.Opacity = OpacitySlider.Value;
            s.EdgeMargin = EdgeMarginSlider.Value;
            s.AvoidScrollbar = AvoidScrollbarBox.IsChecked == true;

            s.AutoHide = AutoHideBox.IsChecked == true;
            s.DoubleClickGoUp = DoubleClickBox.IsChecked == true;
            s.MinExplorerWidth = ParseDouble(MinWidthBox.Text, s.MinExplorerWidth);
            s.MinExplorerHeight = ParseDouble(MinHeightBox.Text, s.MinExplorerHeight);

            s.ClipboardFormat = (ClipboardImageFormat)Math.Max(0, ClipboardFormatBox.SelectedIndex);
            s.JpegQuality = (int)ParseDouble(JpegQualityBox.Text, s.JpegQuality);
            s.ClipboardAskFileName = AskFileNameBox.IsChecked == true;

            s.CheckForUpdates = UpdateBox.IsChecked == true;
            s.VerboseLogging = VerboseBox.IsChecked == true;
        });

        ThemeManager.Apply();
    }

    /// <summary>
    /// Changer de langue demande de reconstruire la fenetre : le XAML resout ses textes une
    /// seule fois, a l'analyse. On la referme et on la rouvre aussitot.
    /// </summary>
    private void OnLanguageChanged()
    {
        if (_loading) return;

        var language = (AppLanguage)Math.Max(0, LanguageBox.SelectedIndex);
        if (language == SettingsStore.Current.Language) return;

        SettingsStore.Update(s => s.Language = language);
        Strings.Apply(language);

        Close();
        Dispatcher.BeginInvoke(ShowSingleton, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    // ------------------------------------------------------------- raccourcis

    private void RefreshShortcuts()
    {
        var selectedId = (ShortcutList.SelectedItem as ShortcutRow)?.Item.Id;
        ShortcutList.ItemsSource = ShortcutStore.Items.Select(i => new ShortcutRow(i)).ToList();
        if (selectedId is not null)
            ShortcutList.SelectedItem = ShortcutList.Items.Cast<ShortcutRow>()
                .FirstOrDefault(r => r.Item.Id == selectedId);
    }

    private ShortcutItem? Selected => (ShortcutList.SelectedItem as ShortcutRow)?.Item;

    private void OnAddApplication(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("Dialog_ChooseApplication"),
            Filter = Strings.Get("Dialog_FilterAppsAndLinks") + "|" + Strings.Get("Common_AllFiles")
        };
        if (dialog.ShowDialog() != true) return;

        var item = ShortcutLauncher.CreateFromDroppedPath(dialog.FileName);
        if (item is null)
        {
            MessageBox.Show(this, Strings.Get("Settings_UnsupportedType"), "Xbar");
            return;
        }
        ShortcutStore.Add(item);
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.Get("Dialog_ChooseFolder") };
        if (dialog.ShowDialog() != true) return;

        ShortcutStore.Add(new ShortcutItem
        {
            Kind = ShortcutKind.Folder,
            Name = System.IO.Path.GetFileName(dialog.FolderName.TrimEnd('\\')),
            Target = dialog.FolderName
        });
    }

    /// <summary>Propose la liste des actions integrees plutot que d'ajouter toujours la premiere.</summary>
    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        foreach (var action in ActionRegistry.All)
        {
            var chosen = action;
            var entry = new MenuItem { Header = chosen.Title };
            entry.Click += (_, _) => ShortcutStore.Add(new ShortcutItem
            {
                Kind = ShortcutKind.Action,
                ActionId = chosen.Id,
                Name = chosen.Title
            });
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    private void OnAddFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("Dialog_ChooseFile"),
            Filter = Strings.Get("Common_AllFiles")
        };
        if (dialog.ShowDialog() != true) return;

        ShortcutStore.Add(new ShortcutItem
        {
            Kind = ShortcutKind.File,
            Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            Target = dialog.FileName
        });
    }

    private void OnAddSeparator(object sender, RoutedEventArgs e) =>
        ShortcutStore.Add(new ShortcutItem { Kind = ShortcutKind.Separator, Name = Strings.Get("Common_Separator") });

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) ShortcutEditorWindow.Edit(item);
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var name = InputDialog.Ask(Strings.Get("Bar_Rename"), Strings.Get("Bar_NewName"), item.Name);
        if (name is null) return;
        var copy = item.Clone();
        copy.Name = name;
        ShortcutStore.Replace(copy);
    }

    private void OnChangeIcon(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Get("Dialog_ChooseIcon"),
            Filter = Strings.Get("Dialog_FilterImages") + "|" + Strings.Get("Common_AllFiles")
        };
        if (dialog.ShowDialog() != true) return;

        var stored = IconService.ImportIcon(dialog.FileName);
        if (stored is null) return;

        var copy = item.Clone();
        copy.IconOverride = stored;
        IconService.ClearCache();
        ShortcutStore.Replace(copy);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        if (Selected is not { } item) return;
        var index = ShortcutStore.IndexOf(item.Id);
        if (index < 0) return;
        ShortcutStore.Move(item.Id, index + delta);
        ShortcutList.SelectedItem = ShortcutList.Items.Cast<ShortcutRow>()
            .FirstOrDefault(r => r.Item.Id == item.Id);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) ShortcutStore.Remove(item.Id);
    }

    // ------------------------------------------------------------- general

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
    }

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Current;
        var sb = new StringBuilder();
        sb.AppendLine($"Xbar {AppInfo.Version} ({(AppPaths.IsPortable ? "portable" : "installé")})");
        sb.AppendLine($"Windows : {Environment.OSVersion.VersionString}");
        sb.AppendLine($".NET : {Environment.Version}");
        sb.AppendLine($"Configuration: {AppPaths.Root}");
        sb.AppendLine($"Theme={s.Theme} Labels={s.Labels} ButtonSize={s.ButtonSize} Opacity={s.Opacity}");
        sb.AppendLine($"AutoHide={s.AutoHide} DoubleClick={s.DoubleClickGoUp} Shortcuts={ShortcutStore.Items.Count}");
        sb.AppendLine();
        sb.AppendLine(Strings.Get("Settings_RecentLog"));
        sb.AppendLine(Log.GetRecent());

        try
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show(this, Strings.Get("Settings_DiagnosticsCopied"), "Xbar");
        }
        catch (Exception ex)
        {
            Log.Error("Copie du diagnostic", ex);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
