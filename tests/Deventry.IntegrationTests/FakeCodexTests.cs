using System.Text.Json.Nodes;
using Deventry.Core.Codex;
using Deventry.Core.Processes;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.IntegrationTests.Support;

namespace Deventry.IntegrationTests;

/// <summary>
/// A Codex session against <c>fake-codex</c>: a real child process and pipes speaking <c>codex app-server</c>'s JSON-RPC,
/// no Codex needed (DESIGN.md §18, "Codex, a second agent").
/// </summary>
public sealed class FakeCodexTests : IDisposable
{
    private readonly TempFolder _work = new();
    private readonly TempFolder _home = new();
    private readonly CodexSessionFactory _factory = new(FakeCodex.Path, new ProcessLauncher(), TimeProvider.System);

    public void Dispose()
    {
        _work.Dispose();
        _home.Dispose();
    }

    private Task<CodexSession> StartAsync(string mode = CodexModes.Auto, string? resume = null) => _factory.StartAsync(new CodexLaunchOptions
    {
        Start = new CodexStartOptions { WorkingDirectory = _work.Path, Mode = mode, Resume = resume },
        ConfigOverrides = [CodexArguments.ClaudeMdFallback],
        EnvironmentOverrides = new Dictionary<string, string?> { ["CODEX_HOME"] = _home.Path },
    }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_turn_streams_and_completes()
    {
        await using var session = await StartAsync();

        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        var (done, seen) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("pong: hello", done.Result.Result);
        Assert.Equal("pong: hello", string.Concat(seen.OfType<TextDelta>().Select(d => d.Text)));
        Assert.Contains(session.Initialization!.Models, m => m.Value == "gpt-fake-1");
        Assert.Equal("0.169.0", session.Version);
        Assert.NotNull(session.SessionId);
        Assert.True(File.Exists(session.ThreadPath));
        Assert.Equal((400, 800, 40), (done.Usage.Models["gpt-fake-1"].Input, done.Usage.Models["gpt-fake-1"].CacheRead, done.Usage.Models["gpt-fake-1"].Output));
    }

    [Fact]
    public async Task A_resumed_thread_reads_back_its_turns_and_is_in_history()
    {
        string thread;
        await using (var first = await StartAsync())
        {
            thread = first.SessionId!;
            await first.SendUserMessageAsync("RUN git status", TestContext.Current.CancellationToken);
            await first.ReadUntilAsync<TurnCompleted>();
        }
        await using var session = await StartAsync(resume: thread);

        var turns = await session.ReadTurnsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(thread, session.SessionId);
        Assert.Equal("RUN git status", Assert.IsType<PromptReplayed>(turns[0].Event).Message.PromptText);
        var call = turns.Select(t => t.Event).OfType<AssistantMessageReceived>().SelectMany(a => a.Message.Content.OfType<ToolUseBlock>()).Single();
        Assert.Equal((ToolKind.Command, "git status"), (call.Call.Kind, call.Call.Command));
        Assert.Contains(turns, t => t.Event is ToolResultsReceived);
        var summary = Assert.Single(Core.History.CodexHistory.Scan(_home.Path, TestContext.Current.CancellationToken));
        Assert.Equal((thread, "RUN git status"), (summary.SessionId, summary.FirstPrompt));
    }

    [Fact]
    public async Task It_is_found_by_its_version_and_signs_itself_in_and_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var located = await new Core.Installation.CodexLocator(new ProcessLauncher(), TimeProvider.System).LocateAsync(FakeCodex.Path, ct);
        Assert.Null(located.Problem);
        Assert.Equal(new Version(0, 169, 0), located.Install!.Version);
        var auth = new Core.Auth.CodexAuth(FakeCodex.Path, new ProcessLauncher(), TimeProvider.System,
            environmentOverrides: new Dictionary<string, string?> { ["CODEX_HOME"] = _home.Path });

        await auth.LogoutAsync(ct);
        var signedOut = await auth.GetStatusAsync(ct);
        var signedIn = await auth.LoginAsync(cancellationToken: ct);

        Assert.Equal((false, "Not logged in"), (signedOut.SignedIn, signedOut.Text));
        Assert.Equal((true, "Logged in using ChatGPT"), (signedIn.SignedIn, signedIn.Text));
    }

    [Theory]
    [InlineData("none", null, true)]
    [InlineData("mxc,unelevated", "mxc", true)]
    [InlineData("unelevated", "unelevated", true)]
    [InlineData("elevated", null, false)]
    public async Task On_Windows_a_tab_takes_the_first_sandbox_thats_ready(string ready, string? chosen, bool sandboxReady)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Codex's sandboxes to choose from are Windows'.");
        var record = Path.Combine(_home.Path, "launch.json");

        await using var session = await _factory.StartAsync(new CodexLaunchOptions
        {
            Start = new CodexStartOptions { WorkingDirectory = _work.Path },
            EnvironmentOverrides = new Dictionary<string, string?> { ["CODEX_HOME"] = _home.Path, ["FAKE_CODEX_SANDBOX"] = ready, ["FAKE_CODEX_RECORD"] = record },
        }, TestContext.Current.CancellationToken);

        var args = JsonNode.Parse(await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken))!["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
        Assert.Equal(sandboxReady, session.SandboxReady);
        Assert.Equal(chosen is null ? [] : [$"windows.sandbox=\"{chosen}\""], args.Where(a => a.StartsWith("windows.sandbox", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_thread_it_doesnt_have_is_missing()
    {
        var missing = await Assert.ThrowsAsync<CodexThreadMissingException>(() => StartAsync(resume: "01a1eb89-0000-0000-0000-000000000000"));

        Assert.Equal("01a1eb89-0000-0000-0000-000000000000", missing.ThreadId);
    }

    [Fact]
    public async Task A_change_to_a_file_is_a_call_with_its_hunk()
    {
        var path = Path.Combine(_work.Path, "notes.md");
        await File.WriteAllTextAsync(path, "# Notes\n", TestContext.Current.CancellationToken);
        await using var session = await StartAsync();

        await session.SendUserMessageAsync($"EDIT_FILE {path}", TestContext.Current.CancellationToken);
        var (_, seen) = await session.ReadUntilAsync<TurnCompleted>();

        var edit = seen.OfType<AssistantMessageReceived>().SelectMany(a => a.Message.Content.OfType<ToolUseBlock>()).Single();
        Assert.Equal((ToolKind.Edit, path), (edit.Call.Kind, edit.Call.Path));
        Assert.Equal("# Notes\n", Core.Diffs.UnifiedHunks.Before(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), edit.Call.Hunks));
    }

    [Fact]
    public async Task An_approval_is_asked_and_answered()
    {
        await using var session = await StartAsync();

        await session.SendUserMessageAsync("ASK_PERMISSION", TestContext.Current.CancellationToken);
        var (requested, _) = await session.ReadUntilAsync<PermissionRequested>();
        Assert.Equal("git push", requested.Request.Call.Command);
        requested.Request.Allow();
        var (done, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("allowed", done.Result.Result);
    }

    [Fact]
    public async Task A_slow_turn_is_stopped()
    {
        await using var session = await StartAsync();

        await session.SendUserMessageAsync("SLOW", TestContext.Current.CancellationToken);
        await session.ReadUntilAsync<TextDelta>();
        await session.InterruptAsync(TestContext.Current.CancellationToken);
        var (done, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("aborted_streaming", done.Result.TerminalReason);
    }

    [Fact]
    public async Task Its_launch_has_neither_agents_session_variables_and_reads_claude_md()
    {
        var record = Path.Combine(_work.Path, "record.json");
        await using var session = await _factory.StartAsync(new CodexLaunchOptions
        {
            Start = new CodexStartOptions { WorkingDirectory = _work.Path },
            ConfigOverrides = [CodexArguments.ClaudeMdFallback],
            EnvironmentOverrides = new Dictionary<string, string?>
            {
                ["CODEX_HOME"] = _home.Path,
                ["FAKE_CODEX_RECORD"] = record,
                // As if Deventry had been started from inside a Codex session.
                ["CODEX_THREAD_ID"] = null,
            },
        }, TestContext.Current.CancellationToken);

        var recorded = JsonNode.Parse(await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken))!.AsObject();
        Assert.Equal(["app-server", "-c", CodexArguments.ClaudeMdFallback], recorded["args"]!.AsArray().Select(a => a!.GetValue<string>()));
        var env = recorded["env"]!.AsObject();
        Assert.DoesNotContain(env, e => Core.Claude.ClaudeEnvironment.SessionVariables.Contains(e.Key));
    }
}
