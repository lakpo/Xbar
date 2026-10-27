using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Xbar.Actions;
using Xbar.Core;
using Xbar.ExplorerIntegration;
using Xbar.Icons;
using Xbar.Interop;
using Xbar.Shortcuts;
using Xbar.Localization;

namespace Xbar.Overlay;

/// <summary>
/// Barre de raccourcis affichee en surimpression d'une fenetre de l'Explorateur.
///
/// La fenetre n'est jamais activee (WS_EX_NOACTIVATE) et n'apparait ni dans la barre des taches
/// ni dans Alt+Tab (WS_EX_TOOLWINDOW). Elle est declaree comme fenetre *possedee* par la fenetre
/// Explorer (GWLP_HWNDPARENT) : elle reste donc au-dessus de son Explorer sans jamais passer
/// au-dessus des autres applications.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double ItemSpacing = 3;
    /// <summary>Espace libre de part et d'autre du trait de separation.</summary>
    private const double SeparatorSpacing = 5;
    private const double CollapsedWidth = 8;
    private const double DragThreshold = 6;

    private readonly DispatcherTimer _collapseTimer;
    private IntPtr _handle;
    private bool _collapsed;
    private bool _labelsExpanded;
    private bool _placementValid;
    private (int X, int Y, int W, int H) _lastBounds;

    private Point _dragStart;
    private ShortcutItem? _dragItem;
    private bool _isDragging;
    private Border? _dropIndicator;
    private readonly List<TextBlock> _labelBlocks = new();
    private UIElement? _dropTargetVisual;

    /// <summary>Ecart entre l'etiquette d'un bouton et le bord gauche de la barre.</summary>
    private const double ItemLabelGap = 8;
    /// <summary>Distance parcourue par l'etiquette, depuis la barre vers la gauche.</summary>
    private const double ItemLabelSlideDistance = 18;
    private const int ItemLabelAnimationMs = 180;
    /// <summary>La disparition est le mouvement inverse de l'apparition, un peu plus lente.</summary>
    private const int ItemLabelHideMs = 250;
    /// <summary>Largeur maximale reservee aux etiquettes.</summary>
    private const double ItemLabelMaxWidth = 360;

    /// <summary>Largeur necessaire a la plus longue etiquette, calculee a la reconstruction.</summary>
    private double _widestItemLabel;

    /// <summary>La vue des fichiers affiche-t-elle une barre de defilement ? (verifie en differe)</summary>
    private bool _viewScrolls = true;

    /// <summary>
    /// Largeur laissee libre au bord droit par une vue qui y dessine quelque chose. La Galerie de
    /// Windows 11 y place sa reglette chronologique : mesuree a 51 DIP (etiquette d'annee de 31 px
    /// plus sa marge), soit trois fois une barre de defilement.
    /// </summary>
    private const double GalleryRailDip = 51;

    /// <summary>Bande reservee au bord droit par la vue courante, en DIP. 0 hors Galerie.</summary>
    private double _reservedRightDip;
    private readonly DispatcherTimer _scrollCheckTimer;

    /// <summary>Position verticale en cours de glissement, avant enregistrement.</summary>
    private double? _liveVerticalRatio;
    private int _gripStartCursorY;
    private int _gripStartCenterY;

    public IntPtr ExplorerHwnd { get; }

    /// <summary>Actions globales demandees depuis le menu contextuel de la barre.</summary>
    public event Action? SettingsRequested;
    public event Action? ReloadRequested;
    public event Action? LogsRequested;
    public event Action? QuitRequested;
    public event Action? AutoHideToggled;

    public OverlayWindow(IntPtr explorerHwnd)
    {
        ExplorerHwnd = explorerHwnd;
        InitializeComponent();

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); OnPointerLeftForAWhile(); };

        // Verification differee : inutile de solliciter UI Automation a chaque evenement de
        // redimensionnement, on attend que la geometrie se stabilise.
        _scrollCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _scrollCheckTimer.Tick += (_, _) => { _scrollCheckTimer.Stop(); _ = CheckScrollbarAsync(); };

        MouseEnter += OnBarMouseEnter;
        MouseLeave += OnBarMouseLeave;
        MouseRightButtonUp += OnBarRightClick;
        DragOver += OnDragOver;
        Drop += OnDrop;
        DragLeave += (_, _) => { RemoveDropIndicator(); SetDropTarget(null); };

        DragGrip.MouseLeftButtonDown += OnGripMouseDown;
        DragGrip.MouseMove += OnGripMouseMove;
        DragGrip.MouseLeftButtonUp += OnGripMouseUp;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;

        var ex = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~(long)NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        // Fenetre possedee par l'Explorer : reste au-dessus de lui, mais pas des autres applications.
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GWLP_HWNDPARENT, ExplorerHwnd);

        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);

        Rebuild();
        UpdatePlacement();
    }

    protected override void OnClosed(EventArgs e)
    {
        SetSwitchHotkey(false);
        base.OnClosed(e);
    }

    // ---------------------------------------------------------------- changement de barre

    private const int SwitchHotkeyId = 1;
    private bool _switchHotkeyRegistered;

    /// <summary>
    /// Tab fait passer a l'autre barre tant que la souris survole celle-ci. La barre n'a jamais
    /// le focus clavier (WS_EX_NOACTIVATE) : la touche est donc reservee par RegisterHotKey,
    /// le temps du survol seulement, puis rendue aussitot a l'Explorateur.
    /// </summary>
    private void SetSwitchHotkey(bool enabled)
    {
        if (_handle == IntPtr.Zero || enabled == _switchHotkeyRegistered) return;

        if (enabled)
        {
            _switchHotkeyRegistered = NativeMethods.RegisterHotKey(_handle, SwitchHotkeyId,
                NativeMethods.MOD_NOREPEAT, NativeMethods.VK_TAB);
            if (!_switchHotkeyRegistered) Log.Debug("Tab au survol de la barre : touche deja reservee");
        }
        else
        {
            NativeMethods.UnregisterHotKey(_handle, SwitchHotkeyId);
            _switchHotkeyRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == SwitchHotkeyId)
        {
            handled = true;
            // Securite : la souris a pu quitter la barre sans que MouseLeave soit recu.
            if (IsMouseOver) SwitchToNextBar();
            else SetSwitchHotkey(false);
        }
        return IntPtr.Zero;
    }

    private void SwitchToNextBar()
    {
        ShortcutStore.SwitchToNextProfile();
        ShowFeedback(Strings.Format("Bar_ProfileTooltip", ShortcutStore.Profile + 1), true);
    }

    // ---------------------------------------------------------------- contenu

    /// <summary>Reconstruit entierement le contenu de la barre depuis la configuration.</summary>
    public void Rebuild()
    {
        var settings = SettingsStore.Current;
        ItemsHost.Children.Clear();
        _labelBlocks.Clear();
        _dropTargetVisual = null;
        Opacity = settings.Opacity;

        foreach (var item in ShortcutStore.Items)
            ItemsHost.Children.Add(item.IsSeparator ? CreateSeparator() : CreateButton(item, settings));

        _widestItemLabel = settings.Labels == LabelMode.LabelOnItemHover
            ? MeasureWidestLabel(settings)
            : 0;
        HideItemLabel(animated: false);

        ItemsHost.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        DragGrip.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        BarRoot.BeginAnimation(WidthProperty, null);
        BarRoot.Width = TargetBarWidth();
        InvalidateBarMeasure();
        UpdatePlacement();
    }

    /// <summary>
    /// Changer les boutons ne declare a remesurer que ItemsHost : ses parents gardent leur mesure
    /// precedente, et BarRoot.Measure, rappele avec la meme contrainte, renverrait l'ancienne
    /// hauteur. La fenetre garderait alors la taille de l'ancienne liste (boutons coupes, ou vide
    /// en bas), par exemple en passant d'une barre a l'autre. On invalide donc toute la chaine.
    /// </summary>
    private void InvalidateBarMeasure()
    {
        for (UIElement? element = ItemsHost;
             element is not null && !ReferenceEquals(element, this);
             element = VisualTreeHelper.GetParent(element) as UIElement)
            element.InvalidateMeasure();
    }

    /// <summary>
    /// Trait de separation. Le Border fait exactement la hauteur du trait, qui est peint par son
    /// fond : dessiner le trait sur le bord superieur d'un Border plus haut laisserait un vide
    /// dessous, et l'espacement paraitrait asymetrique.
    /// </summary>
    private static UIElement CreateSeparator()
    {
        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(4, SeparatorSpacing, 4, SeparatorSpacing),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true
        };
        // Reference vivante et non copie du pinceau : la couleur suit un changement de theme
        // sans qu'il faille reconstruire la barre.
        separator.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
        return separator;
    }

    private UIElement CreateButton(ShortcutItem item, AppSettings settings)
    {
        var size = settings.ButtonSize;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        content.Children.Add(CreateItemVisual(item, size, IconPixelSize(size)));

        if (ShowsLabels(settings))
        {
            var label = new TextBlock
            {
                Text = GetDisplayName(item),
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Max(11, size * 0.38)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BarForeground");
            _labelBlocks.Add(label);
            content.Children.Add(label);
        }

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BarItemButtonStyle"],
            Content = content,
            Height = size + 6,
            Margin = new Thickness(0, ItemSpacing / 2, 0, ItemSpacing / 2),
            // Marge constante : l'icone occupe la meme position avec ou sans texte, donc
            // elle ne saute pas quand les libelles sont ajoutes ou retires.
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(1, 0, 0, 0),
            Tag = item,
            // L'etiquette au survol dit deja le nom : une infobulle par-dessus ferait doublon.
            ToolTip = settings.Labels == LabelMode.LabelOnItemHover ? null : BuildToolTip(item),
            AllowDrop = true
        };

        // L'activation passe par l'evenement Click du Button : c'est WPF qui gere alors la
        // capture de la souris. Intercepter PreviewMouseUp en marquant l'evenement traite
        // empecherait le Button de relacher sa capture, et tous les clics suivants lui
        // seraient reachemines.
        if (settings.Labels == LabelMode.LabelOnItemHover)
        {
            button.MouseEnter += (sender, _) => ShowItemLabel((Button)sender, item);
            button.MouseLeave += (_, _) => HideItemLabel(animated: true);
        }

        button.Click += OnItemClick;
        button.PreviewMouseDown += OnItemMouseDown;
        button.PreviewMouseMove += OnItemMouseMove;
        button.PreviewMouseUp += OnItemPreviewMouseUp;
        button.MouseRightButtonUp += OnItemRightClick;

        return button;
    }

    /// <summary>Taille de rendu reelle d'une icone, en pixels ecran (DPI compris).</summary>
    private int IconPixelSize(double sizeDip)
    {
        var dpi = NativeMethods.GetDpiForWindow(ExplorerHwnd);
        if (dpi == 0) dpi = 96;
        return (int)Math.Round(sizeDip * dpi / 96.0);
    }

    private static UIElement CreateItemVisual(ShortcutItem item, double size, int pixelSize)
    {
        // Icone de la bibliotheque en couleur automatique : la forme sert de masque a un fond
        // lie a la ressource du theme, si bien qu'elle suit un changement de theme sans attendre
        // la reconstruction de la barre.
        if (IconLibrary.NameOf(item.IconOverride) is { } libraryName
            && IconLibrary.ParseColor(item.IconColor) is null
            && IconLibrary.Render(libraryName, Brushes.Black) is { } mask)
        {
            var shape = new System.Windows.Shapes.Rectangle
            {
                Width = size,
                Height = size,
                OpacityMask = new ImageBrush(mask) { Stretch = Stretch.Uniform }
            };
            shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BarForeground");
            return shape;
        }

        if (item.Kind == ShortcutKind.Action)
        {
            var action = ActionRegistry.Find(item.ActionId);
            var custom = IconService.GetIcon(item, pixelSize);
            if (custom is not null)
                return new Image { Source = custom, Width = size, Height = size };

            var glyph = new TextBlock
            {
                Text = action?.Glyph ?? "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = size * 0.55,
                Width = size,
                Height = size,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, size * 0.18, 0, 0)
            };
            if (IconLibrary.ParseColor(item.IconColor) is { } color) glyph.Foreground = color;
            else glyph.SetResourceReference(TextBlock.ForegroundProperty, "BarForeground");
            return glyph;
        }

        var icon = IconService.GetIcon(item, pixelSize);
        if (icon is not null)
            return new Image
            {
                Source = icon,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };

        // Cible introuvable : glyphe d'avertissement plutot qu'un bouton vide.
        var warning = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size * 0.5,
            Width = size,
            Height = size,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, size * 0.2, 0, 0)
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "BarForeground");
        return warning;
    }

    private static string GetDisplayName(ShortcutItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Name)) return item.Name;
        if (item.Kind == ShortcutKind.Action) return ActionRegistry.Find(item.ActionId)?.Title ?? "Action";
        return Path.GetFileNameWithoutExtension(item.Target);
    }

    private static string BuildToolTip(ShortcutItem item)
    {
        var name = GetDisplayName(item);
        return item.Kind switch
        {
            ShortcutKind.Folder => Strings.Format("Bar_FolderTooltip", name),
            ShortcutKind.Application => string.IsNullOrWhiteSpace(item.Arguments) ? name : $"{name}\n{item.Arguments}",
            ShortcutKind.File => Strings.Format("Bar_FileTooltip", name),
            _ => name
        };
    }

    /// <summary>Largeur de la plus longue etiquette, pour dimensionner la fenetre une fois pour toutes.</summary>
    private double MeasureWidestLabel(AppSettings settings)
    {
        var fontSize = ItemLabelFontSize(settings);
        double widest = 0;

        foreach (var item in ShortcutStore.Items)
        {
            if (item.IsSeparator) continue;

            var probe = new TextBlock { Text = GetDisplayName(item), FontSize = fontSize };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }

        // Les marges internes de l'etiquette, plus une garde pour la bordure et l'ombre.
        return Math.Min(widest + 30, ItemLabelMaxWidth);
    }

    private static double ItemLabelFontSize(AppSettings settings) => Math.Max(11, settings.ButtonSize * 0.42);

    /// <summary>
    /// Fait sortir le nom du bouton survole, centre sur son icone. L'etiquette emerge de la
    /// barre et glisse de droite a gauche, en apparaissant progressivement.
    /// </summary>
    private void ShowItemLabel(Button button, ShortcutItem item)
    {
        var settings = SettingsStore.Current;
        if (settings.Labels != LabelMode.LabelOnItemHover || _collapsed) return;

        ItemLabelText.Text = GetDisplayName(item);
        ItemLabelText.FontSize = ItemLabelFontSize(settings);
        ItemLabel.MaxWidth = ItemLabelMaxWidth;

        // Le conteneur occupe exactement la bande verticale du bouton, et l'etiquette y est
        // centree par WPF. On evite ainsi de mesurer sa hauteur nous-memes : un Measure() force
        // sur un element deja present dans l'arbre visuel renvoie un DesiredSize incoherent,
        // ce qui decalait l'etiquette d'une valeur variable.
        var buttonTop = button.TranslatePoint(new Point(0, 0), this).Y;
        ItemLabelHost.Height = button.ActualHeight;
        ItemLabelHost.Margin = new Thickness(0, Math.Max(0, buttonTop),
            settings.CompactBarWidth + ItemLabelGap, 0);
        ItemLabelHost.Visibility = Visibility.Visible;

        // L'etiquette emerge de la barre et file vers la gauche.
        var slide = new DoubleAnimation(ItemLabelSlideDistance, 0,
            TimeSpan.FromMilliseconds(ItemLabelAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ItemLabelSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        ItemLabel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ItemLabelAnimationMs)));
    }

    private void HideItemLabel(bool animated)
    {
        if (ItemLabelHost.Visibility != Visibility.Visible) return;

        if (!animated)
        {
            ItemLabel.BeginAnimation(OpacityProperty, null);
            ItemLabelSlide.BeginAnimation(TranslateTransform.XProperty, null);
            ItemLabelHost.Visibility = Visibility.Collapsed;
            return;
        }

        // Mouvement inverse de l'apparition : l'etiquette repart vers la droite, dans la barre,
        // en s'effacant. L'acceleration (EaseIn) repond a la deceleration de l'entree.
        var duration = TimeSpan.FromMilliseconds(ItemLabelHideMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        ItemLabelSlide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(ItemLabelSlideDistance, duration) { EasingFunction = ease });

        var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            // Un autre bouton a pu reprendre la main entre-temps : ne pas masquer dans ce cas.
            if (ItemLabel.Opacity <= 0.01) ItemLabelHost.Visibility = Visibility.Collapsed;
        };
        ItemLabel.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- placement

    /// <summary>Repositionne la barre a l'interieur du bord droit de la fenetre Explorer.</summary>
    public void UpdatePlacement()
    {
        if (_handle == IntPtr.Zero) return;

        var settings = SettingsStore.Current;

        if (!settings.BarEnabled
            || !NativeMethods.IsWindow(ExplorerHwnd)
            || !NativeMethods.IsWindowVisible(ExplorerHwnd)
            || NativeMethods.IsIconic(ExplorerHwnd))
        {
            HideBar();
            return;
        }

        if (!NativeMethods.GetClientRect(ExplorerHwnd, out var client)) { HideBar(); return; }

        var origin = new POINT(0, 0);
        NativeMethods.ClientToScreen(ExplorerHwnd, ref origin);

        var dpi = NativeMethods.GetDpiForWindow(ExplorerHwnd);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;

        // Fenetre trop petite : la barre gene plus qu'elle n'aide.
        if (client.Width < settings.MinExplorerWidth * scale ||
            client.Height < settings.MinExplorerHeight * scale)
        {
            HideBar();
            return;
        }

        var widthDip = WindowWidthDip(settings);
        BarRoot.Measure(new Size(widthDip, double.PositiveInfinity));
        var heightDip = Math.Max(BarRoot.DesiredSize.Height, settings.ButtonSize);

        var widthPx = (int)Math.Round(widthDip * scale);
        var heightPx = (int)Math.Round(heightDip * scale);

        var maxHeight = client.Height - (int)(8 * scale);
        if (heightPx > maxHeight) heightPx = maxHeight;

        // Ecart au bord droit. La marge et l'evitement de la barre de defilement ne s'ajoutent
        // pas : on retient le plus grand des deux. Une marge deja superieure a la largeur d'une
        // scrollbar degage donc celle-ci d'elle-meme, et le decalage ne s'applique que lorsque
        // la marge choisie ne suffit pas. Sans barre de defilement affichee, la marge vaut seule,
        // et une marge de 0 colle bien au bord.
        var scrollbar = settings.AvoidScrollbar && _viewScrolls
            ? NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXVSCROLL, dpi)
            : 0;
        var reserved = (int)Math.Round(_reservedRightDip * scale);
        var edgeOffset = Math.Max((int)Math.Round(settings.EdgeMargin * scale),
                                  Math.Max(scrollbar, reserved));

        var x = origin.X + client.Width - edgeOffset - widthPx;
        var margin = (int)Math.Round(12 * scale);
        var ratio = _liveVerticalRatio ?? settings.VerticalRatio;

        var y = ratio is { } r
            // Position libre : le ratio designe le centre de la barre dans la zone client.
            ? origin.Y + (int)Math.Round(r * client.Height) - heightPx / 2
            : settings.Anchor switch
            {
                BarAnchor.Top => origin.Y + margin,
                BarAnchor.Bottom => origin.Y + client.Height - heightPx - margin,
                _ => origin.Y + (client.Height - heightPx) / 2
            };

        // La barre doit rester entierement dans la fenetre, quelle que soit sa taille.
        var minY = origin.Y + margin;
        var maxY = origin.Y + client.Height - heightPx - margin;
        if (maxY >= minY) y = Math.Clamp(y, minY, maxY);

        // La plupart des evenements Windows ne changent pas la geometrie : on evite alors
        // tout SetWindowPos inutile (moins de scintillement, et les popups ouverts ne sont
        // pas perturbes par un redimensionnement).
        var bounds = (x, y, widthPx, heightPx);
        if (_placementValid && bounds == _lastBounds) return;

        // SWP_NOZORDER est essentiel : la barre est une fenetre *possedee*, Windows la maintient
        // donc deja au-dessus de son Explorer. Demander HWND_TOP la ferait remonter au sommet
        // global et passer devant les autres applications a chaque repositionnement.
        // SWP_NOCOPYBITS evite les residus d'image quand la largeur change a chaque image
        // pendant l'animation.
        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, x, y, widthPx, heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER |
            NativeMethods.SWP_NOCOPYBITS);

        Log.Debug($"Placement barre {ExplorerHwnd:X} : {x},{y} {widthPx}x{heightPx} dpi={dpi}");
        _lastBounds = bounds;
        ScheduleScrollbarCheck(); // un redimensionnement peut faire apparaitre ou disparaitre la barre
        _placementValid = true;
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
    }

    private void HideBar()
    {
        if (_handle == IntPtr.Zero) return;
        if (!_placementValid && Visibility != Visibility.Visible) return;
        _placementValid = false;
        SetSwitchHotkey(false);
        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_HIDEWINDOW | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER);
    }

    // ---------------------------------------------------------------- auto-hide

    public void ApplyAutoHide()
    {
        if (SettingsStore.Current.AutoHide) Collapse();
        else Expand();
    }

    private void Collapse()
    {
        if (!SettingsStore.Current.AutoHide || _collapsed) return;
        _collapsed = true;
        AnimateBarWidthTo(TargetBarWidth(),
            () => ItemsHost.Visibility = Visibility.Collapsed);
    }

    private void Expand()
    {
        if (!_collapsed) return;
        _collapsed = false;
        ItemsHost.Visibility = Visibility.Visible;
        AnimateBarWidthTo(TargetBarWidth(), null);
    }

    private const int WidthAnimationMs = 200;

    /// <summary>
    /// Largeur du HWND. Elle ne depend que du mode d'affichage, jamais de l'etat deplie ou
    /// replie : on reserve d'emblee la place maximale dont la barre aura besoin. La fenetre
    /// n'est donc pas redimensionnee pendant l'animation, ce qui evite le decalage d'une image
    /// entre la taille reelle de la fenetre et la mise en page WPF (source de tremblement sur
    /// une fenetre en transparence).
    /// </summary>
    private double WindowWidthDip(AppSettings settings) => settings.Labels switch
    {
        // La barre reste compacte, mais la fenetre doit contenir l'etiquette qui sort a gauche.
        LabelMode.LabelOnItemHover =>
            settings.CompactBarWidth + ItemLabelGap + _widestItemLabel,
        LabelMode.IconsOnly => settings.CompactBarWidth,
        _ => settings.ExpandedBarWidth
    };

    /// <summary>Largeur visible du panneau dans l'etat courant.</summary>
    private double TargetBarWidth()
    {
        if (_collapsed) return CollapsedWidth;
        var settings = SettingsStore.Current;
        return ShowsLabels(settings) ? settings.ExpandedBarWidth : settings.CompactBarWidth;
    }

    /// <summary>
    /// Fait glisser la largeur du panneau. L'animation est purement WPF : aucun appel a
    /// SetWindowPos n'a lieu pendant sa duree.
    /// </summary>
    private void AnimateBarWidthTo(double target, Action? onCompleted)
    {
        var from = double.IsNaN(BarRoot.Width) ? target : BarRoot.Width;
        if (Math.Abs(from - target) < 0.5)
        {
            onCompleted?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(WidthAnimationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            BarRoot.BeginAnimation(WidthProperty, null);
            BarRoot.Width = target;
            onCompleted?.Invoke();
        };

        BarRoot.BeginAnimation(WidthProperty, animation);
    }

    private void OnBarMouseEnter(object sender, MouseEventArgs e)
    {
        SetSwitchHotkey(true);
        _collapseTimer.Stop();
        Expand();
        SetLabelsExpanded(true);
    }

    private void OnBarMouseLeave(object sender, MouseEventArgs e)
    {
        SetSwitchHotkey(false);
        HideItemLabel(animated: true);

        if (SettingsStore.Current.AutoHide ||
            SettingsStore.Current.Labels == LabelMode.IconAndTextOnHover)
            _collapseTimer.Start();
    }

    /// <summary>La souris a quitte la barre depuis un instant : on replie ce qui doit l'etre.</summary>
    private void OnPointerLeftForAWhile()
    {
        if (SettingsStore.Current.AutoHide)
        {
            // La barre se reduit de toute facon a une pastille : inutile d'animer les libelles.
            _labelsExpanded = false;
            Collapse();
            Rebuild();
            return;
        }

        SetLabelsExpanded(false);
    }

    /// <summary>Le texte des boutons est-il visible dans l'etat courant ?</summary>
    private bool ShowsLabels(AppSettings settings) => settings.Labels switch
    {
        LabelMode.IconAndText => true,
        LabelMode.IconAndTextOnHover => _labelsExpanded,
        _ => false // LabelOnItemHover affiche son texte a cote du bouton, pas dedans
    };

    /// <summary>
    /// Deploie ou replie le texte des boutons (mode « texte au survol » uniquement), en faisant
    /// glisser la largeur de la barre sur 200 ms.
    ///
    /// A l'ouverture, le texte est mis en place avant l'animation : il se decouvre au fur et a
    /// mesure de l'elargissement. A la fermeture, il n'est retire qu'une fois la barre retrecie.
    /// </summary>
    private void SetLabelsExpanded(bool expanded)
    {
        if (SettingsStore.Current.Labels != LabelMode.IconAndTextOnHover) return;
        if (_labelsExpanded == expanded) return;

        _labelsExpanded = expanded;

        if (expanded)
        {
            var from = double.IsNaN(BarRoot.Width) ? TargetBarWidth() : BarRoot.Width;
            Rebuild();               // remet le texte en place...
            BarRoot.Width = from;    // ...sans laisser la largeur sauter a sa valeur finale
            AnimateBarWidthTo(TargetBarWidth(), null);
        }
        else
        {
            // Le texte s'efface plus vite que la barre ne retrecit : il ne reste pas visible
            // jusqu'a etre brutalement coupe par le bord de la fenetre.
            foreach (var label in _labelBlocks)
                label.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120)));

            AnimateBarWidthTo(TargetBarWidth(), Rebuild);
        }
    }

    /// <summary>Le dossier affiche a change : la presence d'une barre de defilement aussi, peut-etre.</summary>
    public void OnExplorerFolderChanged() => ScheduleScrollbarCheck();

    private void ScheduleScrollbarCheck()
    {
        // Sans le reglage "eviter la barre de defilement", il reste la reglette de la Galerie a
        // mesurer : la verification n'est donc plus conditionnee a ce reglage.
        _scrollCheckTimer.Stop();
        _scrollCheckTimer.Start();
    }

    private async Task CheckScrollbarAsync()
    {
        var hwnd = ExplorerHwnd;
        var avoidScrollbar = SettingsStore.Current.AvoidScrollbar;

        bool scrolls;
        double reserved;
        try
        {
            // UI Automation supporte le pool de threads ; l'enumeration ShellWindows non. Depuis
            // un thread MTA elle echoue en silence et rend toujours "pas la Galerie" : ce test
            // doit rester sur le thread d'interface, en STA.
            scrolls = avoidScrollbar &&
                await Task.Run(() => ExplorerWindows.ViewHasVerticalScrollbar(hwnd)).ConfigureAwait(true);
            reserved = ExplorerNavigator.IsGalleryView(hwnd) ? GalleryRailDip : 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"Verification du bord droit de la vue : {ex.Message}");
            return;
        }

        if (scrolls == _viewScrolls && Math.Abs(reserved - _reservedRightDip) < 0.5) return;

        _viewScrolls = scrolls;
        _reservedRightDip = reserved;
        Log.Debug($"Bord droit pour {hwnd:X} : defilement={scrolls} reserve={reserved:0} DIP");
        UpdatePlacement();
    }

    // ---------------------------------------------------------------- poignee de position

    /// <summary>
    /// Glissement vertical de la barre. On raisonne en coordonnees ecran (le curseur ne bouge
    /// pas avec la fenetre) et on enregistre une fraction de la hauteur de la fenetre Explorer,
    /// pour que la position tienne quelle que soit la taille de celle-ci.
    /// </summary>
    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;

        _gripStartCursorY = cursor.Y;
        _gripStartCenterY = _lastBounds.Y + _lastBounds.H / 2;
        _liveVerticalRatio = CurrentVerticalRatio();

        DragGrip.CaptureMouse();
        e.Handled = true;
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!DragGrip.IsMouseCaptured) return;
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        if (!NativeMethods.GetClientRect(ExplorerHwnd, out var client) || client.Height <= 0) return;

        var origin = new POINT(0, 0);
        NativeMethods.ClientToScreen(ExplorerHwnd, ref origin);

        var centerY = _gripStartCenterY + (cursor.Y - _gripStartCursorY);
        _liveVerticalRatio = Math.Clamp((centerY - origin.Y) / (double)client.Height, 0, 1);

        UpdatePlacement();
        e.Handled = true;
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!DragGrip.IsMouseCaptured) return;
        DragGrip.ReleaseMouseCapture();
        e.Handled = true;

        var ratio = _liveVerticalRatio;
        _liveVerticalRatio = null;
        if (ratio is null) return;

        // Enregistrement en fin de geste seulement : pas d'ecriture disque a chaque pixel.
        SettingsStore.Update(s => s.VerticalRatio = ratio);
        Log.Debug($"Position verticale de la barre : {ratio:P0} de la hauteur");
    }

    /// <summary>Position verticale actuelle, exprimee en fraction de la hauteur de la fenetre.</summary>
    private double CurrentVerticalRatio()
    {
        if (SettingsStore.Current.VerticalRatio is { } stored) return stored;
        if (!NativeMethods.GetClientRect(ExplorerHwnd, out var client) || client.Height <= 0) return 0.5;

        var origin = new POINT(0, 0);
        NativeMethods.ClientToScreen(ExplorerHwnd, ref origin);
        return Math.Clamp((_lastBounds.Y + _lastBounds.H / 2.0 - origin.Y) / client.Height, 0, 1);
    }

    // ---------------------------------------------------------------- interactions

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ShortcutItem item) return;
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragStart = e.GetPosition(this);
            _dragItem = item;
            _isDragging = false;
        }
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.Y - _dragStart.Y) < DragThreshold && Math.Abs(p.X - _dragStart.X) < DragThreshold) return;

        _isDragging = true;
        try
        {
            var data = new DataObject(XbarDragFormat, _dragItem.Id);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        catch (Exception ex) { Log.Warn($"Reorganisation par glisser-deposer : {ex.Message}"); }
        finally
        {
            _isDragging = false;
            _dragItem = null;
            RemoveDropIndicator();
        }
    }

    /// <summary>Clic gauche (Ctrl+clic = nouvelle fenetre pour un dossier).</summary>
    private async void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutItem item }) return;
        if (_isDragging) { _isDragging = false; return; }

        var mode = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? ActivationMode.NewWindow
            : ActivationMode.Default;

        await ActivateAsync(item, mode, null);
    }

    /// <summary>
    /// Clic molette uniquement. Le clic gauche n'est volontairement pas traite ici : le laisser
    /// suivre son cours permet au Button de relacher sa capture de souris.
    /// </summary>
    private async void OnItemPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutItem item }) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            _dragItem = null;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle) return;

        e.Handled = true;
        _dragItem = null;
        await ActivateAsync(item, ActivationMode.NewTab, null);
    }

    private async Task ActivateAsync(ShortcutItem item, ActivationMode mode, IReadOnlyList<string>? dropped)
    {
        try
        {
            await ShortcutLauncher.ActivateAsync(item, ExplorerHwnd, mode, dropped,
                ShowFeedback, ShowActionMenu);
        }
        catch (Exception ex)
        {
            Log.Error($"Activation du raccourci {item.Name}", ex);
            ShowFeedback(Strings.Get("Msg_Error"), false);
        }
    }

    private void ShowFeedback(string message, bool success) =>
        Dispatcher.Invoke(() => ToastWindow.Show(this, message, success));

    /// <summary>Menu ouvert par une action integree (par exemple les dossiers recents).</summary>
    private void ShowActionMenu(IReadOnlyList<ActionMenuEntry> entries)
    {
        var menu = new ContextMenu();
        foreach (var entry in entries)
        {
            if (entry.SeparatorBefore) menu.Items.Add(new Separator());

            var mi = new MenuItem
            {
                Header = entry.Header,
                IsEnabled = entry.Enabled,
                ToolTip = entry.ToolTip
            };
            var invoke = entry.Invoke;
            mi.Click += (_, _) => invoke();
            menu.Items.Add(mi);
        }
        ShowMenu(menu);
    }

    // ---------------------------------------------------------------- glisser-deposer

    public const string XbarDragFormat = "Xbar.ShortcutId";

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(XbarDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            ShowDropIndicator(GetInsertIndex(e.GetPosition(ItemsHost)));
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var button = HitTestButton(e.GetPosition(this));
            var overItem = button?.Tag as ShortcutItem;

            if (overItem is { Kind: ShortcutKind.Folder })
            {
                // Deposer dans le dossier : deplacement ou copie, comme dans l'Explorateur.
                e.Effects = ChooseFileEffect(e, overItem.Target);
                var hintKey = e.Effects == DragDropEffects.Copy ? "Bar_DropCopyTo" : "Bar_DropMoveTo";
                RemoveDropIndicator();
                SetDropTarget(button, Strings.Format(hintKey, GetDisplayName(overItem)));
            }
            else if (overItem is { AcceptsDrops: true, Kind: ShortcutKind.Application })
            {
                e.Effects = DragDropEffects.Copy;
                RemoveDropIndicator();
                SetDropTarget(button, Strings.Format("Bar_DropOpenWith", GetDisplayName(overItem)));
            }
            else
            {
                // Aucun bouton vise : le depot ajoutera un raccourci a cet emplacement.
                e.Effects = DragDropEffects.Copy;
                SetDropTarget(null);
                ShowDropIndicator(GetInsertIndex(e.GetPosition(ItemsHost)));
            }
        }
        else e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var insertIndex = GetInsertIndex(e.GetPosition(ItemsHost));
        RemoveDropIndicator();
        SetDropTarget(null);

        if (e.Data.GetDataPresent(XbarDragFormat))
        {
            if (e.Data.GetData(XbarDragFormat) is string id)
            {
                var from = ShortcutStore.IndexOf(id);
                if (from >= 0 && insertIndex > from) insertIndex--;
                ShortcutStore.Move(id, insertIndex);
            }
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        e.Handled = true;

        var overItem = HitTestItem(e.GetPosition(this));

        if (overItem is { Kind: ShortcutKind.Folder })
        {
            var copy = ChooseFileEffect(e, overItem.Target) == DragDropEffects.Copy;
            var target = Environment.ExpandEnvironmentVariables(overItem.Target);
            if (ExplorerNavigator.DropInto(target, paths, copy))
                ShowFeedback(Strings.Format(copy ? "Msg_Copied" : "Msg_Moved", System.IO.Path.GetFileName(target)), true);
            else
                ShowFeedback(Strings.Get("Msg_DropFailed"), false);
            return;
        }

        if (overItem is { AcceptsDrops: true, Kind: ShortcutKind.Application })
        {
            await ActivateAsync(overItem, ActivationMode.Default, paths);
            return;
        }

        var added = 0;
        foreach (var path in paths)
        {
            var item = ShortcutLauncher.CreateFromDroppedPath(path);
            if (item is null)
            {
                Log.Info($"Element ignore (type non pris en charge en V1) : {path}");
                continue;
            }
            ShortcutStore.Add(item, insertIndex + added);
            added++;
        }

        if (added == 0) ShowFeedback(Strings.Get("Msg_UnsupportedDrop"), false);
    }

    private Button? HitTestButton(Point positionInWindow)
    {
        var hit = InputHitTest(positionInWindow) as DependencyObject;
        while (hit is not null)
        {
            if (hit is Button { Tag: ShortcutItem } button) return button;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private ShortcutItem? HitTestItem(Point positionInWindow) =>
        HitTestButton(positionInWindow)?.Tag as ShortcutItem;

    private int GetInsertIndex(Point positionInHost)
    {
        var index = 0;
        foreach (UIElement child in ItemsHost.Children)
        {
            if (ReferenceEquals(child, _dropIndicator)) continue;
            var top = child.TranslatePoint(new Point(0, 0), ItemsHost).Y;
            var height = child.RenderSize.Height;
            if (positionInHost.Y < top + height / 2) return index;
            index++;
        }
        return index;
    }

    /// <summary>
    /// Deplacement ou copie ? On reproduit la convention de l'Explorateur : Ctrl force la copie,
    /// Maj force le deplacement, et par defaut on deplace au sein d'un meme volume, on copie
    /// d'un volume a l'autre.
    /// </summary>
    private static DragDropEffects ChooseFileEffect(DragEventArgs e, string destination)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DragDropEffects.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)) return DragDropEffects.Move;

        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                var from = System.IO.Path.GetPathRoot(paths[0]);
                var to = System.IO.Path.GetPathRoot(Environment.ExpandEnvironmentVariables(destination));
                if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                    return DragDropEffects.Copy;
            }
        }
        catch { /* volume indeterminable : on garde le deplacement */ }

        return DragDropEffects.Move;
    }

    /// <summary>Agrandissement de l'icone du bouton vise pendant un glisser.</summary>
    private const double DropTargetScale = 1.2;

    /// <summary>
    /// Met en avant le bouton vise pendant un glisser : son icone grandit, et une etiquette
    /// annonce ce que fera le depot. Sans ambiguite sur la cible ni sur l'effet.
    /// </summary>
    private void SetDropTarget(Button? button, string? hint = null)
    {
        var visual = button?.Content is StackPanel { Children.Count: > 0 } panel
            ? panel.Children[0]
            : null;

        if (!ReferenceEquals(visual, _dropTargetVisual))
        {
            ScaleVisual(_dropTargetVisual, 1.0);
            _dropTargetVisual = visual;
            ScaleVisual(visual, DropTargetScale);
        }

        if (button is null || hint is null) DragHintWindow.Hide();
        else DragHintWindow.Show(this, button, hint);
    }

    private static void ScaleVisual(UIElement? visual, double scale)
    {
        if (visual is null) return;

        if (visual.RenderTransform is not ScaleTransform transform)
        {
            if (Math.Abs(scale - 1.0) < 0.001) return; // rien a restaurer
            visual.RenderTransformOrigin = new Point(0.5, 0.5);
            transform = new ScaleTransform(1, 1);
            visual.RenderTransform = transform;
        }

        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(90))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void ShowDropIndicator(int index)
    {
        if (_dropIndicator is null)
        {
            _dropIndicator = new Border { Height = 2, Margin = new Thickness(2, 1, 2, 1) };
            _dropIndicator.SetResourceReference(Border.BackgroundProperty, "BarForeground");
        }

        if (ItemsHost.Children.Contains(_dropIndicator))
        {
            var current = ItemsHost.Children.IndexOf(_dropIndicator);
            if (current == index) return;
            ItemsHost.Children.Remove(_dropIndicator);
        }
        ItemsHost.Children.Insert(Math.Clamp(index, 0, ItemsHost.Children.Count), _dropIndicator);
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator is not null && ItemsHost.Children.Contains(_dropIndicator))
            ItemsHost.Children.Remove(_dropIndicator);
    }

    // ---------------------------------------------------------------- menus contextuels

    private void OnBarRightClick(object sender, MouseButtonEventArgs e)
    {
        if (HitTestItem(e.GetPosition(this)) is not null) return;
        e.Handled = true;
        ShowMenu(BuildBarMenu());
    }

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutItem item }) return;
        e.Handled = true;
        ShowMenu(BuildItemMenu(item));
    }

    private void ShowMenu(ContextMenu menu)
    {
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu BuildBarMenu()
    {
        var menu = new ContextMenu();

        var add = new MenuItem { Header = Strings.Get("Menu_Add") };
        var actions = new MenuItem { Header = Strings.Get("Menu_AddAction") };
        foreach (var action in ActionRegistry.All)
        {
            var a = action;
            actions.Items.Add(Item(a.Title, () => ShortcutStore.Add(new ShortcutItem
            {
                Kind = ShortcutKind.Action,
                ActionId = a.Id,
                Name = a.Title
            })));
        }
        var locations = new MenuItem { Header = Strings.Get("Menu_AddShellLocation") };
        foreach (var address in ShellLocations.Known)
        {
            var target = address;
            var name = ShellLocations.DisplayName(target);
            if (name is null) continue; // emplacement absent de cette version de Windows
            locations.Items.Add(Item(name, () => ShortcutStore.Add(ShellLocations.CreateShortcut(target))));
        }

        add.Items.Add(actions);
        if (locations.Items.Count > 0) add.Items.Add(locations);
        add.Items.Add(Item(Strings.Get("Common_Separator"), () => ShortcutStore.Add(new ShortcutItem
        {
            Kind = ShortcutKind.Separator,
            Name = Strings.Get("Common_Separator")
        })));
        menu.Items.Add(add);

        var nextBar = Item(
            Strings.Format("Menu_SwitchBar", (ShortcutStore.Profile + 1) % ShortcutStore.ProfileCount + 1),
            SwitchToNextBar);
        nextBar.InputGestureText = "Tab";
        menu.Items.Add(nextBar);

        menu.Items.Add(new Separator());

        var autoHide = new MenuItem
        {
            Header = Strings.Get("Menu_AutoHide"),
            IsCheckable = true,
            IsChecked = SettingsStore.Current.AutoHide
        };
        autoHide.Click += (_, _) => AutoHideToggled?.Invoke();
        menu.Items.Add(autoHide);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("Menu_Settings"), () => SettingsRequested?.Invoke()));
        menu.Items.Add(Item(Strings.Get("Menu_Reload"), () => ReloadRequested?.Invoke()));
        menu.Items.Add(Item(Strings.Get("Menu_OpenLogs"), () => LogsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("Menu_Quit"), () => QuitRequested?.Invoke()));
        return menu;
    }

    private ContextMenu BuildItemMenu(ShortcutItem item)
    {
        var menu = new ContextMenu();

        if (!item.IsSeparator)
        {
            menu.Items.Add(Item(Strings.Get("Menu_Open"), async () => await ActivateAsync(item, ActivationMode.Default, null)));
            if (item.Kind == ShortcutKind.Folder)
            {
                menu.Items.Add(Item(Strings.Get("Menu_OpenNewTab"),
                    async () => await ActivateAsync(item, ActivationMode.NewTab, null)));
                menu.Items.Add(Item(Strings.Get("Menu_OpenNewWindow"),
                    async () => await ActivateAsync(item, ActivationMode.NewWindow, null)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Strings.Get("Menu_Edit"), () => UI.ShortcutEditorWindow.Edit(item)));
            menu.Items.Add(Item(Strings.Get("Menu_Rename"), () => Rename(item)));
            menu.Items.Add(Item(Strings.Get("Menu_IconLibrary"), () => UI.IconPickerWindow.ChooseFor(item)));
            menu.Items.Add(Item(Strings.Get("Menu_ChangeIcon"), () => ChangeIcon(item)));
            if (item.IconOverride is not null || item.IconColor is not null)
                menu.Items.Add(Item(Strings.Get("Menu_DefaultIcon"), () =>
                {
                    var copy = item.Clone();
                    copy.IconOverride = null;
                    copy.IconColor = null;
                    ShortcutStore.Replace(copy);
                }));
            menu.Items.Add(new Separator());
        }

        var index = ShortcutStore.IndexOf(item.Id);
        menu.Items.Add(Item(Strings.Get("Menu_SeparatorBefore"), () => AddSeparator(index)));
        menu.Items.Add(Item(Strings.Get("Menu_SeparatorAfter"), () => AddSeparator(index + 1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("Menu_Delete"), () => ShortcutStore.Remove(item.Id)));
        return menu;
    }

    private static void AddSeparator(int index) =>
        ShortcutStore.Add(new ShortcutItem { Kind = ShortcutKind.Separator, Name = Strings.Get("Common_Separator") }, index);

    private static void Rename(ShortcutItem item)
    {
        var name = UI.InputDialog.Ask(Strings.Get("Bar_Rename"), Strings.Get("Bar_NewName"), GetDisplayName(item));
        if (name is null) return;
        var copy = item.Clone();
        copy.Name = name;
        ShortcutStore.Replace(copy);
    }

    private static void ChangeIcon(ShortcutItem item)
    {
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
        copy.IconColor = null;
        IconService.ClearCache();
        ShortcutStore.Replace(copy);
    }

    private static MenuItem Item(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }
}
