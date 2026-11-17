using System.Text.Json.Nodes;
using Deventry.Core.Codex;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Codex;

/// <summary>
/// A Codex tab's session over <c>codex app-server</c> (DESIGN.md §18, "Codex, a second agent"): the test plays Codex's
/// side of the JSON-RPC, with the shapes the spike recorded against Codex 0.169.0.
/// </summary>
public sealed class CodexSessionTests
{
    private const string Thread = "01a1eb89-43e5-71e3-8bc3-b1e0d1c53852";
    private const string Turn = "01a1eb89-447c-72e1-8cb0-782d932971f2";

    private readonly FakeTransport _transport = new();
    private readonly FakeTimeProvider _time = new();

    public CodexSessionTests()
    {
        _transport.AutoRespondRpc["initialize"] = _ => new JsonObject { ["userAgent"] = "deventry/0.169.0 (Windows 10.0.26300; x86_64)", ["platformOs"] = "windows" };
        _transport.AutoRespondRpc["model/list"] = _ => new JsonObject
        {
            ["data"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "gpt-6.1-sol",
                    ["model"] = "gpt-6.1-sol",
                    ["displayName"] = "GPT-6.1-Sol",
                    ["description"] = "Latest workhorse model",
                    ["hidden"] = false,
                    ["supportedReasoningEfforts"] = new JsonArray(new JsonObject { ["reasoningEffort"] = "low" }, new JsonObject { ["reasoningEffort"] = "high" }),
                },
                new JsonObject { ["id"] = "hidden-one", ["displayName"] = "Hidden", ["hidden"] = true }),
        };
        _transport.AutoRespondRpc["account/read"] = _ => new JsonObject { ["account"] = new JsonObject { ["type"] = "chatgpt", ["email"] = "dev@example.com", ["planType"] = "pro" } };
        _transport.AutoRespondRpc["thread/start"] = _ => new JsonObject
        {
            ["thread"] = new JsonObject { ["id"] = Thread, ["path"] = $"C:\\codex\\sessions\\rollout-{Thread}.jsonl" },
            ["model"] = "gpt-6.1-sol",
            ["reasoningEffort"] = "low",
        };
        _transport.AutoRespondRpc["turn/start"] = _ => new JsonObject { ["turn"] = new JsonObject { ["id"] = Turn, ["status"] = "inProgress" } };
        _transport.AutoRespondRpc["thread/settings/update"] = _ => [];
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<CodexSession> StartedAsync(string mode = CodexModes.Auto)
    {
        var session = new CodexSession(_transport, _time);
        await session.StartAsync(new CodexStartOptions { WorkingDirectory = @"C:\Demo\starfall", Mode = mode, ClientVersion = "1.2.0" }, Token);
        return session;
    }

    private void Notify(string method, JsonObject parameters) => _transport.EmitJson(new JsonObject { ["method"] = method, ["params"] = parameters });

    private void TurnStarted() => Notify("turn/started", new JsonObject { ["threadId"] = Thread, ["turn"] = new JsonObject { ["id"] = Turn, ["status"] = "inProgress" } });

    private void TurnCompleted(string status = "completed", string? error = null) => Notify("turn/completed", new JsonObject
    {
        ["threadId"] = Thread,
        ["turn"] = new JsonObject { ["id"] = Turn, ["status"] = status, ["error"] = error is null ? null : new JsonObject { ["message"] = error }, ["durationMs"] = 406 },
    });

    private void Item(string method, JsonObject item, string thread = Thread) => Notify(method, new JsonObject { ["threadId"] = thread, ["turnId"] = Turn, ["item"] = item });

    [Fact]
    public async Task Starting_says_who_Deventry_is_and_starts_a_thread_as_the_mode_says()
    {
        await using var session = await StartedAsync();

        var initialize = _transport.Sent.First(s => s.GetString("method") == "initialize");
        Assert.Equal("deventry", initialize["params"]!["clientInfo"]!["name"]!.GetValue<string>());
        Assert.True(initialize["params"]!["capabilities"]!["experimentalApi"]!.GetValue<bool>());
        Assert.Contains(_transport.Sent, s => s.GetString("method") == "initialized" && s["id"] is null);
        var start = _transport.Sent.First(s => s.GetString("method") == "thread/start")["params"]!.AsObject();
        Assert.Equal(@"C:\Demo\starfall", start.GetString("cwd"));
        Assert.Equal("on-request", start.GetString("approvalPolicy"));
        Assert.Equal("workspace-write", start.GetString("sandbox"));

        Assert.Equal(Thread, session.SessionId);
        Assert.Equal("gpt-6.1-sol", session.Model);
        Assert.Equal("0.169.0", session.Version);
        Assert.Equal(SessionState.Idle, session.State);
        var model = Assert.Single(session.Initialization!.Models);
        Assert.Equal(["low", "high"], model.SupportedEffortLevels);
        Assert.Equal("dev@example.com", session.Initialization.Account.Email);
        Assert.Equal("Codex", session.Agent.Name);
    }

    [Fact]
    public async Task Resuming_resumes_the_thread_by_its_id()
    {
        _transport.AutoRespondRpc["thread/resume"] = p => new JsonObject { ["thread"] = new JsonObject { ["id"] = p.GetString("threadId") }, ["model"] = "gpt-6.1-sol" };
        await using var session = new CodexSession(_transport, _time);

        await session.StartAsync(new CodexStartOptions { WorkingDirectory = "/repo", Resume = Thread, Mode = CodexModes.FullAccess }, Token);

        var resume = _transport.Sent.First(s => s.GetString("method") == "thread/resume")["params"]!.AsObject();
        Assert.Equal(Thread, resume.GetString("threadId"));
        Assert.Equal("never", resume.GetString("approvalPolicy"));
        Assert.Equal("danger-full-access", resume.GetString("sandbox"));
        Assert.DoesNotContain(_transport.Sent, s => s.GetString("method") == "thread/start");
        Assert.Equal(Thread, session.SessionId);
    }

    [Fact]
    public async Task A_turn_streams_its_reply_and_ends_with_what_it_spent()
    {
        await using var session = await StartedAsync();

        await session.SendUserMessageAsync("hello", [], null, new MessageStamp("msg-1", true), Token);
        var turnStart = _transport.Sent.First(s => s.GetString("method") == "turn/start")["params"]!.AsObject();
        Assert.Equal("hello", turnStart["input"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("msg-1", turnStart.GetString("clientUserMessageId"));
        Assert.Equal(SessionState.Working, session.State);

        TurnStarted();
        Item("item/completed", new JsonObject { ["type"] = "userMessage", ["id"] = "u1", ["clientId"] = "msg-1", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "hello" }) });
        Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = Thread, ["itemId"] = "m1", ["delta"] = "po" });
        Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = Thread, ["itemId"] = "m1", ["delta"] = "ng" });
        Item("item/completed", new JsonObject { ["type"] = "agentMessage", ["id"] = "m1", ["text"] = "pong" });
        Notify("thread/tokenUsage/updated", new JsonObject
        {
            ["threadId"] = Thread,
            ["turnId"] = Turn,
            ["tokenUsage"] = new JsonObject
            {
                ["total"] = new JsonObject { ["inputTokens"] = 1200, ["cachedInputTokens"] = 800, ["cacheWriteInputTokens"] = 0, ["outputTokens"] = 40, ["reasoningOutputTokens"] = 12 },
                ["last"] = new JsonObject { ["inputTokens"] = 1200, ["cachedInputTokens"] = 800, ["outputTokens"] = 40 },
                ["modelContextWindow"] = 258400,
            },
        });
        TurnCompleted();

        var (completed, seen) = await session.ReadUntilAsync<TurnCompleted>();
        Assert.Contains(seen, e => e is TurnStarted);
        Assert.Equal("msg-1", seen.OfType<PromptReplayed>().Single().Message.Uuid);
        Assert.Equal("pong", string.Concat(seen.OfType<TextDelta>().Select(d => d.Text)));
        Assert.Equal("pong", seen.OfType<AssistantMessageReceived>().Single().Message.Content.OfType<TextBlock>().Single().Text);
        Assert.False(completed.Result.IsError);
        Assert.Equal("pong", completed.Result.Result);
        var spent = completed.Usage.Models["gpt-6.1-sol"];
        Assert.Equal((400, 800, 40), (spent.Input, spent.CacheRead, spent.Output));
        await session.ReadUntilAsync<StateChanged>(s => s.State == SessionState.Idle);
        Assert.Equal(SessionState.Idle, session.State);

        var context = await session.GetContextUsageAsync(Token);
        Assert.Equal((1200, 258400), (context.TotalTokens, context.MaxTokens));
    }

    [Fact]
    public async Task A_command_is_a_call_of_the_kind_Codex_says_it_is()
    {
        await using var session = await StartedAsync();
        TurnStarted();

        Item("item/started", new JsonObject
        {
            ["type"] = "commandExecution",
            ["id"] = "exec-1",
            ["command"] = "\"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\" -Command 'Get-Content README.md'",
            ["cwd"] = @"C:\Demo\starfall",
            ["status"] = "inProgress",
            ["commandActions"] = new JsonArray(new JsonObject { ["type"] = "read", ["command"] = "Get-Content README.md", ["name"] = "README.md", ["path"] = @"C:\Demo\starfall\README.md" }),
        });
        Item("item/completed", new JsonObject { ["type"] = "commandExecution", ["id"] = "exec-1", ["status"] = "completed", ["aggregatedOutput"] = "# Starfall\n", ["exitCode"] = 0 });

        var (results, seen) = await session.ReadUntilAsync<ToolResultsReceived>();
        var use = seen.OfType<AssistantMessageReceived>().Single().Message.Content.OfType<ToolUseBlock>().Single();
        Assert.Equal("exec-1", use.Id);
        Assert.Equal(ToolKind.Read, use.Call.Kind);
        Assert.Equal(@"C:\Demo\starfall\README.md", use.Call.Path);
        Assert.Equal("Get-Content README.md", use.Call.Command);
        var result = Assert.Single(results.Message.Content.OfType<ToolResultBlock>());
        Assert.Equal(("exec-1", "# Starfall\n", false, 0), (result.ToolUseId, result.Text, result.IsError, result.ExitCode));
    }

    [Fact]
    public async Task A_file_change_is_a_call_for_each_file()
    {
        await using var session = await StartedAsync();
        TurnStarted();

        Item("item/started", new JsonObject
        {
            ["type"] = "fileChange",
            ["id"] = "patch-1",
            ["status"] = "inProgress",
            ["changes"] = new JsonArray(
                new JsonObject { ["path"] = "/repo/README.md", ["kind"] = new JsonObject { ["type"] = "update", ["move_path"] = null }, ["diff"] = "@@ -1 +1 @@\n-# Old\n+# New\n" },
                new JsonObject { ["path"] = "/repo/notes.txt", ["kind"] = new JsonObject { ["type"] = "add" }, ["diff"] = "written\n" }),
        });
        Item("item/completed", new JsonObject { ["type"] = "fileChange", ["id"] = "patch-1", ["status"] = "completed", ["changes"] = new JsonArray(new JsonObject(), new JsonObject()) });

        var (results, seen) = await session.ReadUntilAsync<ToolResultsReceived>();
        var uses = seen.OfType<AssistantMessageReceived>().Single().Message.Content.OfType<ToolUseBlock>().ToArray();
        Assert.Equal(["patch-1:0", "patch-1:1"], uses.Select(u => u.Id));
        Assert.Equal((ToolKind.Edit, "/repo/README.md", "@@ -1 +1 @@\n-# Old\n+# New\n"), (uses[0].Call.Kind, uses[0].Call.Path, uses[0].Call.Hunks));
        Assert.Equal((ToolKind.Write, "written\n"), (uses[1].Call.Kind, uses[1].Call.Content));
        Assert.Equal(["patch-1:0", "patch-1:1"], results.Message.Content.OfType<ToolResultBlock>().Select(r => r.ToolUseId));
    }

    [Theory]
    [InlineData("allow", "accept")]
    [InlineData("always", "acceptForSession")]
    [InlineData("deny", "decline")]
    [InlineData("stop", "cancel")]
    public async Task An_approval_is_a_permission_card_and_its_answer_goes_back(string answer, string decision)
    {
        await using var session = await StartedAsync();
        TurnStarted();

        _transport.EmitJson(new JsonObject
        {
            ["method"] = "item/commandExecution/requestApproval",
            ["id"] = 7,
            ["params"] = new JsonObject
            {
                ["threadId"] = Thread,
                ["turnId"] = Turn,
                ["itemId"] = "exec-2",
                ["command"] = "\"powershell.exe\" -Command 'git push'",
                ["reason"] = "It needs the network.",
                ["commandActions"] = new JsonArray(new JsonObject { ["type"] = "unknown", ["command"] = "git push" }),
            },
        });
        var (requested, _) = await session.ReadUntilAsync<PermissionRequested>();
        var request = requested.Request;
        Assert.Equal((ToolKind.Command, "git push", "exec-2", "It needs the network."), (request.Call.Kind, request.Call.Command, request.ToolUseId, request.DecisionReason));

        switch (answer)
        {
            case "allow": request.Allow(); break;
            case "always": request.AllowAlways([], PermissionRequest.Session); break;
            case "deny": request.Deny("No"); break;
            default: request.Deny("Stop", interrupt: true); break;
        }

        var response = await _transport.WaitForSentAsync(s => s["id"]?.ToJsonString() == "7" && s["result"] is not null);
        Assert.Equal(decision, response["result"]!["decision"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_approval_Codex_withdraws_takes_its_card_away()
    {
        await using var session = await StartedAsync();
        TurnStarted();
        _transport.EmitJson(new JsonObject
        {
            ["method"] = "item/fileChange/requestApproval",
            ["id"] = 8,
            ["params"] = new JsonObject { ["threadId"] = Thread, ["turnId"] = Turn, ["itemId"] = "patch-2" },
        });
        await session.ReadUntilAsync<PermissionRequested>();

        Notify("serverRequest/resolved", new JsonObject { ["threadId"] = Thread, ["requestId"] = 8 });

        var (cancelled, _) = await session.ReadUntilAsync<PermissionCancelled>();
        Assert.Equal("8", cancelled.RequestId);
    }

    [Fact]
    public async Task A_stopped_turn_ends_as_stopped_and_a_failed_one_says_why()
    {
        await using var session = await StartedAsync();
        TurnStarted();
        TurnCompleted("interrupted");
        var (stopped, _) = await session.ReadUntilAsync<TurnCompleted>();
        Assert.Equal("aborted_streaming", stopped.Result.TerminalReason);

        TurnStarted();
        TurnCompleted("failed", "You've hit your usage limit.");
        var (failed, _) = await session.ReadUntilAsync<TurnCompleted>();
        Assert.True(failed.Result.IsError);
        Assert.Equal("You've hit your usage limit.", failed.Result.Result);
    }

    [Fact]
    public async Task Stopping_interrupts_the_running_turn()
    {
        _transport.AutoRespondRpc["turn/interrupt"] = _ => [];
        await using var session = await StartedAsync();
        TurnStarted();
        await session.ReadUntilAsync<TurnStarted>();

        await session.InterruptAsync(Token);

        var interrupt = _transport.Sent.First(s => s.GetString("method") == "turn/interrupt")["params"]!.AsObject();
        Assert.Equal((Thread, Turn), (interrupt.GetString("threadId"), interrupt.GetString("turnId")));
    }

    [Fact]
    public async Task A_message_sent_while_it_works_joins_the_turn()
    {
        _transport.AutoRespondRpc["turn/steer"] = _ => new JsonObject { ["turnId"] = Turn };
        await using var session = await StartedAsync();
        await session.SendUserMessageAsync("first", [], null, null, Token);
        TurnStarted();
        await session.ReadUntilAsync<TurnStarted>();

        await session.SendUserMessageAsync("and also this", [], null, null, Token);

        var steer = _transport.Sent.First(s => s.GetString("method") == "turn/steer")["params"]!.AsObject();
        Assert.Equal(Turn, steer.GetString("expectedTurnId"));
        Assert.Single(_transport.Sent, s => s.GetString("method") == "turn/start");
    }

    [Fact]
    public async Task An_image_goes_as_a_data_address()
    {
        await using var session = await StartedAsync();

        await session.SendUserMessageAsync("look", [new MessageImage("image/png", [1, 2, 3])], null, null, Token);

        var input = _transport.Sent.First(s => s.GetString("method") == "turn/start")["params"]!["input"]!.AsArray();
        Assert.Equal("data:image/png;base64,AQID", input[1]!["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Changing_the_mode_changes_the_sandbox_and_when_it_asks()
    {
        await using var session = await StartedAsync();

        await session.SetPermissionModeAsync(CodexModes.ReadOnly, Token);

        var update = _transport.Sent.Last(s => s.GetString("method") == "thread/settings/update")["params"]!.AsObject();
        Assert.Equal("readOnly", update["sandboxPolicy"]!["type"]!.GetValue<string>());
        Assert.Equal("on-request", update.GetString("approvalPolicy"));
        Assert.Equal(CodexModes.ReadOnly, session.PermissionMode);
    }

    [Fact]
    public async Task Another_threads_events_and_a_bad_line_dont_get_in()
    {
        await using var session = await StartedAsync();
        TurnStarted();

        _transport.Emit("{not json");
        Item("item/completed", new JsonObject { ["type"] = "agentMessage", ["id"] = "sub", ["text"] = "from a subagent" }, thread: "another-thread");
        Item("item/completed", new JsonObject { ["type"] = "agentMessage", ["id"] = "m1", ["text"] = "mine" });

        var (reply, seen) = await session.ReadUntilAsync<AssistantMessageReceived>();
        Assert.Equal("mine", reply.Message.Content.OfType<TextBlock>().Single().Text);
        Assert.Single(seen.OfType<ProtocolError>());
        Assert.Equal(1, session.ProtocolErrorCount);
    }

    [Fact]
    public async Task A_request_Deventry_doesnt_handle_is_refused()
    {
        await using var session = await StartedAsync();

        _transport.EmitJson(new JsonObject { ["method"] = "attestation/generate", ["id"] = 9, ["params"] = new JsonObject() });

        var refusal = await _transport.WaitForSentAsync(s => s["id"]?.ToJsonString() == "9" && s["error"] is not null);
        Assert.Equal(-32601, refusal["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Its_exit_ends_the_session()
    {
        await using var session = await StartedAsync();

        _transport.Exit(1, "boom");

        var (exited, _) = await session.ReadUntilAsync<SessionExited>();
        Assert.Equal(1, exited.Exit.ExitCode);
        Assert.Equal(SessionState.Exited, session.State);
    }
}
