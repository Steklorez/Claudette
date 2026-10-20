using Avalonia;
using Avalonia.Controls;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// An editor's log in the main area (DESIGN.md §18, "Editor logs"): its header, and the log, which
/// <see cref="LogMirror"/> keeps in step with what's read of the file, its errors and warnings colored.
/// </summary>
public partial class EditorLogView : UserControl
{
    private readonly LogMirror _mirror;

    public EditorLogView()
    {
        InitializeComponent();
        _mirror = new LogMirror(this, Log);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _mirror.Use(DataContext as EditorLogViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _mirror.Watch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _mirror.Unwatch();
        base.OnDetachedFromVisualTree(e);
    }
}
