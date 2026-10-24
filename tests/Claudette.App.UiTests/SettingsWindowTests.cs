using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Claudette.App.Controls;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;
using Claudette.Core.Settings;

namespace Claudette.App.UiTests;

/// <summary>The Settings window rendered headlessly, a page at a time (DESIGN.md §14).</summary>
public class SettingsWindowTests
{
    [AvaloniaTheory]
    [InlineData("General")]
    [InlineData("Tabs")]
    [InlineData("Appearance")]
    [InlineData("Sessions")]
    public async Task A_page_shows_its_sections_and_settings(string category)
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var favorite = Directory.CreateDirectory(Path.Combine(h.Root, "projects-favorite", "api")).FullName;
        h.Services.State.FavoriteFolders.Add(favorite);
        var settings = new SettingsViewModel(h.Services, "me@example.com") { SelectedCategory = category };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        // Sessions measures the library off the UI thread as it shows.
        await Waiting.UntilAsync(() => settings.Sessions.Measuring.IsCompleted, "the library's size", poll: () => Dispatcher.UIThread.RunJobs());
        UiText.Settle(window);

        var page = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PageScroller");

        await Verify(UiText.Describe(page, (h.Root, "{root}"), (Environment.MachineName, "{machine}"))).UseParameters(category);
    }

    /// <summary>
    /// A page is a column at most 720 pixels wide (DESIGN.md §14): it fills a window narrower than that, so its rows use
    /// the room, and is centred in a wider one, so a control stays near its name.
    /// </summary>
    [AvaloniaFact]
    public async Task The_page_fills_a_narrow_window_and_is_centred_in_a_wide_one()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PageScroller");
        var page = Assert.IsAssignableFrom<Control>(scroller.Content);
        (double Left, double Right) Gaps()
        {
            var room = page.GetVisualParent()!.Bounds.Width;
            return (page.Bounds.X, room - page.Bounds.Right);
        }

        Assert.True(page.Bounds.Width < 720);
        Assert.Equal((40d, 40d), Gaps());

        window.Width = 1800;
        UiText.Settle(window);
        Assert.Equal(720, page.Bounds.Width);
        var (left, right) = Gaps();
        Assert.True(left > 40);
        Assert.Equal(left, right, 1);
        window.Close();
    }

    /// <summary>
    /// A setting that isn't at its default has a bar at its left (DESIGN.md §14), drawn in the room the row reaches out
    /// by; one at its default has none.
    /// </summary>
    [AvaloniaFact]
    public async Task A_changed_setting_has_its_bar_and_one_at_its_default_none()
    {
        await using var h = new TabTestHarness(s => s.Appearance.Theme = ThemeChoice.Dark, dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null, host: h.Shell) { SelectedCategory = SettingsCategory.Appearance };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        Border Bar(string key) => window.GetVisualDescendants().OfType<SettingRow>().Single(r => r.Key == key)
            .GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_ChangedBar");
        var theme = Bar("appearance.theme");
        Assert.True(theme.IsEffectivelyVisible);
        Assert.True(theme.Bounds.Width > 0 && theme.Bounds.Height > 0);
        // Avalonia clips a templated control to its bounds: nothing around the bar may cut it off.
        foreach (var clipping in theme.GetVisualAncestors().Where(v => v.ClipToBounds))
        {
            var at = theme.TranslatePoint(default, clipping)!.Value;
            Assert.True(at.X >= 0 && at.Y >= 0, $"{clipping.GetType().Name} {(clipping as Control)?.Name} cuts off the changed bar");
        }
        Assert.False(Bar("appearance.density").IsEffectivelyVisible);
        window.Close();
    }

    /// <summary>
    /// The search box's entries go somewhere (DESIGN.md §14, "Search"): each page, rendered, has a row or section for the
    /// key of each of its entries, which picking the entry scrolls to. And each row with a key finds its setting, so its
    /// words, its marks and its Reset come from the page. Rows a page holds while hidden count, since some only show in
    /// some states, such as a custom diff tool's command or an update to install.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_search_entry_has_its_row_and_every_row_its_setting()
    {
        await using var h = new TabTestHarness(updater: new FakeClaudeUpdater(new Version(2, 1, 284)), dispatcher: new AvaloniaUiDispatcher());
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await h.OpenTabAsync();
        await Waiting.UntilAsync(() => tab.ProjectTools.Project is not null, "the project", poll: () => Dispatcher.UIThread.RunJobs());
        var updates = new ClaudeUpdateViewModel(h.Services, h.Services.ClaudeUpdates!, () => h.Shell.RunningVersions);
        await h.Services.ClaudeUpdates!.CheckNowAsync();
        using var settings = new SettingsViewModel(h.Services, "me@example.com", updates, new SettingsOpening(Project: new ProjectSettingsViewModel(h.Services, tab, h.Shell)));
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();

        var missing = new List<string>();
        var lost = new List<string>();
        foreach (var page in settings.Pages)
        {
            settings.SelectedCategory = page.Title;
            UiText.Settle(window);
            var host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "PageHost");
            var rows = host.GetVisualDescendants().OfType<SettingRow>().ToList();
            // A row hidden in another's details isn't drawn, so look through the page's logical tree.
            var keys = host.GetLogicalDescendants().OfType<SettingRow>().Select(r => r.Key).Concat(host.GetLogicalDescendants().OfType<SettingsSection>().Select(s => s.Key)).OfType<string>().ToHashSet();
            missing.AddRange(page.SearchEntries.Where(entry => entry.Key is null || !keys.Contains(entry.Key)).Select(entry => $"{entry.Where}: {entry.Label} ({entry.Key})"));
            lost.AddRange(rows.Where(r => r.Key is not null && r.IsVisible && r.Setting is null).Select(r => $"{page.Title}: {r.Key}"));
        }
        window.Close();

        Assert.Equal([.. SettingsViewModel.AllCategories, .. SettingsViewModel.ProjectPages], settings.Pages.Select(p => p.Title));
        Assert.Empty(missing);
        Assert.Empty(lost);
    }

    /// <summary>
    /// A screen reader names every setting's control (DESIGN.md §3, "Accessibility"): a switch or a dropdown beside a
    /// title is named by the row's title, unless it has a name of its own.
    /// </summary>
    [AvaloniaFact]
    public async Task Every_settings_control_has_a_name()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();

        var nameless = new List<string>();
        foreach (var category in SettingsViewModel.AllCategories)
        {
            settings.SelectedCategory = category;
            UiText.Settle(window);
            nameless.AddRange(window.GetVisualDescendants().OfType<SettingRow>()
                .Where(r => r.IsEffectivelyVisible && r.Content is Control control && control is not Panel && control is not Button)
                .Where(r => AutomationProperties.GetName((Control)r.Content!) is not { Length: > 0 })
                .Select(r => $"{category}: {r.Key ?? r.Title}"));
        }
        window.Close();

        Assert.Empty(nameless);
    }

    /// <summary>
    /// A page's view is made when its page is picked and dropped when another is: none of its dropdowns or boxes writes a
    /// setting back as it goes.
    /// </summary>
    [AvaloniaFact]
    public async Task Going_through_every_page_changes_no_setting()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var appearance = h.Services.Settings.Appearance;
        appearance.Density = Density.Compact;
        appearance.Motion = MotionSetting.Reduce;
        appearance.Zoom = 125;
        h.Services.SaveSettings();
        var before = JsonFileStore<AppSettings>.Serialize(h.Services.Settings);
        var settings = new SettingsViewModel(h.Services, "me@example.com");
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();

        foreach (var category in SettingsViewModel.AllCategories.Concat(SettingsViewModel.AllCategories.Reverse()))
        {
            settings.SelectedCategory = category;
            UiText.Settle(window);
        }

        Assert.Equal(before, JsonFileStore<AppSettings>.Serialize(h.Services.Settings));
        window.Close();
    }

    /// <summary>
    /// Typing in a text setting doesn't save and apply it on every keystroke, which refreshed every tab each time; it
    /// takes effect when the box loses focus.
    /// </summary>
    [AvaloniaFact]
    public async Task A_text_setting_takes_effect_when_the_box_loses_focus_not_on_each_keystroke()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var settings = new SettingsViewModel(h.Services, null) { SelectedCategory = "Advanced" };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);
        var changes = 0;
        h.Services.SettingsChanged += (_, _) => changes++;
        var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "for example --add-dir ../shared");

        box.Focus();
        window.KeyTextInput("--verbose");
        UiText.Settle(window);
        Assert.Equal(0, changes);

        window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Search settings").Focus();
        UiText.Settle(window);
        Assert.Equal(1, changes);
        Assert.Equal("--verbose", settings.Advanced.ExtraArguments);
    }

    /// <summary>
    /// Picking a search result shows its page and brings its row into view, picked out for a moment (DESIGN.md §14,
    /// "Search"): here a setting on the long Keyboard page, far below the top.
    /// </summary>
    [AvaloniaFact]
    public async Task Picking_a_search_result_scrolls_to_its_row_and_picks_it_out()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 500 };
        window.Show();
        UiText.Settle(window);

        settings.SearchText = "snippets";
        settings.SelectedSearchResult = settings.SearchResults.First(r => r.Key == "quickSuffixes");
        await Waiting.UntilAsync(() => settings.ScrollToKey is null, "the window to scroll", poll: () => UiText.Settle(window));
        UiText.Settle(window);

        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PageScroller");
        var row = window.GetVisualDescendants().OfType<SettingRow>().First(r => r.Key == "quickSuffixes");
        Assert.Same(settings.Keyboard, settings.CurrentPage);
        Assert.True(scroller.Offset.Y > 0);
        var top = row.TranslatePoint(default, scroller)!.Value.Y;
        Assert.InRange(top, -1, scroller.Viewport.Height);
        Assert.Contains(":highlighted", row.Classes);

        h.Time.Advance(SettingsViewModel.HighlightFor);
        UiText.Settle(window);
        Assert.DoesNotContain(":highlighted", row.Classes);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_sidebar_ends_with_the_version_and_Help_with_the_guide_Discord_and_Report_an_issue()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.BuildCommit = "842169b";
        h.Services.IsSourceBuild = true;
        var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        var version = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Copy version details");
        var help = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Help");
        var sidebar = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        Assert.True(version.IsEffectivelyVisible);
        Assert.True(help.IsEffectivelyVisible);
        Assert.Equal("Claudette 0.1.0 · 842169b", version.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        // The foot sits below the pages, not over them.
        var pagesBottom = sidebar.TranslatePoint(new Point(0, sidebar.Bounds.Height), window)!.Value.Y;
        Assert.True(version.TranslatePoint(default, window)!.Value.Y >= pagesBottom);
        // Help's menu: the guide, Discord, and a bug report.
        var menu = Assert.IsType<MenuFlyout>(help.Flyout);
        menu.ShowAt(help);
        UiText.Settle(window);
        Assert.Equal(["User Guide", "Ask on Discord", "Report an issue"], menu.Items.OfType<MenuItem>().Select(i => i.Header as string));
        Assert.Equal([settings.OpenGuideCommand, settings.OpenDiscordCommand, settings.ReportIssueCommand], menu.Items.OfType<MenuItem>().Select(i => i.Command));
        menu.Hide();

        version.Command!.Execute(null);
        UiText.Settle(window);

        Assert.Equal("Copied", version.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.StartsWith("Claudette: 0.1.0 (source build 842169b)", h.Platform.Clipboard, StringComparison.Ordinal);
    }
}
