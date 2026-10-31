using System.Text.Json.Nodes;
using Deventry.Core.Credentials;
using Deventry.Core.FromClaudette;
using Deventry.Core.LoginItems;
using Deventry.Core.ProjectTools;
using Deventry.Core.Tests.Support;
using Deventry.Core.Threads;
using Deventry.Core.Updates;

namespace Deventry.Core.Tests.FromClaudette;

/// <summary>
/// What Deventry carries over from Claudette besides its folders (DESIGN.md §2, "Renaming to Deventry"): its settings'
/// old keys, saved passwords, the login entry, projects' <c>claudette.json</c>, its environment variables, its threads'
/// delivery line and the ready file an old update waits for.
/// </summary>
public sealed class ClaudetteCarryOverTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_settings_named_after_Claudette_are_read_under_Joulenes_names_without_replacing_ones_already_set()
    {
        var file = _temp.Combine("settings.json");
        File.WriteAllText(file, """{ "appearance": { "showClaudette": false, "claudetteSize": "large", "jouleneTips": true, "claudetteTips": false, "theme": "dark" } }""");

        Assert.True(ClaudetteSettings.Upgrade(file));

        var appearance = JsonNode.Parse(File.ReadAllText(file))!["appearance"]!;
        Assert.False((bool)appearance["showJoulene"]!);
        Assert.Equal("large", (string?)appearance["jouleneSize"]);
        Assert.True((bool)appearance["jouleneTips"]!);
        Assert.Equal("dark", (string?)appearance["theme"]);
        Assert.False(ClaudetteSettings.Upgrade(file));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "general": {} }""")]
    public void Settings_it_cant_read_or_without_those_are_left_alone(string text)
    {
        var file = _temp.Combine("settings.json");
        File.WriteAllText(file, text);

        Assert.False(ClaudetteSettings.Upgrade(file));
        Assert.Equal(text, File.ReadAllText(file));
        Assert.False(ClaudetteSettings.Upgrade(_temp.Combine("missing.json")));
    }

    [Fact]
    public async Task A_secret_saved_under_Claudettes_name_moves_to_Deventrys_the_first_time_its_read()
    {
        var Deventrys = new MemoryCredentials();
        var claudettes = new MemoryCredentials();
        await claudettes.WriteAsync("perforce/ssl:p4:1666/matt", "Claudette: Perforce matt @ ssl:p4:1666", "hunter2", Ct);
        var store = new CarriedOverCredentials(Deventrys, claudettes);

        Assert.Equal("hunter2", await store.ReadAsync("perforce/ssl:p4:1666/matt", Ct));

        Assert.Equal("hunter2", Deventrys.Secrets["perforce/ssl:p4:1666/matt"]);
        Assert.Equal("Deventry: Perforce matt @ ssl:p4:1666", Deventrys.Labels["perforce/ssl:p4:1666/matt"]);
        Assert.Empty(claudettes.Secrets);
        Assert.Null(await store.ReadAsync("perforce/other/matt", Ct));
    }

    [Fact]
    public async Task Saving_or_deleting_a_secret_leaves_none_under_Claudettes_name()
    {
        var Deventrys = new MemoryCredentials();
        var claudettes = new MemoryCredentials();
        await claudettes.WriteAsync("claude-api-key/a1", "Claudette: Claude API key (Studio)", "old", Ct);
        await claudettes.WriteAsync("claude-api-key/b2", "Claudette: Claude API key (Home)", "old", Ct);
        var store = new CarriedOverCredentials(Deventrys, claudettes);

        await store.WriteAsync("claude-api-key/a1", "Deventry: Claude API key (Studio)", "new", Ct);
        Assert.True(await store.DeleteAsync("claude-api-key/b2", Ct));

        Assert.Equal("new", await store.ReadAsync("claude-api-key/a1", Ct));
        Assert.Empty(claudettes.Secrets);
        Assert.Equal("Deventry: Claude API key", CarriedOverCredentials.Label("claude-api-key/b2"));
    }

    [Fact]
    public async Task A_login_entry_under_Claudettes_name_is_written_again_under_Deventrys_as_Deventry_starts()
    {
        var copy = new ClaudetteCopy(AppInstallKind.Other, _temp.Combine("tools", "Deventry"), "1.0.0");
        var Deventrys = new FakeLoginItems();
        var claudettes = new FakeLoginItems { Entry = LoginEntryState.Enabled };
        var items = new CarriedOverLoginItems(Deventrys, claudettes);
        var record = new LoginItemRecord { Target = copy, Installed = copy };
        Deventrys.Install(copy);

        Assert.Equal(LoginEntryState.Enabled, items.ReadEntry());
        await new StartAtLogin(items, copy, record, save: () => { }).RefreshAtLaunchAsync(Ct);

        Assert.Equal(copy, Deventrys.EntryStarts);
        Assert.Equal(LoginEntryState.Missing, claudettes.Entry);
        Assert.False(items.HasOnlyClaudettes);
        Assert.Equal(copy, record.Target);
    }

    [Fact]
    public void A_project_with_only_claudette_json_is_read_and_written_there_and_a_new_one_gets_deventry_json()
    {
        var project = Directory.CreateDirectory(_temp.Combine("old-project")).FullName;
        File.WriteAllText(Path.Combine(project, "claudette.json"), """{ "actions": [ { "name": "Test", "command": "dotnet test" } ], "links": "nope" }""");

        var contents = ProjectFile.Read(project, ToolOS.Windows);

        Assert.Equal("Test", Assert.Single(contents.Actions).Name);
        Assert.Contains(contents.Problems, p => p.StartsWith("claudette.json", StringComparison.Ordinal));
        Assert.Equal("claudette.json", ProjectFile.FileName(project, ProjectFileScope.Shared));
        ProjectFile.WriteLinks(project, ProjectFileScope.Shared, []);
        Assert.False(File.Exists(Path.Combine(project, "deventry.json")));

        var fresh = Directory.CreateDirectory(_temp.Combine("new-project")).FullName;
        ProjectFile.WriteLinks(fresh, ProjectFileScope.Local, []);
        Assert.True(File.Exists(Path.Combine(fresh, "deventry.local.json")));

        // With both, Deventry's wins.
        File.WriteAllText(Path.Combine(project, "deventry.json"), """{ "actions": [ { "name": "Build", "command": "dotnet build" } ] }""");
        Assert.Equal("Build", Assert.Single(ProjectFile.Read(project, ToolOS.Windows).Actions).Name);
    }

    [Fact]
    public void Claudettes_names_for_Deventrys_variables_are_read_and_given_too()
    {
        var environment = new Dictionary<string, string?> { ["DEVENTRY_WORKTREE"] = "/w", ["OTHER"] = "x" };
        ClaudetteVariables.AddTo(environment, "DEVENTRY_WORKTREE", "DEVENTRY_MAIN_CHECKOUT");

        Assert.Equal("/w", environment["CLAUDETTE_WORKTREE"]);
        Assert.False(environment.ContainsKey("CLAUDETTE_MAIN_CHECKOUT"));
        var set = new Dictionary<string, string> { ["CLAUDETTE_HOME"] = "/old" };
        Assert.Equal("/old", ClaudetteVariables.Get("DEVENTRY_HOME", set.GetValueOrDefault));
        set["DEVENTRY_HOME"] = "/new";
        Assert.Equal("/new", ClaudetteVariables.Get("DEVENTRY_HOME", set.GetValueOrDefault));
        Assert.Null(ClaudetteVariables.Get("DEVENTRY_RUN_IN_PLACE", set.GetValueOrDefault));
    }

    [Fact]
    public void A_threads_message_Claudette_delivered_still_reads_as_delivered()
    {
        Assert.True(ThreadMessages.IsDeliveryAnswer("Claudette delivered this message to the sub-thread \"Art\", where it starts the next turn."));
        Assert.True(ThreadMessages.IsDeliveryAnswer(ThreadMessages.Delivered("Art", waits: false)));
        Assert.StartsWith("Deventry delivered", ThreadMessages.Delivered("Art", waits: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_an_update_that_didnt_say_where_is_also_told_in_Claudettes_data_folder()
    {
        Assert.SkipWhen(ClaudetteVariables.Get(AppPaths.HomeVariable) is not null, "DEVENTRY_HOME or CLAUDETTE_HOME is set.");

        Assert.Null(ClaudetteReadyFile.AlsoFor(isUpdate: false, readyFileOption: null));
        Assert.Null(ClaudetteReadyFile.AlsoFor(isUpdate: true, readyFileOption: _temp.Combine("ready")));
        Assert.Equal(Path.Combine(ClaudetteFolders.ForCurrentUser().DataDirectory, "restart-ready"), ClaudetteReadyFile.AlsoFor(isUpdate: true, readyFileOption: null));
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public Dictionary<string, string> Labels { get; } = [];

        public string Name => "the test store";

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.GetValueOrDefault(key));

        public Task WriteAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
        {
            Secrets[key] = secret;
            Labels[key] = label;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Secrets.Remove(key) | Labels.Remove(key));
    }
}
