using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Accounts;

namespace Deventry.App.Tests;

/// <summary>Opening Deventry with no Claude sign-in while there's an Ollama or llama.cpp account (DESIGN.md §18, "Without a Claude account").</summary>
public class LocalAccountSignInTests
{
    /// <summary>A claude that's installed and signed out, as a launch finds it.</summary>
    private static TabTestHarness SignedOut(FakeLauncher launcher, System.Net.Http.HttpMessageHandler? http = null)
    {
        launcher.OnStart = (spec, process) =>
        {
            switch (spec.Arguments)
            {
                case ["--version"]:
                    process.WriteOutput("2.1.332 (Claude Code)");
                    process.Exit(0);
                    break;
                case ["auth", "status", ..]:
                    process.WriteOutput("""{"loggedIn": false, "authMethod": "none", "apiProvider": "firstParty"}""");
                    process.Exit(1);
                    break;
            }
        };
        var h = new TabTestHarness(launcher: launcher, http: http);
        var claude = Path.Combine(h.Root, "bin", OperatingSystem.IsWindows() ? "claude.exe" : "claude");
        Directory.CreateDirectory(Path.GetDirectoryName(claude)!);
        File.WriteAllText(claude, "");
        h.Services.Settings.ClaudeCode.ClaudePath = claude;
        return h;
    }

    [Fact]
    public async Task Signed_out_without_one_the_sign_in_screen_offers_Ollama_or_llama_cpp_instead()
    {
        await using var h = SignedOut(new FakeLauncher());
        var main = new MainWindowViewModel(h.Services);

        await main.StartAsync();

        var signIn = Assert.IsType<SignInViewModel>(main.CurrentPage);
        var offer = signIn.LocalAccount!;
        Assert.Equal("Use Ollama or llama.cpp instead", LocalAccountOfferViewModel.OfferText);
        Assert.False(offer.IsOpen);
        Assert.Equal(["Ollama", "llama.cpp"], offer.Kinds.Select(k => k.Label));
        Assert.Same(LocalServers.Ollama, offer.Form!.Kind);
    }

    [Fact]
    public async Task Signed_out_with_one_Deventry_opens_its_tabs_without_asking_for_plan_usage()
    {
        await using var h = SignedOut(new FakeLauncher());
        await h.Services.Accounts.AddLocalAccountAsync("Home", LocalServers.Ollama,
            new LocalServerSettings { Address = FakeLocalServers.OllamaAddress, Model = "qwen3:8b" }, null);
        var main = new MainWindowViewModel(h.Services);

        await main.StartAsync();

        Assert.IsType<ShellViewModel>(main.CurrentPage);
        Assert.True(main.Account.IsSignedOut);
        Assert.Equal("Not signed in", main.Account.Summary);
        Assert.False(main.Shell!.NeedsSignIn);
        // Only claude auth status ran: nothing asked for the plan's usage.
        Assert.DoesNotContain(h.Factory.Launches, l => l.WorkingDirectory == h.Services.Paths.UtilityDirectory);
    }

    [Fact]
    public async Task The_sign_in_screen_adds_one_makes_it_what_new_folders_use_and_opens_the_tabs()
    {
        using var ollama = FakeLocalServers.Ollama();
        await using var h = SignedOut(new FakeLauncher(), ollama);
        var main = new MainWindowViewModel(h.Services);
        await main.StartAsync();
        var offer = Assert.IsType<SignInViewModel>(main.CurrentPage).LocalAccount!;

        offer.OpenCommand.Execute(null);
        Assert.True(offer.IsOpen);
        await offer.AddCommand.ExecuteAsync(null);
        Assert.Equal("Choose the model.", offer.Status);

        await offer.Form!.CheckCommand.ExecuteAsync(null);
        offer.Form.SelectedModel = offer.Form.ModelChoices[0];
        await offer.AddCommand.ExecuteAsync(null);

        await TabTestHarness.Eventually(() => main.CurrentPage is ShellViewModel, "the tabs to open");
        var home = Assert.Single(h.Services.Accounts.All);
        // Named after its server when given no name.
        Assert.Equal("Ollama", home.Name);
        Assert.Equal("qwen3:8b", home.Local!.Model);
        Assert.Equal(home.Id, h.Services.Accounts.NewFolderAccount);
    }
}
