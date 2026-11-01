using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Deventry.Platform.Notifications.Windows;

/// <summary>
/// Whether the taskbar is dark (DESIGN.md §10), so its overlay shows the frame for a dark taskbar: Settings →
/// Personalization → Colors, "Choose your default Windows mode", which Windows keeps apart from apps' mode.
/// </summary>
internal static class TaskbarTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [SupportedOSPlatform("windows")]
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return IsDark(key?.GetValue("SystemUsesLightTheme"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return true;
        }
    }

    /// <summary>From <c>SystemUsesLightTheme</c>: 1 is light. Windows before 1903 has no light taskbar, and no value.</summary>
    public static bool IsDark(object? systemUsesLightTheme) => systemUsesLightTheme is not int light || light == 0;
}
