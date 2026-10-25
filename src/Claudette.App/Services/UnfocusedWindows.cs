using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Claudette.Platform.Shell.Windows;

namespace Claudette.App.Services;

/// <summary>
/// With <c>CLAUDETTE_UNFOCUSED=1</c>, Claudette's windows never take the focus (DESIGN.md §9, "Taking screenshots
/// without the focus"): the screenshot scripts in <c>tools/Claudette.Demo</c> set it, so a run doesn't take the focus
/// from whatever the user is doing meanwhile. Every window opens without activating, nothing brings one forward, and
/// on Windows every window and popup stays behind the user's windows, as it opens and after.
/// </summary>
public static class UnfocusedWindows
{
    public const string Variable = "CLAUDETTE_UNFOCUSED";

    private static readonly ConditionalWeakTable<TopLevel, object> Hooked = [];

    public static bool IsOn { get; } = Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Opens every window without activating it, and on Windows behind the rest, when it's on. Call before the main window is shown.</summary>
    public static void Use(Application app)
    {
        if (!IsOn)
        {
            return;
        }
        app.Styles.Add(new Style(x => x.Is<Window>()) { Setters = { new Setter(Window.ShowActivatedProperty, false) } });
        // On Windows: a window's template is applied in its first layout, before it's shown, so its first place in the
        // z-order is already the bottom. Popups too: menus, tips and flyouts are windows of their own.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TopLevel>((topLevel, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                KeepBehind(topLevel);
            }
        });
        // A popup's window is shown before its first layout, so it's sent behind the rest as it opens, and kept there.
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (OperatingSystem.IsWindows() && e.NewValue is true && popup.Child is { } child && TopLevel.GetTopLevel(child) is { } root)
            {
                KeepBehind(root);
                if (root.TryGetPlatformHandle()?.Handle is { } handle)
                {
                    WindowsBehind.SendToBack(handle);
                }
            }
        });
    }

    /// <summary>Brings <paramref name="window"/> to the front with the focus, unless it's on.</summary>
    public static void Activate(Window window)
    {
        if (!IsOn)
        {
            window.Activate();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void KeepBehind(TopLevel topLevel)
    {
        if (Hooked.TryGetValue(topLevel, out _))
        {
            return;
        }
        Hooked.Add(topLevel, topLevel);
        Win32Properties.AddWndProcHookCallback(topLevel, (IntPtr _, uint message, IntPtr _, IntPtr lParam, ref bool _) =>
        {
            if (message == WindowsBehind.WindowPosChanging)
            {
                WindowsBehind.KeepBehind(lParam);
            }
            return IntPtr.Zero;
        });
    }
}
