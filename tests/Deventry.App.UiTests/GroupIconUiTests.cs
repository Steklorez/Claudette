using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.Views;
using Deventry.Core.ProjectTools;

namespace Deventry.App.UiTests;

/// <summary>A project's icon in place of its group's colored dot (DESIGN.md §4, "Grouped by folder").</summary>
public class GroupIconUiTests
{
    [AvaloniaFact]
    public async Task A_projects_icon_takes_the_place_of_its_groups_dot_in_the_sidebar_and_the_rail()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var label = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("grouplabel") && b.IsEffectivelyVisible);
        Assert.True(label.GetVisualDescendants().OfType<Ellipse>().Single().IsEffectivelyVisible);
        Assert.False(label.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "GroupIcon").IsEffectivelyVisible);

        // Deventry's own icon, an ICO of PNGs, as Windows has read them since Vista.
        var icon = Path.Combine(h.WorkFolder, "Assets", "deventry.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "deventry.ico"), icon);
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """{ "icon": "Assets/deventry.ico" }""");
        await tab.ProjectTools.RefreshFileAsync();
        UiText.Settle(window);

        // Where the dot was, decoded small, at most three times the 14 pixels it's shown at.
        var image = label.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "GroupIcon");
        Assert.True(image.IsEffectivelyVisible);
        var bitmap = Assert.IsAssignableFrom<Bitmap>(image.Source);
        Assert.InRange(bitmap.PixelSize.Width, 1, 48);
        Assert.False(label.GetVisualDescendants().OfType<Ellipse>().Single().IsEffectivelyVisible);

        // The rail shows it in place of the group's bar.
        h.Shell.Layout.ToggleSidebarCommand.Execute(null);
        UiText.Settle(window);
        var rail = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("grouplabel") && b.IsEffectivelyVisible);
        Assert.True(rail.GetVisualDescendants().OfType<Image>().Single().IsEffectivelyVisible);
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Border>(), b => b.Width == 16 && b.Height == 4 && b.IsEffectivelyVisible);
    }
}
