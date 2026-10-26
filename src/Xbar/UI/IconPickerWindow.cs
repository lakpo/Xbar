using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xbar.Icons;
using Xbar.Localization;
using Xbar.Shortcuts;

namespace Xbar.UI;

/// <summary>
/// Choix d'une icone de la bibliotheque integree et de sa couleur. Le fond de la grille est
/// celui de la barre : on voit l'icone telle qu'elle y apparaitra, dans le theme courant.
/// </summary>
public sealed class IconPickerWindow : Window
{
    /// <summary>
    /// Nombre d'icones affichees a la fois. La bibliotheque en compte plus de 7 000 : les
    /// afficher toutes rendrait la fenetre lente a ouvrir, la recherche sert a affiner.
    /// </summary>
    private const int MaxResults = 240;
    private const double CellSize = 40;

    /// <summary>Teintes proposees d'un clic ; "Auto" (null) suit le theme de la barre.</summary>
    private static readonly string?[] Palette =
    [
        null, "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C", "#3498DB",
        "#2D6CDF", "#9B59B6", "#E84393", "#95A5A6", "#FFFFFF", "#000000"
    ];

    private readonly TextBox _search;
    private readonly WrapPanel _grid;
    private readonly TextBlock _status;
    private readonly TextBox _colorBox;
    private readonly StackPanel _swatches;
    private readonly Image _preview;
    private readonly TextBlock _previewName;
    private readonly Button _ok;
    private readonly DispatcherTimer _searchDelay;

    private string? _selected;
    private string? _color;

    private IconPickerWindow(string? initialName, string? initialColor)
    {
        Title = Strings.Get("IconPicker_Title");
        Width = 620;
        Height = 600;
        MinWidth = 460;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        _selected = initialName is not null && IconLibrary.Contains(initialName) ? initialName : null;
        _color = IconLibrary.ParseColor(initialColor) is null ? null : initialColor;

        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); RefreshGrid(); };

        // --- Recherche ---
        _search = new TextBox { Padding = new Thickness(4), Margin = new Thickness(0, 4, 0, 2) };
        _search.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Strings.Get("IconPicker_Search"), FontWeight = FontWeights.SemiBold });
        top.Children.Add(_search);
        _status = new TextBlock { Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(_status);

        // --- Grille ---
        _grid = new WrapPanel { Margin = new Thickness(6) };
        var gridHost = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = _grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        gridHost.SetResourceReference(Border.BackgroundProperty, "BarBackground");
        gridHost.SetResourceReference(Border.BorderBrushProperty, "BarBorder");

        // --- Couleur ---
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var color in Palette) _swatches.Children.Add(CreateSwatch(color));

        _colorBox = new TextBox
        {
            Width = 90,
            Padding = new Thickness(4),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Text = _color ?? "",
            ToolTip = Strings.Get("IconPicker_CustomColor")
        };
        _colorBox.LostFocus += (_, _) => OnCustomColor();
        _colorBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) OnCustomColor(); };
        _swatches.Children.Add(_colorBox);

        // --- Apercu ---
        _preview = new Image { Width = 32, Height = 32, Margin = new Thickness(8) };
        var previewHost = new Border { CornerRadius = new CornerRadius(8), Child = _preview, Margin = new Thickness(0, 0, 10, 0) };
        previewHost.SetResourceReference(Border.BackgroundProperty, "BarBackground");
        _previewName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        _ok = new Button { Content = Strings.Get("Common_Ok"), IsDefault = true, Style = FindStyle("DialogButtonStyle") };
        _ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = Strings.Get("Common_Cancel"), IsCancel = true, Style = FindStyle("DialogButtonStyle") };

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        DockPanel.SetDock(buttons, Dock.Right);
        DockPanel.SetDock(previewHost, Dock.Left);
        bottom.Children.Add(buttons);
        bottom.Children.Add(previewHost);
        bottom.Children.Add(_previewName);

        var colorSection = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        colorSection.Children.Add(new TextBlock { Text = Strings.Get("IconPicker_Color"), FontWeight = FontWeights.SemiBold });
        colorSection.Children.Add(_swatches);

        var credit = new TextBlock
        {
            Text = Strings.Get("IconPicker_Credit"),
            Opacity = 0.6,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(credit, Dock.Bottom);
        DockPanel.SetDock(colorSection, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(credit);
        root.Children.Add(colorSection);
        root.Children.Add(gridHost);
        Content = root;

        if (_selected is not null) _search.Text = _selected;
        Loaded += (_, _) =>
        {
            RefreshGrid();
            UpdatePreview();
            _search.Focus();
            _search.SelectAll();
        };
    }

    private static Style? FindStyle(string key) => Application.Current.Resources[key] as Style;

    /// <summary>
    /// Ouvre la bibliotheque pour un raccourci et enregistre l'icone choisie. Un raccourci qui
    /// en utilise deja une la retrouve preselectionnee, avec sa couleur.
    /// </summary>
    public static void ChooseFor(ShortcutItem item)
    {
        if (item.IsSeparator) return;

        var dialog = new IconPickerWindow(IconLibrary.NameOf(item.IconOverride), item.IconColor);
        if (dialog.ShowDialog() != true || dialog._selected is null) return;

        var copy = item.Clone();
        copy.IconOverride = IconLibrary.Prefix + dialog._selected;
        copy.IconColor = dialog._color;
        ShortcutStore.Replace(copy);
    }

    private Brush CurrentBrush() => IconLibrary.ParseColor(_color) ?? IconLibrary.ThemeBrush();

    private void RefreshGrid()
    {
        var brush = CurrentBrush();
        var matches = IconLibrary.Search(_search.Text).ToList();

        // L'icone choisie reste visible meme si la recherche l'ecarte : on voit ce qu'on valide.
        var shown = matches.Take(MaxResults).ToList();
        if (_selected is not null && !shown.Contains(_selected)) shown.Insert(0, _selected);

        _grid.Children.Clear();
        foreach (var name in shown) _grid.Children.Add(CreateCell(name, brush));

        _status.Text = matches.Count > MaxResults
            ? Strings.Format("IconPicker_ResultsTruncated", matches.Count, MaxResults)
            : Strings.Format("IconPicker_Results", matches.Count);
    }

    private ToggleButton CreateCell(string name, Brush brush)
    {
        var cell = new ToggleButton
        {
            Width = CellSize,
            Height = CellSize,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Tag = name,
            ToolTip = name,
            IsChecked = name == _selected,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(name == _selected ? 2 : 0),
            Content = new Image { Source = IconLibrary.Render(name, brush), Width = 24, Height = 24 }
        };
        cell.SetResourceReference(BorderBrushProperty, "BarForeground");
        cell.Click += (_, _) => Select(name);
        cell.MouseDoubleClick += (_, _) => { Select(name); DialogResult = true; };
        return cell;
    }

    private void Select(string name)
    {
        _selected = name;
        foreach (ToggleButton cell in _grid.Children)
        {
            var isSelected = (string)cell.Tag == name;
            cell.IsChecked = isSelected;
            cell.BorderThickness = new Thickness(isSelected ? 2 : 0);
        }
        UpdatePreview();
    }

    private UIElement CreateSwatch(string? color)
    {
        var swatch = new Button
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(0),
            ToolTip = color ?? Strings.Get("IconPicker_ColorAuto"),
            BorderThickness = new Thickness(1)
        };

        if (color is null)
        {
            // "Auto" : la couleur du texte de la barre, montree sur le fond de la barre.
            swatch.Content = new TextBlock { Text = "A", FontWeight = FontWeights.Bold, FontSize = 11 };
            swatch.SetResourceReference(BackgroundProperty, "BarBackground");
            swatch.SetResourceReference(ForegroundProperty, "BarForeground");
        }
        else swatch.Background = IconLibrary.ParseColor(color);

        swatch.Click += (_, _) => SetColor(color);
        return swatch;
    }

    private void OnCustomColor()
    {
        var text = _colorBox.Text.Trim();
        if (text.Length == 0) { SetColor(null); return; }
        if (!text.StartsWith('#')) text = "#" + text;

        if (IconLibrary.ParseColor(text) is null)
        {
            _colorBox.Text = _color ?? "";
            return;
        }
        SetColor(text.ToUpperInvariant());
    }

    private void SetColor(string? color)
    {
        _color = color;
        _colorBox.Text = color ?? "";
        RefreshGrid();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _ok.IsEnabled = _selected is not null;
        _preview.Source = _selected is null ? null : IconLibrary.Render(_selected, CurrentBrush());
        _previewName.Text = _selected ?? Strings.Get("IconPicker_NoSelection");
    }
}
