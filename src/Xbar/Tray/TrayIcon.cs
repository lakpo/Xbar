using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Xbar.Core;
using Xbar.Localization;

namespace Xbar.Tray;

/// <summary>
/// Icone de zone de notification, implementee directement sur Shell_NotifyIcon
/// (pas de dependance a Windows Forms). Une fenetre de messages sert de destinataire
/// aux notifications de l'icone, aux changements de theme et au redemarrage de l'Explorateur.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_SETTINGCHANGE = 0x001A;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private NOTIFYICONDATA _data;
    private bool _added;

    public event Action? ToggleBarRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action? SystemThemeChanged;

    /// <summary>Etat coche de l'entree « Afficher la barre ».</summary>
    public Func<bool>? IsBarVisible { get; set; }

    public TrayIcon()
    {
        var parameters = new HwndSourceParameters("Xbar.TrayWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = LoadAppIcon(),
            szTip = "Xbar"
        };

        Add();
    }

    private void Add()
    {
        _added = Shell_NotifyIcon(NIM_ADD, ref _data);
        if (!_added) Log.Warn("Ajout de l'icone de notification impossible");
    }

    public void ShowBalloon(string title, string message)
    {
        var data = _data;
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = 0;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreatedMessage)
        {
            // L'Explorateur a redemarre : il faut reinscrire l'icone.
            Log.Info("Barre des taches recreee, reinscription de l'icone de notification");
            Add();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_SETTINGCHANGE:
                SystemThemeChanged?.Invoke();
                break;

            case WM_APP_TRAY:
                switch ((int)lParam)
                {
                    case WM_LBUTTONUP:
                    case WM_LBUTTONDBLCLK:
                        ToggleBarRequested?.Invoke();
                        handled = true;
                        break;
                    case WM_RBUTTONUP:
                        ShowMenu();
                        handled = true;
                        break;
                }
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();

        var show = new MenuItem
        {
            Header = Strings.Get("Tray_ShowBar"),
            IsCheckable = true,
            IsChecked = IsBarVisible?.Invoke() ?? true
        };
        show.Click += (_, _) => ToggleBarRequested?.Invoke();
        menu.Items.Add(show);

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(Strings.Get("Menu_Settings"), () => SettingsRequested?.Invoke()));

        var startup = new MenuItem
        {
            Header = Strings.Get("Tray_Startup"),
            IsCheckable = true,
            IsChecked = StartupManager.IsEnabled()
        };
        startup.Click += (_, _) => StartupManager.SetEnabled(!StartupManager.IsEnabled());
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(Strings.Get("Menu_Quit"), () => QuitRequested?.Invoke()));

        // Necessaire pour qu'un menu ouvert depuis la zone de notification se ferme au clic exterieur.
        SetForegroundWindow(_source.Handle);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem MenuEntry(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = ExtractIcon(IntPtr.Zero, exe, 0);
                if (icon != IntPtr.Zero && icon != new IntPtr(1)) return icon;
            }
        }
        catch (Exception ex) { Log.Warn($"Chargement de l'icone : {ex.Message}"); }

        return LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    public void Dispose()
    {
        if (_added) Shell_NotifyIcon(NIM_DELETE, ref _data);
        _source.Dispose();
    }

    // --- Interop ---

    private const int NIM_ADD = 0x00;
    private const int NIM_MODIFY = 0x01;
    private const int NIM_DELETE = 0x02;
    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr instance, string exeFileName, int iconIndex);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
