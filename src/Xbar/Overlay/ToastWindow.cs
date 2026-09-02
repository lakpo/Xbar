using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Xbar.Interop;

namespace Xbar.Overlay;

/// <summary>
/// Retour visuel discret apres une action (aucune boite de dialogue bloquante) :
/// une petite etiquette qui apparait a gauche de la barre puis disparait seule.
/// </summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;

    private readonly TextBlock _text;
    private readonly DispatcherTimer _timer;

    private ToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _timer.Tick += (_, _) => { _timer.Stop(); FadeOut(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void Show(Window anchor, string message, bool success)
    {
        try
        {
            _current?.Close();
            var toast = new ToastWindow();
            toast._text.Text = success ? message : $"⚠ {message}";
            toast.Opacity = 0;
            toast.Show();
            toast.PositionNear(anchor);
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            toast._timer.Start();
            _current = toast;
        }
        catch (Exception ex)
        {
            Core.Log.Warn($"Affichage du retour visuel : {ex.Message}");
        }
    }

    private void PositionNear(Window anchor)
    {
        var anchorHandle = new WindowInteropHelper(anchor).Handle;
        if (anchorHandle == IntPtr.Zero || !NativeMethods.GetWindowRect(anchorHandle, out var rect)) return;

        var dpi = NativeMethods.GetDpiForWindow(anchorHandle);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96.0;

        var handle = new WindowInteropHelper(this).Handle;
        var width = (int)Math.Round(ActualWidth * scale);
        var height = (int)Math.Round(ActualHeight * scale);

        var x = rect.Left - width - (int)(8 * scale);
        var y = rect.Top + (rect.Height - height) / 2;

        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOP, x, y, width, height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_current, this)) _current = null;
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
