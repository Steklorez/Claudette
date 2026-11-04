using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Deventry.App.ViewModels.Settings;

namespace Deventry.App.Views.Settings;

public partial class ProjectGroupPageView : UserControl
{
    public ProjectGroupPageView() => InitializeComponent();

    /// <summary>
    /// <b>Choose…</b>: the group's color picker beside the button. Each color it picks shows on the group at once; the one
    /// it's left on is saved in the chosen file once, as it closes, however it closes.
    /// </summary>
    private void OnChooseColor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectGroupPage { Project: var project } || project.PickColor() is not { } picker || sender is not Control button)
        {
            return;
        }
        var flyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedTop, Content = new GroupColorPicker { DataContext = picker } };
        picker.Done += flyout.Hide;
        flyout.Closed += (_, _) => picker.Close();
        FlyoutBase.SetAttachedFlyout(button, flyout);
        flyout.ShowAt(button);
    }
}
