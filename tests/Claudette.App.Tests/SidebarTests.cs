using Avalonia.Input;
using Avalonia.Media;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Protocol;
using Claudette.Core.Settings;

namespace Claudette.App.Tests;

/// <summary>The sidebar that lists the tabs: collapsing to its rail, resizing, its rows and its groups' colors and names (DESIGN.md §3, §4).</summary>
public class SidebarTests
{
    [Fact]
    public async Task Collapsing_the_sidebar_is_remembered()
    {
        await using var h = new TabTestHarness();
        Assert.False(h.Shell.Layout.IsSidebarCollapsed);
        Assert.Equal(ShellLayout.DefaultSidebarWidth, h.Shell.Layout.SidebarDisplayWidth);

        h.Shell.Layout.ToggleSidebarCommand.Execute(null);

        Assert.True(h.Shell.Layout.IsSidebarCollapsed);
        Assert.Equal(ShellLayout.RailWidth, h.Shell.Layout.SidebarDisplayWidth);
        Assert.True(h.Services.State.SidebarCollapsed);
        Assert.True(new ShellViewModel(h.Services, () => { }).Layout.IsSidebarCollapsed);
    }

    [Fact]
    public async Task User_guide_under_Settings_opens_the_guide()
    {
        await using var h = new TabTestHarness();

        await h.Shell.OpenGuideCommand.ExecuteAsync(null);

        Assert.Equal("https://claudette.dotbunny.com/guide/", Assert.Single(h.Platform.OpenedUrls));
    }

    [Fact]
    public async Task A_narrow_window_shows_the_rail_without_changing_the_users_choice()
    {
        await using var h = new TabTestHarness();
        h.Shell.Layout.SetAvailableWidth(1200);

        h.Shell.Layout.SetAvailableWidth(ShellLayout.NarrowWidth - 1);
        Assert.True(h.Shell.Layout.IsSidebarCollapsed);
        Assert.False(h.Services.State.SidebarCollapsed);

        // Expanding while narrow is only for now.
        h.Shell.Layout.ToggleSidebarCommand.Execute(null);
        Assert.False(h.Shell.Layout.IsSidebarCollapsed);
        Assert.False(h.Services.State.SidebarCollapsed);

        h.Shell.Layout.SetAvailableWidth(1200);
        Assert.False(h.Shell.Layout.IsSidebarCollapsed);

        // Collapsed by the user, it stays collapsed after the window has been narrow.
        h.Shell.Layout.ToggleSidebarCommand.Execute(null);
        h.Shell.Layout.SetAvailableWidth(600);
        h.Shell.Layout.SetAvailableWidth(1200);
        Assert.True(h.Shell.Layout.IsSidebarCollapsed);
    }

    [Fact]
    public async Task Resizing_the_sidebar_stops_at_its_least_and_where_the_conversation_would_get_too_narrow_and_is_saved()
    {
        await using var h = new TabTestHarness();
        h.Shell.Layout.SetAvailableWidth(2000);

        h.Shell.Layout.ResizeSidebar(40);
        Assert.Equal(ShellLayout.MinSidebarWidth, h.Shell.Layout.SidebarWidth);
        // No most of its own.
        h.Shell.Layout.ResizeSidebar(1500);
        Assert.Equal(1500, h.Shell.Layout.SidebarWidth);
        h.Shell.Layout.ResizeSidebar(5000);
        Assert.Equal(2000 - ShellLayout.MinConversationWidth, h.Shell.Layout.SidebarWidth);

        h.Shell.Layout.ResizeSidebar(300);
        Assert.Null(h.Services.State.SidebarWidth);
        h.Shell.Layout.SaveSidebarWidth();

        Assert.Equal(300, h.Services.State.SidebarWidth);
        Assert.Equal(300, new ShellViewModel(h.Services, () => { }).Layout.SidebarDisplayWidth);
    }

    [Fact]
    public async Task The_sidebar_leaves_room_for_the_selected_tabs_side_panel_and_gives_way_after_it()
    {
        await using var h = new TabTestHarness();
        var first = await h.OpenTabAsync();
        var layout = h.Shell.Layout;
        layout.SetAvailableWidth(2000);
        layout.ResizeSidebar(1000);

        // The side panel open, the sidebar leaves it its least beside the conversation's.
        first.IsSidePanelOpen = true;
        Assert.True(layout.IsSidePanelShown);
        Assert.Equal(1000, layout.SidebarDisplayWidth);
        layout.SetAvailableWidth(1400);
        Assert.Equal(1400 - ShellLayout.MinConversationWidth - ShellLayout.MinSidePanelWidth, layout.SidebarDisplayWidth);

        // The window wide again, the width the user dragged comes back.
        layout.SetAvailableWidth(2000);
        Assert.Equal(1000, layout.SidebarDisplayWidth);
        Assert.Equal(1000, layout.SidebarWidth);

        // A tab without its side panel open gives the sidebar that room; the first one, selected again, takes it back.
        layout.SetAvailableWidth(1200);
        // (Its folder is gone, so it starts no session.)
        var second = new TabViewModel(h.Services, h.Shell, new TabState { Folder = Path.Combine(h.Root, "elsewhere") }, isRestored: false);
        var group = new TabGroupViewModel(second.Folder, TabGroupViewModel.Palette[1].Color, isCollapsed: false);
        group.Tabs.Add(second);
        h.Shell.Groups.Add(group);
        h.Shell.SelectedTab = second;
        Assert.False(layout.IsSidePanelShown);
        Assert.Equal(1200 - ShellLayout.MinConversationWidth, layout.SidebarDisplayWidth);
        h.Shell.SelectedTab = first;
        Assert.True(layout.IsSidePanelShown);
        Assert.Equal(1200 - ShellLayout.MinConversationWidth - ShellLayout.MinSidePanelWidth, layout.SidebarDisplayWidth);
        first.IsSidePanelOpen = false;
        Assert.Equal(1200 - ShellLayout.MinConversationWidth, layout.SidebarDisplayWidth);
    }

    [Fact]
    public async Task Move_up_and_down_keep_pinned_tabs_first()
    {
        await using var h = new TabTestHarness();
        h.Services.Settings.Sessions.RestoreUnpinnedTabs = true;
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true }, new TabState { Folder = h.WorkFolder }, new TabState { Folder = h.WorkFolder }];
        h.Shell.Restore(null);
        var group = h.Shell.Groups.Single();
        var (a, b, c) = (group.Tabs[0], group.Tabs[1], group.Tabs[2]);

        h.Shell.MoveTabUpCommand.Execute(c);
        Assert.Equal([a, c, b], group.Tabs);

        h.Shell.MoveTabUpCommand.Execute(c);
        h.Shell.MoveTabDownCommand.Execute(a);
        Assert.Equal([a, c, b], group.Tabs);

        h.Shell.MoveTabDownCommand.Execute(c);
        Assert.Equal([a, b, c], group.Tabs);
    }

    [Fact]
    public async Task A_row_shows_the_model_or_what_needs_attention()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(tab.ModelBadge, tab.RowDetail);

        tab.Status = TabStatus.NeedsInput;
        Assert.Equal("Needs your input", tab.RowDetail);
        tab.Status = TabStatus.Error;
        Assert.Equal(tab.StatusTip, tab.RowDetail);
        tab.Status = TabStatus.Working;
        Assert.Equal(tab.ModelBadge, tab.RowDetail);

        tab.Effort = "high";
        Assert.EndsWith("High", tab.RowDetail, StringComparison.Ordinal);
        Assert.Contains(nameof(TabViewModel.RowDetail), changed);

        // Check-ins that got no reply (DESIGN.md §5).
        changed.Clear();
        tab.IsPossiblyStuck = true;
        Assert.Equal("Possibly stuck", tab.RowDetail);
        Assert.Contains(nameof(TabViewModel.RowDetail), changed);
    }

    [Fact]
    public async Task A_failed_tab_shows_the_error_on_its_row()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Exit(1, "Loading settings…\nError: Invalid model name: claude-nonsense\n");
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Error, "the error");

        Assert.Equal("Error: Invalid model name: claude-nonsense", tab.RowDetail);
        Assert.Contains(tab.InfoRows, r => r.Label == "Status" && r.Value == "Error: Invalid model name: claude-nonsense");
        Assert.Equal("Claude Code stopped unexpectedly (exit code 3)", TabViewModel.ExitErrorMessage(new TransportExit(3, "  \n")));
    }

    [Fact]
    public async Task The_sidebar_has_a_shortcut_named_in_its_tooltips()
    {
        await using var h = new TabTestHarness();
        var primary = Shortcuts.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        var shown = Shortcuts.IsMac ? "⌘B" : "Ctrl+B";

        Assert.True(Shortcuts.Matches(h.Services.Settings.Keyboard, KeyboardShortcuts.ToggleSidebar, Key.B, primary));
        Assert.Equal($"Collapse the sidebar ({shown})", h.Shell.Tips.CollapseSidebar);
        Assert.Equal($"Expand the sidebar ({shown})", h.Shell.Tips.ExpandSidebar);
    }

    [Fact]
    public async Task Change_color_applies_a_swatch_a_hex_code_or_the_spectrum_at_once_and_remembers_it()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        Assert.Equal(TabGroupViewModel.Palette[0].Color, group.Color);

        var picker = h.Shell.PickGroupColor(group);
        var done = 0;
        picker.Done += () => done++;
        Assert.Equal("#4C8DFF", picker.Hex);
        Assert.Equal(["Blue"], picker.Swatches.Where(s => s.IsSelected).Select(s => s.Name));

        // A swatch applies and closes the picker.
        picker.PickCommand.Execute(picker.Swatches[3]);
        Assert.Equal(TabGroupViewModel.Palette[3].Color, group.Color);
        Assert.Equal("#A66BFF", picker.Hex);
        Assert.Equal(["Purple"], picker.Swatches.Where(s => s.IsSelected).Select(s => s.Name));
        Assert.Equal("#A66BFF", h.Services.State.FolderColors[group.Folder]);
        Assert.Equal(1, done);

        // A hex code applies once it's a color, so typing one changes nothing on the way.
        picker.Hex = "#12AB3";
        Assert.Equal(TabGroupViewModel.Palette[3].Color, group.Color);
        picker.Hex = "12ab34";
        Assert.Equal(Color.Parse("#12AB34"), group.Color);
        Assert.DoesNotContain(picker.Swatches, s => s.IsSelected);
        Assert.Equal("#12AB34", h.Services.State.FolderColors[group.Folder]);

        // The spectrum and hue slider.
        picker.HsvColor = new HsvColor(1, 120, 1, 1);
        Assert.Equal(Color.FromRgb(0, 255, 0), group.Color);
        Assert.Equal("#00FF00", picker.Hex);
        Assert.Equal(1, done);
        picker.FinishCommand.Execute(null);
        Assert.Equal(2, done);

        // Closing the group and opening the folder again brings the color back.
        await h.Shell.CloseGroupCommand.ExecuteAsync(group);
        Assert.Empty(h.Shell.Groups);
        await h.OpenTabAsync();
        Assert.Equal(Color.FromRgb(0, 255, 0), h.Shell.Groups.Single().Color);
    }

    [Fact]
    public async Task A_groups_color_comes_back_however_its_folder_was_written_and_from_an_older_save()
    {
        await using var h = new TabTestHarness();
        h.Services.Settings.Sessions.RestoreUnpinnedTabs = true;
        var other = Path.Combine(h.Root, "other");
        Directory.CreateDirectory(other);
        var state = h.Services.State;
        // Saved under another spelling of the folder's path, and by an older Claudette, as the index of a group color.
        state.FolderColors[h.WorkFolder + Path.DirectorySeparatorChar] = "#12AB34";
        state.GroupColors[other] = 5;
        state.Tabs = [new TabState { Folder = h.WorkFolder }, new TabState { Folder = other }];

        h.Shell.Restore(null);

        Assert.Equal(Color.Parse("#12AB34"), h.Shell.Groups.Single(g => FolderHistory.SamePath(g.Folder, h.WorkFolder)).Color);
        Assert.Equal(TabGroupViewModel.Palette[5].Color, h.Shell.Groups.Single(g => FolderHistory.SamePath(g.Folder, other)).Color);
        // Each is saved once now, as the group's folder is written.
        Assert.Equal(
            h.Shell.Groups.Select(g => (g.Folder, TabGroupViewModel.ToHex(g.Color))).Order(),
            state.FolderColors.Select(p => (p.Key, p.Value)).Order());
        Assert.Empty(state.GroupColors);
    }

    [Fact]
    public async Task Rename_gives_a_group_a_name_of_its_own_and_remembers_it_for_the_folder()
    {
        await using var h = new TabTestHarness();
        await h.OpenTabAsync();
        var group = h.Shell.Groups.Single();
        Assert.Equal("work", group.Label);

        var editor = h.Shell.RenameGroup(group);
        var done = 0;
        editor.Done += () => done++;
        Assert.Equal("work", editor.Name);
        Assert.Equal("work", editor.Placeholder);
        editor.Name = "  Orbit (main)  ";
        editor.SaveCommand.Execute(null);

        Assert.Equal(1, done);
        Assert.Equal("Orbit (main)", group.Label);
        Assert.True(group.HasName);
        Assert.Equal("Orbit (main)", h.Services.State.FolderNames[group.Folder]);

        // Cancel leaves it as it was.
        editor = h.Shell.RenameGroup(group);
        editor.Done += () => done++;
        Assert.Equal("Orbit (main)", editor.Name);
        editor.Name = "Something else";
        editor.CancelCommand.Execute(null);
        Assert.Equal(2, done);
        Assert.Equal("Orbit (main)", group.Label);

        // Closing the group and opening the folder again brings the name back.
        await h.Shell.CloseGroupCommand.ExecuteAsync(group);
        await h.OpenTabAsync();
        group = h.Shell.Groups.Single();
        Assert.Equal("Orbit (main)", group.Label);

        // Reset name goes back to the folder's, as does saving an empty name or the folder's own.
        h.Shell.ResetGroupNameCommand.Execute(group);
        Assert.Equal("work", group.Label);
        Assert.False(group.HasName);
        Assert.Empty(h.Services.State.FolderNames);
        foreach (var name in new[] { "   ", "work" })
        {
            editor = h.Shell.RenameGroup(group);
            editor.Name = name;
            editor.SaveCommand.Execute(null);
            Assert.False(group.HasName);
            Assert.Empty(h.Services.State.FolderNames);
        }
    }

    [Fact]
    public async Task A_named_group_leaves_another_folder_of_the_same_name_its_name_alone()
    {
        await using var h = new TabTestHarness();
        h.Services.Settings.Sessions.RestoreUnpinnedTabs = true;
        var work = Path.Combine(h.Root, "work", "api");
        var personal = Path.Combine(h.Root, "personal", "api");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(personal);
        // Saved under another spelling of the folder's path.
        h.Services.State.FolderNames[personal + Path.DirectorySeparatorChar] = "Side project";
        h.Services.State.Tabs = [new TabState { Folder = work }, new TabState { Folder = personal }];

        h.Shell.Restore(null);
        Assert.Equal(["api", "Side project"], h.Shell.Groups.Select(g => g.Label));

        var named = h.Shell.Groups[1];
        h.Shell.ResetGroupNameCommand.Execute(named);
        Assert.Equal(["work/api", "personal/api"], h.Shell.Groups.Select(g => g.Label));
        Assert.Empty(h.Services.State.FolderNames);
    }

    [Theory]
    [InlineData("#12ab34", "#12AB34")]
    [InlineData("  12AB34 ", "#12AB34")]
    [InlineData("#1a3", "#11AA33")]
    [InlineData("#8012AB34", null)]
    [InlineData("#12AB3", null)]
    [InlineData("red", null)]
    [InlineData("", null)]
    public void A_hex_code_is_six_or_three_digits(string text, string? expected)
    {
        var parsed = TabGroupViewModel.TryParseHex(text, out var color);
        Assert.Equal(expected, parsed ? TabGroupViewModel.ToHex(color) : null);
    }

    [Theory]
    [InlineData("refactor the auth middleware", "R")]
    [InlineData("  #42 flaky test", "4")]
    [InlineData("", "·")]
    [InlineData(null, "·")]
    public void A_collapsed_row_shows_the_names_first_letter(string? name, string expected) =>
        Assert.Equal(expected, Converters.Initials(name));
}
