using Avalonia.Controls;
using Avalonia.Input;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>Tab settings… (DESIGN.md §14, "Per-tab overrides"). Esc closes it as <b>Done</b> does: every change has applied already.</summary>
public partial class TabSettingsView : UserControl
{
    public TabSettingsView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    /// <summary>An Esc an open dropdown didn't take closes the dialog.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && DataContext is TabSettingsViewModel settings)
        {
            settings.DoneCommand.Execute(null);
            e.Handled = true;
        }
    }
}
