using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Claudette.App.Services;

/// <summary>
/// With <c>CLAUDETTE_UNFOCUSED=1</c>, Claudette's windows open without taking the focus (DESIGN.md §9, "Working on
/// Claudette"): the screenshot scripts in <c>tools/Claudette.Demo</c> set it, so a run doesn't take the focus from
/// whatever the user is doing meanwhile.
/// </summary>
public static class UnfocusedWindows
{
    public const string Variable = "CLAUDETTE_UNFOCUSED";

    public static bool IsOn { get; } = Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Opens every window without activating it, when it's on. Call before the main window is shown.</summary>
    public static void Use(Application app)
    {
        if (IsOn)
        {
            app.Styles.Add(new Style(x => x.Is<Window>()) { Setters = { new Setter(Window.ShowActivatedProperty, false) } });
        }
    }
}
