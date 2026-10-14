using Avalonia.Controls;
using Avalonia.Input;

namespace Claudette.App.Views;

/// <summary>An image from a tool's result, full size in a window of its own (DESIGN.md §18, "Images from Claude's tools").</summary>
public partial class ImageWindow : Window
{
    public ImageWindow() => InitializeComponent();

    /// <summary>
    /// Opens it beside <paramref name="owner"/>, no larger than 90% of the screen that's on: a 1080p laptop at 150% has
    /// only 720 pixels of height to give it.
    /// </summary>
    public void ShowBeside(Window owner)
    {
        if (owner.Screens.ScreenFromWindow(owner) is { } screen)
        {
            var area = screen.WorkingArea.ToRect(screen.Scaling);
            MaxWidth = Math.Max(MinWidth, Math.Min(MaxWidth, area.Width * 0.9));
            MaxHeight = Math.Max(MinHeight, Math.Min(MaxHeight, area.Height * 0.9));
        }
        Show(owner);
    }

    /// <summary>
    /// It opens at the image's size, up to its largest; from then on it can be made any size, the image shrinking to
    /// fit but never growing past its own size.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SizeToContent = SizeToContent.Manual;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
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
}
