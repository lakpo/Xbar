using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Xbar.Core;

/// <summary>
/// Suit le theme clair/sombre de Windows et publie les couleurs de la barre dans les
/// ressources de l'application (utilisees via DynamicResource).
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDark { get; private set; }

    public static event Action? ThemeChanged;

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i) return i == 0;
        }
        catch (Exception ex) { Log.Warn($"Lecture du theme Windows : {ex.Message}"); }
        return false;
    }

    /// <summary>Recalcule les couleurs et met a jour les ressources de l'application.</summary>
    public static void Apply()
    {
        var dark = SettingsStore.Current.Theme switch
        {
            BarTheme.Light => false,
            BarTheme.Dark => true,
            _ => SystemUsesDarkTheme()
        };

        IsDark = dark;
        var res = Application.Current?.Resources;
        if (res is null) return;

        if (dark)
        {
            Set(res, "BarBackground", Color.FromRgb(0x28, 0x28, 0x28));
            Set(res, "BarBorder", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            Set(res, "BarForeground", Color.FromRgb(0xF2, 0xF2, 0xF2));
            Set(res, "ItemHover", Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
            Set(res, "ItemPressed", Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
            Set(res, "SeparatorBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            Set(res, "ToastBackground", Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set(res, "GripBrush", Color.FromArgb(0x8C, 0xE6, 0xE6, 0xE6));
        }
        else
        {
            Set(res, "BarBackground", Color.FromRgb(0xF7, 0xF7, 0xF7));
            Set(res, "BarBorder", Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            Set(res, "BarForeground", Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set(res, "ItemHover", Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            Set(res, "ItemPressed", Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            Set(res, "SeparatorBrush", Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            Set(res, "ToastBackground", Color.FromRgb(0xFF, 0xFF, 0xFF));
            Set(res, "GripBrush", Color.FromArgb(0x8C, 0x9E, 0x9E, 0x9E));
        }

        ThemeChanged?.Invoke();
    }

    private static void Set(ResourceDictionary res, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        res[key] = brush;
    }
}
