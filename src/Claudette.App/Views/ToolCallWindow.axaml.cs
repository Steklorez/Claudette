using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>A tool call in a window of its own, which follows it while it runs (DESIGN.md §18, "Conversation cards").</summary>
public partial class ToolCallWindow : Window
{
    public ToolCallWindow()
    {
        InitializeComponent();
        // The file's colors follow the theme while the window is open, as the diff view's do.
        ActualThemeVariantChanged += (_, _) => (DataContext as ToolCallWindowViewModel)?.UseDarkColors(ActualThemeVariant == ThemeVariant.Dark);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as ToolCallWindowViewModel)?.Dispose();
    }
}
