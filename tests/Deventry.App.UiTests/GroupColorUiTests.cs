using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;
using Deventry.Core.ProjectTools;

namespace Deventry.App.UiTests;

/// <summary>
/// A folder group's color picker, from <b>Choose…</b> on its project's Group page (DESIGN.md §4, "Grouped by folder"),
/// which took the place of <b>Change color…</b> in the group's menu.
/// </summary>
public class GroupColorUiTests
{
    [AvaloniaFact]
    public async Task Choose_on_the_Group_page_opens_a_picker_that_recolors_the_group_as_you_pick()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var shell = UiText.Show(new ShellView { DataContext = h.Shell });
        var group = h.Shell.Groups.Single();
        var label = shell.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("grouplabel") && b.IsEffectivelyVisible);

        // The group's menu leaves its color and icon to the project's settings (issue #109).
        label.ContextMenu!.Open(label);
        UiText.Settle(shell);
        var headers = label.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        label.ContextMenu.Close();
        Assert.Contains("Project settings…", headers);
        Assert.DoesNotContain("Change color…", headers);
        Assert.DoesNotContain("Change icon…", headers);

        var settings = new ProjectSettingsWindowViewModel(h.Services, h.Shell.ProjectSettingsFor(h.WorkFolder)!, ProjectSettingsWindowViewModel.GroupPage);
        var window = new ProjectSettingsWindow { DataContext = settings, Width = 860, Height = 660 };
        window.Show();
        UiText.Settle(window);
        var choose = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ChooseColor");
        choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UiText.Settle(window);
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(choose));
        var picker = Assert.IsType<GroupColorPicker>(flyout.Content);
        await UiText.SettleUntilAsync(window, () => picker.IsEffectivelyVisible, "the picker");
        var shown = UiText.Describe(picker);

        // The group colors, named, with the group's own ringed; then the spectrum and hue slider, on the same color.
        var swatches = picker.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("swatch")).ToList();
        Assert.Equal(TabGroupViewModel.Palette.Select(p => p.Name), swatches.Select(AutomationProperties.GetName));
        Assert.Equal([true, false, false, false, false, false, false, false], swatches.Select(s => s.Classes.Contains("selected")));
        var spectrum = picker.GetVisualDescendants().OfType<ColorSpectrum>().Single();
        var hue = picker.GetVisualDescendants().OfType<ColorSlider>().Single();
        Assert.Equal(group.Color, spectrum.Color);
        Assert.Equal(ColorComponent.Component1, hue.ColorComponent);
        Assert.Equal("Saturation and brightness", AutomationProperties.GetName(spectrum));
        Assert.Equal("Hue", AutomationProperties.GetName(hue));

        // A hex code recolors the group's dot in the sidebar as it's typed, and moves the spectrum; nothing's saved yet.
        var hex = picker.GetVisualDescendants().OfType<TextBox>().Single();
        hex.Text = "#12AB34";
        UiText.Settle(window);
        UiText.Settle(shell);
        Assert.Equal(Color.Parse("#12AB34"), group.Color);
        Assert.Equal(Color.Parse("#12AB34"), spectrum.Color);
        Assert.Equal(Color.Parse("#12AB34"), ((ISolidColorBrush)label.GetVisualDescendants().OfType<Ellipse>().Single().Fill!).Color);
        Assert.DoesNotContain(swatches, s => s.Classes.Contains("selected"));
        Assert.Null(ProjectFile.ReadColor(h.WorkFolder, ProjectFileScope.Shared));

        // A swatch applies and closes the picker, which saves the color in the file the page shows.
        swatches[2].Command!.Execute(swatches[2].CommandParameter);
        UiText.Settle(window);
        Assert.False(flyout.IsOpen);
        Assert.Equal(TabGroupViewModel.Palette[2].Color, group.Color);
        Assert.Equal(TabGroupViewModel.ToHex(TabGroupViewModel.Palette[2].Color), ProjectFile.ReadColor(h.WorkFolder, ProjectFileScope.Shared));

        window.Close();
        await Verify(shown);
    }
}
