using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Deventry.App.Services;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>
/// A project's settings, in a window of their own (DESIGN.md §14, "The project's settings"). It isn't modal: it stays
/// open beside the main window. Esc, unless a dialog in it took it first, or <c>Ctrl/Cmd+W</c> closes it.
/// </summary>
public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not ProjectSettingsWindowViewModel settings)
        {
            return;
        }
        var primary = Shortcuts.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !settings.Project.HasEditor
            || e.Key == Key.W && e.KeyModifiers == primary)
        {
            Close();
            e.Handled = true;
        }
    }
}
