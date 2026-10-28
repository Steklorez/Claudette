using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.ViewModels.Settings;
using Claudette.App.Views;
using Claudette.Core.ProjectTools;
using Claudette.Core.Settings;

namespace Claudette.App.UiTests;

/// <summary>
/// The selected tab's project in the Settings window, rendered (DESIGN.md §14, "The project's pages"): the group at the
/// sidebar's foot, and the Links page with its dialog.
/// </summary>
public class ProjectSettingsUiTests
{
    private static SettingsWindow ShowSettings(TabTestHarness h, TabViewModel tab, string? category, out SettingsViewModel settings, double height = 700)
    {
        settings = new SettingsViewModel(h.Services, "me@example.com", opening: new SettingsOpening(category, new ProjectSettingsViewModel(h.Services, tab, h.Shell)));
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = height };
        window.Show();
        UiText.Settle(window);
        return window;
    }

    private static async Task<TabViewModel> OpenTabAsync(TabTestHarness h, bool project)
    {
        var tab = await h.OpenTabAsync();
        if (project)
        {
            await Waiting.UntilAsync(() => tab.ProjectTools.Project is not null, "the project", poll: () => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        }
        Assert.Equal(project, tab.ProjectTools.Project is not null);
        return tab;
    }

    /// <summary>
    /// The selected tab's project sits at the sidebar's foot (DESIGN.md §14, "The project's pages"), in view however far
    /// the categories scroll: under a line across the sidebar, headed Selected project and its name after its group's
    /// color, with its folder in the name's tooltip, above a line over the version. Searching hides it with the
    /// categories.
    /// </summary>
    [AvaloniaFact]
    public async Task The_selected_tabs_project_sits_at_the_sidebars_foot_under_a_line_in_either_style()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        UnrealFixture.Write(h.Root, h.WorkFolder);
        var tab = await OpenTabAsync(h, project: true);
        // Tall enough for the whole list: one that scrolls realizes only the rows in view, in no particular order.
        var window = ShowSettings(h, tab, null, out var settings, height: 1100);

        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        var projectList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ProjectList");
        var group = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ProjectGroup");
        var foot = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "SidebarFoot");
        var heading = group.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ProjectHeading");
        Assert.True(group.IsEffectivelyVisible);
        Assert.Equal("NightOwl", heading.Text);
        Assert.Equal(h.WorkFolder, ToolTip.GetTip((Control)heading.Parent!) as string);
        Assert.Contains(group.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Selected project" && t.IsEffectivelyVisible);
        var dot = group.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
        Assert.True(dot.IsEffectivelyVisible);
        Assert.Equal(h.Shell.Groups.Single(g => g.Tabs.Contains(tab)).Color, Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);

        // Under the categories and over the version, each with a line across the sidebar above it.
        var sidebar = foot.FindAncestorOfType<DockPanel>()!;
        double Top(Visual v) => v.TranslatePoint(default, sidebar)!.Value.Y;
        Assert.True(Top(group) >= Top(list) + list.Bounds.Height);
        Assert.True(Top(foot) >= Top(group) + group.Bounds.Height);
        Assert.Equal(new Thickness(0, 1, 0, 0), group.BorderThickness);
        Assert.Equal(new Thickness(0, 1, 0, 0), Assert.IsType<Border>(foot.Parent).BorderThickness);
        Assert.Equal(sidebar.Bounds.Width, group.Bounds.Width);
        Assert.Equal("General", settings.SelectedNavEntry!.Title);
        Assert.Null(projectList.SelectedItem);

        projectList.SelectedItem = settings.ProjectNavEntries.Single(e => e.Title == SettingsViewModel.ToolsPage);
        UiText.Settle(window);
        Assert.IsType<ProjectToolChoicesPage>(settings.CurrentPage);
        Assert.Null(list.SelectedItem);
        var tools = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ToolsPage");
        var toolsText = UiText.Describe(tools, (h.Root, "{root}"));
        Assert.Contains("\"Engine folder\"", toolsText, StringComparison.Ordinal);
        Assert.Contains("[choice] \"Development\"", toolsText, StringComparison.Ordinal);
        list.SelectedItem = settings.NavEntries.Single(e => e.Title == "Appearance");
        UiText.Settle(window);
        Assert.Same(settings.Appearance, settings.CurrentPage);
        Assert.Null(projectList.SelectedItem);

        // Searching lists results in the categories' place, and the project's pages go with them.
        settings.SearchText = "font";
        UiText.Settle(window);
        Assert.False(group.IsEffectivelyVisible);
        settings.SearchText = "";
        UiText.Settle(window);
        Assert.True(group.IsEffectivelyVisible);

        // The Claude style, dark: the group is drawn with the app's own tokens, which the style repaints.
        var app = (App)Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            app.Colors.Apply(AppStyle.Claude);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            UiText.Settle(window);
            Assert.Equal(((ISolidColorBrush)window.FindResource(ThemeVariant.Dark, "MutedTextBrush")!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(heading.Foreground).Color);
            Assert.Equal(((ISolidColorBrush)window.FindResource(ThemeVariant.Dark, "DividerBrush")!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(group.BorderBrush).Color);
        }
        finally
        {
            app.Colors.Apply(AppStyle.Standard);
            app.RequestedThemeVariant = before;
            UiText.Settle(window);
        }

        // What the sidebar holds: every group's pages, then the project's.
        var shown = UiText.Describe(list) + Environment.NewLine + UiText.Describe(group);
        window.Close();
        await Verify(shown);
    }

    /// <summary>
    /// The arrow keys go through every page (DESIGN.md §14): Down from the last category goes on into the project's
    /// pages at the foot, and Up from their first comes back.
    /// </summary>
    [AvaloniaFact]
    public async Task The_arrow_keys_go_from_the_last_category_into_the_projects_pages_and_back()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await OpenTabAsync(h, project: false);
        var window = ShowSettings(h, tab, SettingsCategory.Perforce, out var settings);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        var projectList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ProjectList");
        list.ScrollIntoView(settings.SelectedNavEntry!);
        UiText.Settle(window);
        list.ContainerFromItem(settings.SelectedNavEntry!)!.Focus();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal(SettingsViewModel.LinksPage, settings.SelectedCategory);
        Assert.True(projectList.ContainerFromItem(settings.SelectedProjectNavEntry!)!.IsFocused);

        // Within the project's pages, the list's own keys; then back up into the categories.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Equal(SettingsViewModel.ActionsPage, settings.SelectedCategory);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal(SettingsCategory.Perforce, settings.SelectedCategory);
        Assert.True(list.ContainerFromItem(settings.SelectedNavEntry!)!.IsFocused);
        window.Close();
    }

    /// <summary>
    /// Each kind of project has an icon of its own among the app's (DESIGN.md §18, "Project types"), for its page in
    /// Project tools and the sidebar's project row, and so do All tools and a folder without a project.
    /// </summary>
    [AvaloniaFact]
    public async Task Each_project_type_has_an_icon_of_its_own_in_the_apps_resources()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        List<string> keys = [.. settings.ProjectTypes.Select(p => p.IconKey), ProjectTypeIcons.Folder, ProjectTypeIcons.Other, settings.AllTools.IconKey];

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.True(Application.Current!.TryFindResource(key, out var icon) && icon is Geometry, key));
    }

    /// <summary>
    /// A project type switched off says <i>Off</i> after its name in the sidebar (DESIGN.md §14), which a screen reader
    /// reads as its status; its page can still be picked.
    /// </summary>
    [AvaloniaFact]
    public async Task A_project_type_switched_off_has_Off_after_its_name_and_can_still_be_picked()
    {
        await using var h = new TabTestHarness(s => s.ProjectTools.Type(Core.ProjectTools.Godot.GodotProvider.KindId).Enabled = false, dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 1000 };
        window.Show();
        UiText.Settle(window);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        ListBoxItem Item(string title) => list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => AutomationProperties.GetName(i) == title);

        var godot = Item("Godot");
        var tag = godot.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("settingchip"));
        Assert.True(tag.IsEffectivelyVisible);
        Assert.Equal("Off", tag.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.Equal("Off", AutomationProperties.GetItemStatus(godot));
        Assert.True(godot.Focusable);
        var title = godot.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Godot" && t.IsEffectivelyVisible);
        Assert.True(tag.TranslatePoint(default, godot)!.Value.X > title.TranslatePoint(new Point(title.Bounds.Width, 0), godot)!.Value.X);
        Assert.False(Item("Unity").GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("settingchip")).IsEffectivelyVisible);

        list.SelectedItem = settings.NavEntries.Single(e => e.Title == "Godot");
        UiText.Settle(window);
        Assert.Same(settings.ProjectType(Core.ProjectTools.Godot.GodotProvider.KindId), settings.CurrentPage);
        window.Close();
    }

    [AvaloniaFact]
    public async Task With_no_tab_the_sidebar_has_no_project_group()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        Assert.Empty(settings.ProjectNavEntries);
        Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ProjectGroup").IsVisible);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SidebarList");
        // The list draws only the rows in view, and reuses them as it scrolls: the headings drawn at the top and at the end.
        var headings = new HashSet<string>(StringComparer.Ordinal);
        void Drawn() => headings.UnionWith(list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("navheading") && t.IsEffectivelyVisible).Select(t => t.Text ?? ""));
        Drawn();
        list.ScrollIntoView(settings.NavEntries[^1]);
        UiText.Settle(window);
        Drawn();
        Assert.Equal(new[] { "Claudette", "Claude", "Your work", SettingsSidebar.ProjectTools, SettingsSidebar.SourceControl }.Order(StringComparer.Ordinal), headings.Order(StringComparer.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_Links_page_lists_both_files_links_and_its_dialog_checks_the_address()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.SharedName), """
            {
              "actions": [ { "name": "Run tests", "command": "make test" } ],
              "links": [
                { "name": "Board", "url": "https://example.atlassian.net/jira/software/projects/ABC/boards/1" },
                { "name": "Pull request", "url": "https://github.com/org/repo/compare/{branch}?expand=1" },
              ],
            }
            """);
        File.WriteAllText(Path.Combine(h.WorkFolder, ProjectFile.LocalName), """
            { "links": [ { "name": "Notes", "url": "file:///home/me/notes.txt" }, { "url": "mailto:me@example.com" } ] }
            """);
        var tab = await OpenTabAsync(h, project: false);
        var window = ShowSettings(h, tab, SettingsViewModel.LinksPage, out var settings);
        var page = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "LinksPage");
        Assert.True(page.IsEffectivelyVisible);
        var shown = UiText.Describe(page, (h.Root, "{root}"));

        settings.Project!.AddLinkCommand.Execute(null);
        UiText.Settle(window);
        var dialog = window.GetVisualDescendants().OfType<ProjectLinkEditorView>().Single();
        Assert.True(dialog.IsEffectivelyVisible);
        var address = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Address");
        address.Text = "javascript:alert(1)";
        UiText.Settle(window);
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save");
        Assert.False(save.IsEffectivelyEnabled);
        var dialogText = UiText.Describe(dialog, (h.Root, "{root}"));
        Assert.Contains("\"\"javascript:\" links don't open from Claudette; only https, http and mailto do.\"", dialogText, StringComparison.Ordinal);

        address.Text = "https://example.com/{folderName}";
        UiText.Settle(window);
        Assert.True(save.IsEffectivelyEnabled);
        save.Command!.Execute(null);
        UiText.Settle(window);
        Assert.False(settings.Project.HasEditor);
        Assert.Equal("https://example.com/{folderName}", settings.Project.Links[^1].Url);

        window.Close();
        await Verify($"{shown}\n--- Add a link, with an address that won't open ---\n{dialogText}");
    }

    [AvaloniaFact]
    public async Task The_action_dialog_asks_what_must_exist_and_scrolls_to_Save_in_a_window_at_its_opening_size()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await OpenTabAsync(h, project: false);
        var window = ShowSettings(h, tab, SettingsViewModel.ActionsPage, out var settings);
        window.Width = 820;
        window.Height = 650;

        // From the project's menu: the tallest the dialog gets, as it asks for the file too.
        settings.Project!.StartNewAction();
        UiText.Settle(window);
        var dialog = window.GetVisualDescendants().OfType<ProjectActionEditorView>().Single();
        Assert.True(dialog.IsEffectivelyVisible);
        // Nothing is cut off: its title is in the window at the top, and Save once scrolled to the end.
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "EditorScroller");
        var title = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Add an action");
        Assert.InRange(title.TranslatePoint(default, window)!.Value.Y, 0, window.ClientSize.Height);
        scroller.ScrollToEnd();
        UiText.Settle(window);
        var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save");
        Assert.InRange(save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y, 0, window.ClientSize.Height);
        var ifExists = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Only show when this exists");
        Assert.Equal("Always shown", ifExists.PlaceholderText);

        h.Platform.FileToPick = Path.Combine(h.WorkFolder, "Build", "Game.exe");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Browse…").Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal("Build/Game.exe", ifExists.Text);

        window.Close();
    }
}
