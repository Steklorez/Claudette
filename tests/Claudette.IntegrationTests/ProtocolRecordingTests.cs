using System.Diagnostics;
using Claudette.Core.Files;
using Claudette.Core.Git;
using Claudette.Core.Installation;
using Claudette.Core.Processes;
using Claudette.Core.Sessions;
using Claudette.Fixtures;
using Claudette.IntegrationTests.Support;
using Claudette.MockApi;

namespace Claudette.IntegrationTests;

/// <summary>
/// The protocol scenarios DESIGN.md §15 lists, run against the real <c>claude</c> and the mock Messages API: a denied
/// permission, an interrupt, <c>/clear</c>, compaction, API retries, a subagent's edits and Claude moving into a
/// worktree. Each checks what Claudette sees, and with <c>CLAUDETTE_RECORD_FIXTURES=&lt;folder&gt;</c> set also
/// writes the traffic there as a cleaned protocol fixture
/// ("record mode"), to check in under <c>tests/Claudette.Core.Tests/Fixtures/protocol/&lt;version&gt;/</c>.
/// </summary>
[Trait("Category", "RealCli")]
public sealed class ProtocolRecordingTests : IAsyncLifetime
{
    private readonly TempFolder _root = new("claudette-record");
    private MockAnthropicApi _api = null!;
    private ClaudeSessionFactory? _factory;
    private string? _fixtureName;
    private string? _version;

    private string Work => _root.Combine("repo");

    private string Config => _root.Combine("config");

    private string LogPath => _root.Combine("protocol.log");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Config);
        Process.Start(new ProcessStartInfo("git", ["init", "-q"]) { WorkingDirectory = Work, CreateNoWindow = true })?.WaitForExit(10_000);
        _api = await MockAnthropicApi.StartAsync();
        var located = await new ClaudeLocator(new ProcessLauncher(), TimeProvider.System).LocateAsync(null);
        if (located.IsUsable)
        {
            _factory = new ClaudeSessionFactory(located.Install!.Path, new ProcessLauncher(), TimeProvider.System);
            _version = located.Install.Version.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _api.DisposeAsync();
        if (Environment.GetEnvironmentVariable("CLAUDETTE_RECORD_FIXTURES") is { Length: > 0 } folder && _fixtureName is not null && File.Exists(LogPath))
        {
            var lines = new ProtocolFixtureWriter([_root.Path]).FromProtocolLog(await File.ReadAllLinesAsync(LogPath));
            var target = Path.Combine(folder, _version ?? "unknown", $"{_fixtureName}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllLinesAsync(target, lines);
        }
        _root.Dispose();
    }

    [Fact]
    public async Task A_denied_permission_reaches_claude_as_a_tool_error()
    {
        await using var session = await StartAsync("07-permission-denied");

        await session.SendUserMessageAsync("RUN_BASH touch denied.txt", TestContext.Current.CancellationToken);
        var (requested, _) = await session.ReadUntilAsync<PermissionRequested>();
        requested.Request.Deny("Not in this folder, please.");
        var (done, seen) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("Bash", requested.Request.ToolName);
        Assert.False(File.Exists(Path.Combine(Work, "denied.txt")));
        var denial = seen.OfType<ToolResultsReceived>().SelectMany(r => r.Message.Content.OfType<Core.Protocol.ToolResultBlock>()).Single();
        Assert.True(denial.IsError);
        Assert.Contains("Not in this folder, please.", _api.Requests.Last(r => r.Reply != "title").LastToolResultText, StringComparison.Ordinal);
        Assert.False(done.Result.IsError);
    }

    [Fact]
    public async Task An_interrupt_ends_a_streaming_turn()
    {
        await using var session = await StartAsync("08-interrupt");

        await session.SendUserMessageAsync("SLOW", TestContext.Current.CancellationToken);
        await session.ReadUntilAsync<TextDelta>();
        await session.InterruptAsync(TestContext.Current.CancellationToken);
        var (done, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.True(done.Result.IsError || done.Result.TerminalReason is not null and not "completed");
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Fact]
    public async Task Clear_starts_a_new_conversation_in_the_same_session()
    {
        await using var session = await StartAsync("09-clear");

        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        var (first, _) = await session.ReadUntilAsync<TurnCompleted>();
        await session.SendUserMessageAsync("/clear", TestContext.Current.CancellationToken);
        var (reset, _) = await session.ReadUntilAsync<ConversationReset>();
        // /clear is a turn of its own, with an empty result.
        await session.ReadUntilAsync<TurnCompleted>();
        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        var (second, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("pong", second.Result.Result);
        Assert.NotEqual(first.Result.SessionId, second.Result.SessionId);
        Assert.Equal("clear", reset.Trigger);
    }

    [Fact]
    public async Task Compact_summarizes_and_marks_the_boundary()
    {
        await using var session = await StartAsync("10-compact");

        await session.SendUserMessageAsync("hello", TestContext.Current.CancellationToken);
        await session.ReadUntilAsync<TurnCompleted>();
        await session.SendUserMessageAsync("/compact", TestContext.Current.CancellationToken);
        var (boundary, _) = await session.ReadUntilAsync<SystemNotice>(n => n.Message.Subtype == "compact_boundary");
        await session.ReadUntilAsync<TurnCompleted>();

        Assert.Equal("manual", boundary.Message.Raw["compact_metadata"]?["trigger"]?.GetValue<string>());
    }

    [Fact]
    public async Task Api_errors_are_retried_and_reported()
    {
        await using var session = await StartAsync("11-api-retry");

        await session.SendUserMessageAsync("API_ERROR", TestContext.Current.CancellationToken);
        var (done, seen) = await session.ReadUntilAsync<TurnCompleted>(timeout: TimeSpan.FromSeconds(120));

        Assert.Equal("pong", done.Result.Result);
        Assert.Contains(seen, e => e is SystemNotice { Message.Subtype: "api_retry" });
        Assert.Equal(2, _api.Requests.Count(r => r.Reply == "overloaded"));
    }

    /// <summary>
    /// A subagent's Edit and Write come without their structured result, live and in its transcript, so what the file
    /// held before isn't known from them; a PreToolUse hook sees each one first, with its real path (DESIGN.md §8,
    /// "Subagents' changes"). One in a worktree of its own reports the worktree with its result (checked with 2.1.302).
    /// </summary>
    [Fact]
    public async Task A_subagents_edits_come_without_their_results_but_the_hook_sees_them_first()
    {
        Commit();
        var hooked = new System.Collections.Concurrent.ConcurrentQueue<HookInput>();
        await using var session = await StartAsync("12-subagent-edits", "acceptEdits", [new HookRegistration("PreToolUse", "Edit|Write|MultiEdit|NotebookEdit", (input, _) =>
        {
            hooked.Enqueue(input);
            return Task.FromResult(HookOutputs.Continue());
        })]);
        var existing = Path.Combine(Work, "existing.txt");
        await File.WriteAllTextAsync(existing, "first line\nORIGINAL LINE\nlast line\n", TestContext.Current.CancellationToken);

        var seen = await TurnAsync(session, $"SUBAGENT_WRITE {Path.Combine(Work, "new.txt")}");
        seen.AddRange(await TurnAsync(session, $"SUBAGENT_EDIT {existing}"));
        // A relative path: the model's own, which only the hook sees resolved, in the subagent's worktree.
        seen.AddRange(await TurnAsync(session, "SUBAGENT_WRITE isolated.txt ISOLATED"));

        Assert.Contains("EDITED LINE", await File.ReadAllTextAsync(existing, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var results = seen.OfType<ToolResultsReceived>().Select(r => r.Message).ToArray();
        var fileCalls = seen.OfType<AssistantMessageReceived>().SelectMany(a => a.Message.Content.OfType<Core.Protocol.ToolUseBlock>())
            .Where(t => t.Name is "Write" or "Edit").ToArray();
        Assert.Equal(3, fileCalls.Length);
        foreach (var call in fileCalls)
        {
            var result = results.Single(r => r.Content.OfType<Core.Protocol.ToolResultBlock>().Any(b => b.ToolUseId == call.Id));
            Assert.NotNull(result.ParentToolUseId);
            Assert.Null(result.ToolUseResult);
            var hook = Assert.Single(hooked, h => h.ToolUseId == call.Id);
            Assert.NotNull(hook.Raw["agent_id"]);
            Assert.True(Path.IsPathFullyQualified(hook.ToolInput!["file_path"]!.GetValue<string>()));
        }
        var isolatedHook = hooked.Single(h => h.ToolInput?["file_path"]?.GetValue<string>().EndsWith("isolated.txt", StringComparison.Ordinal) == true);
        var agentResult = results.Select(r => r.ToolUseResult).OfType<System.Text.Json.Nodes.JsonObject>().Single(r => r["worktreePath"] is not null);
        var worktree = agentResult["worktreePath"]!.GetValue<string>();
        Assert.True(RealPath.Same(Path.Combine(worktree, "isolated.txt"), isolatedHook.ToolInput!["file_path"]!.GetValue<string>()));
        Assert.StartsWith("worktree-agent-", agentResult["worktreeBranch"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>
    /// EnterWorktree and ExitWorktree say where the session works from then on, and the next turn's <c>system/init</c>
    /// names it (DESIGN.md §4, "Worktree tabs"; checked with 2.1.302).
    /// </summary>
    [Fact]
    public async Task Claude_moves_into_a_worktree_and_out_again()
    {
        Commit();
        await using var session = await StartAsync("13-worktree-moves", "acceptEdits");

        var entered = await TurnAsync(session, "ENTER_WORKTREE move-check");
        var inside = await TurnAsync(session, "hello");
        var exited = await TurnAsync(session, "EXIT_WORKTREE keep");
        var after = await TurnAsync(session, "hello");

        // 2.1.308 has Claude load both tools with ToolSearch first, whose result comes before.
        var enter = entered.OfType<ToolResultsReceived>().Select(r => r.Message.ToolUseResult).OfType<System.Text.Json.Nodes.JsonObject>().Single(r => r["worktreePath"] is not null);
        var worktree = enter["worktreePath"]!.GetValue<string>();
        Assert.True(RealPath.Same(GitWorktrees.PathFor(Work, "move-check"), worktree));
        Assert.Equal("worktree-move-check", enter["worktreeBranch"]!.GetValue<string>());
        Assert.True(RealPath.Same(worktree, inside.OfType<TurnStarted>().Single().Init.Cwd!));
        var exit = exited.OfType<ToolResultsReceived>().Select(r => r.Message.ToolUseResult).OfType<System.Text.Json.Nodes.JsonObject>().Single(r => r["restoredCwd"] is not null);
        Assert.Equal("keep", exit["action"]!.GetValue<string>());
        Assert.True(RealPath.Same(Work, exit["restoredCwd"]!.GetValue<string>()));
        Assert.True(RealPath.Same(Work, after.OfType<TurnStarted>().Single().Init.Cwd!));
    }

    /// <summary>Sends <paramref name="prompt"/> and reads its turn, allowing whatever asks for permission.</summary>
    private static async Task<List<SessionEvent>> TurnAsync(ClaudeSession session, string prompt)
    {
        await session.SendUserMessageAsync(prompt, TestContext.Current.CancellationToken);
        var seen = new List<SessionEvent>();
        while (true)
        {
            var (match, events) = await session.ReadUntilAsync<SessionEvent>(e => e is TurnCompleted or PermissionRequested, TimeSpan.FromSeconds(90));
            seen.AddRange(events);
            if (match is PermissionRequested requested)
            {
                requested.Request.Allow();
                continue;
            }
            return seen;
        }
    }

    /// <summary>A first commit, which worktrees branch from.</summary>
    private void Commit() =>
        Process.Start(new ProcessStartInfo("git", ["-c", "user.email=t@example.com", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-q", "--allow-empty", "-m", "init"])
            { WorkingDirectory = Work, CreateNoWindow = true })?.WaitForExit(10_000);

    private async Task<ClaudeSession> StartAsync(string fixtureName, string? permissionMode = null, IReadOnlyList<HookRegistration>? hooks = null)
    {
        RealCli.SkipUnlessInstalled(_factory is not null);
        _fixtureName = fixtureName;
        return await _factory!.StartAsync(new ClaudeLaunchOptions
        {
            WorkingDirectory = Work,
            Model = "claude-haiku-4-5",
            PermissionMode = permissionMode,
            Hooks = hooks ?? [],
            ProtocolLogPath = LogPath,
            EnvironmentOverrides = new Dictionary<string, string?>
            {
                ["ANTHROPIC_BASE_URL"] = _api.BaseAddress.ToString().TrimEnd('/'),
                ["ANTHROPIC_API_KEY"] = "sk-ant-mock-000",
                ["ANTHROPIC_AUTH_TOKEN"] = null,
                ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
                ["CLAUDE_CONFIG_DIR"] = Config,
                ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
            },
        }, TestContext.Current.CancellationToken);
    }
}
