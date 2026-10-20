using Avalonia;
using Avalonia.Controls;
using Claudette.App.ViewModels;

namespace Claudette.App.Views;

/// <summary>
/// A project run's log in the main area (DESIGN.md §18, "Project tools", "A run's log"): its header, and the log, which
/// <see cref="LogMirror"/> keeps in step with the run's lines.
/// </summary>
public partial class ProjectRunView : UserControl
{
    private readonly LogMirror _mirror;

    public ProjectRunView()
    {
        InitializeComponent();
        _mirror = new LogMirror(this, Log);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _mirror.Use(DataContext as ProjectRunViewModel);
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
