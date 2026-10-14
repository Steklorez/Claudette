using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>A folder group's name, from Rename… in the group's menu (DESIGN.md §4, "Grouped by folder").</summary>
public class GroupNameUiTests
{
    [AvaloniaFact]
    public async Task Rename_opens_the_name_beside_the_group_ready_to_type_over_and_Enter_saves_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var group = h.Shell.Groups.Single();
        var label = GroupLabel(window);

        var flyout = OpenEditor(window, label);
        var editor = Assert.IsType<GroupNameEditor>(flyout.Content);
        var box = editor.GetVisualDescendants().OfType<TextBox>().Single();
        await UiText.SettleUntilAsync(window, () => box.IsFocused, "the name's box to take the focus");
        var shown = UiText.Describe(editor, (h.Root, "{root}"));

        // The folder's name, selected, so typing replaces it; a space doesn't reach the label's button and collapse the group.
        Assert.Equal("work", box.Text);
        Assert.Equal((0, 4), (box.SelectionStart, box.SelectionEnd));
        var keys = TopLevel.GetTopLevel(box)!;
        keys.KeyTextInput("Orbit main");
        UiText.Settle(window);
        Assert.Equal("Orbit main", box.Text);
        Assert.True(group.IsExpanded);

        // Enter saves it and closes.
        keys.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        keys.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.False(flyout.IsOpen);
        Assert.Equal("Orbit main", group.Label);
        Assert.Contains(label.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Orbit main");

        // The rail's group menu opens it too, and Escape closes it as it was.
        h.Shell.Layout.ToggleSidebarCommand.Execute(null);
        UiText.Settle(window);
        var rail = GroupLabel(window);
        Assert.NotSame(label, rail);
        var railFlyout = OpenEditor(window, rail);
        var railBox = Assert.IsType<GroupNameEditor>(railFlyout.Content).GetVisualDescendants().OfType<TextBox>().Single();
        await UiText.SettleUntilAsync(window, () => railBox.IsFocused, "the rail's name box to take the focus");
        railBox.Text = "Something else";
        keys = TopLevel.GetTopLevel(railBox)!;
        keys.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        keys.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.False(railFlyout.IsOpen);
        Assert.Equal("Orbit main", group.Label);

        await Verify(shown);
    }

    private static Button GroupLabel(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("grouplabel") && b.IsEffectivelyVisible);

    private static Flyout OpenEditor(Window window, Button label)
    {
        label.ContextMenu!.Open(label);
        UiText.Settle(window);
        var item = label.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Rename…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        label.ContextMenu.Close();
        UiText.Settle(window);
        return Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(label));
    }
}
