using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Xbar.Interop;

namespace Xbar.Overlay;

/// <summary>
/// Etiquette affichee pendant un glisser-deposer pour annoncer ce que fera le depot
/// (« Copier vers : … », « Ouvrir avec : … »).
///
/// Une infobulle WPF ne conviendrait pas : pendant un glisser, la souris est capturee par la
/// boucle modale d'OLE et ToolTipService ne se declenche jamais. On utilise donc une petite
/// fenetre, non activable et transparente aux clics, positionnee a gauche du bouton vise.
/// </summary>
public sealed class DragHintWindow : Window
{
    /// <summary>Ecart entre l'etiquette et le bord gauche du bouton vise, en pixels logiques.</summary>
    private const double HintGapDip = 6;

    private static DragHintWindow? _current;

    private readonly TextBlock _text;

    private DragHintWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        Focusable = false;

        _text = new TextBlock
        {
            Margin = new Thickness(12, 7, 12, 8),
            FontSize = 12,
        };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "BarForeground");

        var frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = _text,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.3
            }
        };
        frame.SetResourceReference(Border.BackgroundProperty, "ToastBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "BarBorder");
        Content = frame;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>Affiche (ou met a jour) l'etiquette a gauche du bouton vise.</summary>
    public static void Show(Window bar, FrameworkElement target, string text)
    {
        try
        {
            _current ??= new DragHintWindow();
            _current._text.Text = text;
            if (!_current.IsVisible) _current.Show();

            // Necessaire avant de lire ActualWidth : la taille suit le texte (SizeToContent).
            _current.UpdateLayout();

            _current.PositionLeftOf(bar, target);
        }
        catch (Exception ex)
        {
            Core.Log.Debug($"Etiquette de glisser-deposer : {ex.Message}");
        }
    }

    /// <summary>Ferme l'etiquette. Masque volontairement Window.Hide, qui n'a pas de sens ici.</summary>
    public static new void Hide()
    {
        if (_current is null) return;
        try { _current.Close(); } catch { }
        _current = null;
    }

    private void PositionLeftOf(Window bar, FrameworkElement target)
    {
        var barHandle = new WindowInteropHelper(bar).Handle;
        if (barHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(barHandle, out var barRect)) return;

        var dpi = NativeMethods.GetDpiForWindow(barHandle);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;

        // On se cale sur le bouton vise, pas sur la fenetre : celle-ci reserve la largeur
        // depliee de la barre, son bord gauche peut donc etre tres a gauche du panneau visible.
        var offset = target.TranslatePoint(new Point(0, 0), bar);
        var buttonLeft = barRect.Left + (int)Math.Round(offset.X * scale);
        var buttonCenterY = barRect.Top + (int)Math.Round((offset.Y + target.ActualHeight / 2) * scale);

        var handle = new WindowInteropHelper(this).Handle;
        var width = (int)Math.Round(ActualWidth * scale);
        var height = (int)Math.Round(ActualHeight * scale);

        var x = buttonLeft - width - (int)Math.Round(HintGapDip * scale);
        var y = buttonCenterY - height / 2;

        NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOZORDER);
    }
}
