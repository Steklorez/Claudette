using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Controls;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>Accounts besides the Default one, rendered: the sidebar's tags and Settings → Claude Code → Accounts (DESIGN.md §4, "Accounts").</summary>
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
        using var settings = new SettingsViewModel(h.Services, "me@example.com") { SelectedCategory = SettingsCategory.ClaudeCode };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        await Verify(DescribeAccounts(window, h));
    }

    [AvaloniaFact]
    public async Task Adding_a_providers_key_says_what_it_exposes_the_key_to_and_asks_to_confirm()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        await h.Services.Accounts.AddCloudAccountAsync("Render farm", Core.Accounts.CloudProviders.Bedrock,
            new Core.Accounts.CloudAccountSettings { SignIn = Core.Accounts.CloudSignIns.Profile, Region = "us-east-1", Profile = "studio-sso", RefreshSignIn = true }, null);
        using var settings = new SettingsViewModel(h.Services, "me@example.com") { SelectedCategory = SettingsCategory.ClaudeCode };
        var accounts = (ViewModels.Settings.ClaudeCodePage)settings.CurrentPage!;
        accounts.NewKind = accounts.KindChoices.Single(c => c.Provider == Core.Accounts.CloudProviders.Foundry);
        accounts.NewCloud!.SignIn = accounts.NewCloud.SignIns.Single(s => s.Value == Core.Accounts.CloudSignIns.ApiKey);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        await Verify(DescribeAccounts(window, h));
    }

    [AvaloniaFact]
    public async Task Adding_an_Ollama_account_says_what_it_is_lists_the_models_and_warns_of_a_small_window()
    {
        // An Ollama that runs its models at 4,096 tokens, as on a 16 GB GPU (DESIGN.md §18, "Ollama and llama.cpp accounts").
        using var ollama = FakeLocalServers.Ollama();
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher(), http: ollama);
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        await h.Services.Accounts.AddLocalAccountAsync("Home", Core.Accounts.LocalServers.Ollama,
            new Core.Accounts.LocalServerSettings { Address = FakeLocalServers.OllamaAddress, Model = "qwen3:8b", Window = 65_536 }, null);
        using var settings = new SettingsViewModel(h.Services, "me@example.com") { SelectedCategory = SettingsCategory.ClaudeCode };
        var accounts = (ViewModels.Settings.ClaudeCodePage)settings.CurrentPage!;
        accounts.NewKind = accounts.KindChoices.Single(c => c.Server == Core.Accounts.LocalServers.Ollama);
        var form = accounts.NewLocal!;
        await form.CheckCommand.ExecuteAsync(null);
        form.SelectedModel = form.ModelChoices[0];
        await form.CheckWindowCommand.ExecuteAsync(null);
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);

        await Verify(DescribeAccounts(window, h));
    }

    /// <summary>
    /// The Claude Code page's Accounts and Add an account, without the rest of the page, whose Starting a session has a
    /// setting only macOS and Linux show.
    /// </summary>
    private static string DescribeAccounts(Window window, TabTestHarness h) => string.Concat(window.GetVisualDescendants().OfType<SettingsSection>()
        .Where(s => s.Key is "accounts" or "addAccount").Select(s => UiText.Describe(s, (h.Root, "{root}"))));

    private static Border Tag(Window window, string name) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name && b.FindAncestorOfType<ScrollViewer>()?.IsVisible == true);
}
