using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>Accounts besides the Default one, rendered: the sidebar's tags and Settings → Accounts (DESIGN.md §4, "Accounts").</summary>
public class AccountsUiTests
{
    [AvaloniaFact]
    public async Task A_groups_account_tags_its_label_and_a_tab_on_another_account_tags_its_row()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        var work = await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work");
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });

        // A Default group has no tag, and its Default tab none either.
        Assert.False(Tag(window, "GroupAccountTag").IsVisible);
        Assert.False(Tag(window, "TabAccountTag").IsVisible);

        h.Shell.SetGroupAccount(h.Shell.Groups.Single(), work.Id);
        UiText.Settle(window);

        var groupTag = Tag(window, "GroupAccountTag");
        Assert.True(groupTag.IsVisible);
        Assert.Equal("Work", groupTag.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        // The open tab kept Default, which its row now says.
        var tabTag = Tag(window, "TabAccountTag");
        Assert.True(tabTag.IsVisible);
        Assert.Contains("default", tabTag.Classes);
        Assert.Equal("Default", tabTag.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.False(tab.RowAccountTag?.Brush is not null);
    }

    [AvaloniaFact]
    public async Task Settings_lists_each_account_with_its_color_spend_warnings_and_key()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        var work = await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work");
        work.DailySpendAlert = 20;
        using var settings = new SettingsViewModel(h.Services, "me@example.com") { SelectedCategory = SettingsCategory.Accounts };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        var page = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => Grid.GetColumn(s) == 1);

        await Verify(UiText.Describe(page, (h.Root, "{root}")));
    }

    private static Border Tag(Window window, string name) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name && b.FindAncestorOfType<ScrollViewer>()?.IsVisible == true);
}
