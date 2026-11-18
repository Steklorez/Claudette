using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Deventry.Core.Agents;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Core.Codex;

/// <summary>Codex's Windows sandbox isn't ready with the configuration it started with (<c>windowsSandbox/readiness</c>).</summary>
public sealed class CodexSandboxNotReadyException(string status) : Exception($"Codex's Windows sandbox is {status}.")
{
    public string Status { get; } = status;
}

/// <summary>
/// Codex has no thread to resume (<c>thread/read</c> failed): it's gone, or Codex can't read it. A thread Codex has but
/// won't resume (a required MCP server that didn't start, another model provider) fails with Codex's own error instead.
/// </summary>
public sealed class CodexThreadMissingException(string threadId, CodexRequestException inner)
    : Exception($"Codex couldn't resume its earlier conversation: {inner.Message}", inner)
{
    public string ThreadId { get; } = threadId;
}

/// <summary>The rate limits Codex reported for the signed-in account (<c>account/rateLimits/updated</c>), for the usage header.</summary>
public sealed record RateLimitsReported(JsonObject RateLimits) : SessionEvent;

/// <summary>
/// One <c>codex app-server</c> process, holding one thread (DESIGN.md §18, "Codex, a second agent"). Reads its JSON-RPC
/// notifications and requests, turns them into the same <see cref="SessionEvent"/>s a Claude Code session reports, with
/// each call's <see cref="ToolCall"/> from Codex's items, and answers Codex's approvals.
/// </summary>
public sealed class CodexSession : IAgentSession
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Longer, because MCP servers start with the thread.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);

    private readonly IClaudeTransport _transport;
    private readonly TimeProvider _time;
    private readonly CodexRpc _rpc;
    private readonly ILogger _logger;
    private readonly ProtocolDiagnostics? _diagnostics;
    private readonly Channel<SessionEvent> _events = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });
    private readonly ConcurrentDictionary<string, PermissionRequest> _pendingPermissions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Lock _stateLock = new();
    private readonly Task _readLoop;
    private volatile SessionState _state = SessionState.Starting;
    private string _mode = CodexModes.Auto;
    private string? _turnId;
    private UsageCounts _total = new(0, 0, 0, 0, 0);
    private UsageCounts _atTurnStart = new(0, 0, 0, 0, 0);
    private long? _contextWindow;
    private long _lastInput;
    private string _lastReply = "";
    private int _protocolErrorCount;

    public CodexSession(IClaudeTransport transport, TimeProvider timeProvider, ILogger<CodexSession>? logger = null, ProtocolDiagnostics? diagnostics = null)
    {
        _transport = transport;
        _time = timeProvider;
        _logger = logger ?? NullLogger<CodexSession>.Instance;
        _diagnostics = diagnostics;
        _rpc = new CodexRpc(transport.SendAsync, timeProvider);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public IAgentType Agent => CodexAgent.Instance;

    public ChannelReader<SessionEvent> Events => _events.Reader;

    public bool ShowsElicitations { get; set; }

    public SessionState State => _state;

    public TimeProvider Time => _time;

    public InitializeResult? Initialization { get; private set; }

    /// <summary>The thread's id, which it's resumed by.</summary>
    public string? SessionId { get; private set; }

    public string? Model { get; private set; }

    public string? Effort { get; private set; }

    /// <summary>One of <see cref="CodexModes"/>.</summary>
    public string? PermissionMode => _mode;

    /// <summary>Codex's version, from the user agent it reported (<c>deventry/0.169.0 (…)</c>).</summary>
    public string? Version { get; private set; }

    /// <summary>
    /// False when Codex's Windows sandbox wasn't ready (DESIGN.md §18, "Codex's sandbox"): its commands and edits ask,
    /// whatever the mode says. True otherwise, and everywhere but Windows.
    /// </summary>
    public bool SandboxReady { get; internal set; } = true;

    /// <summary>The session file Codex keeps the thread in, which the session library copies (DESIGN.md §9).</summary>
    public string? ThreadPath { get; private set; }

    public StartupFailure? StartupFailure => null;

    public int? ProcessId => _transport.ProcessId;

    /// <summary>Codex's notifications Deventry doesn't use are passed over by name, not counted as unknown.</summary>
    public int UnknownMessageCount => 0;

    public int ProtocolErrorCount => _protocolErrorCount;

    public Task Completion => _readLoop;

    /// <summary>
    /// Says who Deventry is, reads the models and the account, and starts or resumes the thread (DESIGN.md §18, "Codex, a
    /// second agent"). The session is idle once it has.
    /// </summary>
    public async Task StartAsync(CodexStartOptions options, CancellationToken cancellationToken = default)
    {
        _mode = CodexModes.Normalize(options.Mode);
        var initialize = await RequestAsync("initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject { ["name"] = "deventry", ["title"] = "Deventry", ["version"] = options.ClientVersion },
            // Plan mode, questions, Deventry's own tools and Codex's message queue are behind it.
            ["capabilities"] = new JsonObject { ["experimentalApi"] = true },
        }, StartTimeout, cancellationToken).ConfigureAwait(false);
        Version = VersionFrom(initialize.GetString("userAgent"));
        await _rpc.NotifyAsync("initialized", null, cancellationToken).ConfigureAwait(false);
        if (options.RequireWindowsSandbox)
        {
            JsonObject? readiness;
            try
            {
                readiness = await RequestAsync("windowsSandbox/readiness", [], DefaultTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (CodexRequestException ex) when (ex.IsUnknownMethod)
            {
                // A Codex that can't say: taken as ready, as it was before it could.
                readiness = null;
            }
            catch (CodexRequestException ex)
            {
                // The sandbox chosen can't be used here (MXC where it's unavailable or not allowed).
                throw new CodexSandboxNotReadyException(ex.Message);
            }
            if (readiness?.GetString("status") is { } status && status != "ready")
            {
                throw new CodexSandboxNotReadyException(status);
            }
        }

        var models = await TryRequestAsync("model/list", [], cancellationToken).ConfigureAwait(false);
        var account = await TryRequestAsync("account/read", new JsonObject { ["refreshToken"] = false }, cancellationToken).ConfigureAwait(false);

        var thread = new JsonObject
        {
            ["cwd"] = options.WorkingDirectory,
            ["approvalPolicy"] = CodexModes.ApprovalPolicy(_mode),
            ["sandbox"] = CodexModes.SandboxMode(_mode),
        };
        if (options.Model is { } model)
        {
            thread["model"] = model;
        }
        if (options.DeveloperInstructions is { Length: > 0 } instructions)
        {
            thread["developerInstructions"] = instructions;
        }
        JsonObject started;
        if (options.Resume is { } resume)
        {
            thread["threadId"] = resume;
            thread["excludeTurns"] = true;
            try
            {
                await RequestAsync("thread/read", new JsonObject { ["threadId"] = resume }, StartTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (CodexRequestException ex)
            {
                throw new CodexThreadMissingException(resume, ex);
            }
            started = options.Fork
                // Its settings are the thread's own, then the tab's as below.
                ? await RequestAsync("thread/fork", new JsonObject { ["threadId"] = resume, ["excludeTurns"] = true }, StartTimeout, cancellationToken).ConfigureAwait(false)
                : await RequestAsync("thread/resume", thread, StartTimeout, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (options.Ephemeral)
            {
                thread["ephemeral"] = true;
            }
            if (options.DynamicTools is { Count: > 0 } tools)
            {
                thread["dynamicTools"] = tools.DeepClone();
            }
            started = await RequestAsync("thread/start", thread, StartTimeout, cancellationToken).ConfigureAwait(false);
        }
        SessionId = started.GetObject("thread")?.GetString("id") ?? options.Resume;
        ThreadPath = started.GetObject("thread")?.GetString("path");
        Model = started.GetString("model") ?? options.Model;
        Effort = started.GetString("reasoningEffort");
        if (options.Effort is { } effort && effort != Effort)
        {
            await SetEffortAsync(effort, cancellationToken).ConfigureAwait(false);
        }
        if (_mode == CodexModes.Plan || options.Fork)
        {
            // A fork has its thread's settings: the tab's mode, as a start or a resume passes it.
            await SetPermissionModeAsync(_mode, cancellationToken).ConfigureAwait(false);
        }
        if (options.Fork && options.Model is { } forkModel && forkModel != Model)
        {
            await SetModelAsync(forkModel, cancellationToken).ConfigureAwait(false);
        }
        Initialization = new InitializeResult(ModelsFrom(models), [], AccountFrom(account), _mode, started);
        TrySetState(SessionState.Starting, SessionState.Idle);
    }

    public async ValueTask SendUserMessageAsync(string text, IReadOnlyList<MessageImage> images, string? suffix, MessageStamp? stamp, CancellationToken cancellationToken = default)
    {
        var input = new JsonArray();
        var whole = suffix is { Length: > 0 } ? $"{text}\n\n{suffix}" : text;
        input.Add(new JsonObject { ["type"] = "text", ["text"] = whole });
        foreach (var image in images)
        {
            input.Add(new JsonObject { ["type"] = "image", ["url"] = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}" });
        }
        // A message while a turn runs joins it, as one sent to Claude Code mid-turn does (DESIGN.md §13).
        if (_state == SessionState.Working && _turnId is { } turnId)
        {
            try
            {
                await RequestAsync("turn/steer", new JsonObject { ["threadId"] = SessionId, ["input"] = input, ["expectedTurnId"] = turnId }, DefaultTimeout, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (CodexRequestException)
            {
                // The turn ended as the message went: it starts the next one.
            }
        }
        var started = TrySetState(SessionState.Idle, SessionState.Working);
        try
        {
            var parameters = new JsonObject { ["threadId"] = SessionId, ["input"] = input };
            if (stamp?.Uuid is { } uuid)
            {
                parameters["clientUserMessageId"] = uuid;
            }
            await RequestAsync("turn/start", parameters, DefaultTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (started)
            {
                TrySetState(SessionState.Working, SessionState.Idle);
            }
            throw;
        }
    }

    public Task InterruptAsync(CancellationToken cancellationToken = default) => InterruptAsync(cancelQueued: false, cancellationToken);

    public async Task<InterruptReceipt> InterruptAsync(bool cancelQueued, CancellationToken cancellationToken = default)
    {
        if (_turnId is { } turnId)
        {
            await RequestAsync("turn/interrupt", new JsonObject { ["threadId"] = SessionId, ["turnId"] = turnId }, DefaultTimeout, cancellationToken).ConfigureAwait(false);
        }
        return new InterruptReceipt([], []);
    }

    /// <summary>Codex's own queue isn't used: a message sent mid-turn joins the turn, so there's nothing waiting to take back.</summary>
    public Task<bool> CancelQueuedMessageAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public async Task SetModelAsync(string? model, CancellationToken cancellationToken = default)
    {
        await UpdateSettingsAsync(new JsonObject { ["model"] = model }, cancellationToken).ConfigureAwait(false);
        Model = model ?? Model;
    }

    public async Task SetEffortAsync(string? level, CancellationToken cancellationToken = default)
    {
        await UpdateSettingsAsync(new JsonObject { ["effort"] = level }, cancellationToken).ConfigureAwait(false);
        Effort = level;
    }

    public async Task SetPermissionModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var normalized = CodexModes.Normalize(mode);
        await UpdateSettingsAsync(new JsonObject
        {
            ["approvalPolicy"] = CodexModes.ApprovalPolicy(normalized),
            ["sandboxPolicy"] = CodexModes.SandboxPolicy(normalized),
            ["collaborationMode"] = CodexModes.CollaborationMode(normalized, Model ?? ""),
        }, cancellationToken).ConfigureAwait(false);
        _mode = normalized;
    }

    public Task RenameSessionAsync(string title, CancellationToken cancellationToken = default) =>
        RequestAsync("thread/name/set", new JsonObject { ["threadId"] = SessionId, ["name"] = title }, DefaultTimeout, cancellationToken);

    /// <summary>
    /// How full the context window is, from the tokens of Codex's last model call: Codex has no request for it, as Claude
    /// Code's <c>get_context_usage</c>.
    /// </summary>
    public Task<ContextUsage> GetContextUsageAsync(CancellationToken cancellationToken = default)
    {
        var max = _contextWindow ?? 0;
        var used = Interlocked.Read(ref _lastInput);
        return Task.FromResult(new ContextUsage(used, max, max > 0 ? Math.Round(100.0 * used / max, 1) : 0, null, true, []));
    }

    /// <summary>Stops one subagent: its thread's turn (DESIGN.md §18, "Codex, a second agent").</summary>
    public Task StopTaskAsync(string taskId, CancellationToken cancellationToken = default) =>
        RequestAsync("turn/interrupt", new JsonObject { ["threadId"] = taskId }, DefaultTimeout, cancellationToken);

    public async Task StopAsync(TimeSpan grace)
    {
        _transport.CloseInput();
        try
        {
            await _transport.Completion.WaitAsync(grace, _time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Codex didn't exit within {Grace}; ending it.", grace);
            _transport.Terminate();
        }
        try
        {
            await _readLoop.WaitAsync(grace, _time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_transport.Completion.IsCompleted)
        {
            await StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _transport.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    /// <summary>
    /// The thread's earlier conversation, oldest first, as the events a live turn reports (DESIGN.md §18, "Codex, a second
    /// agent", "History"): each prompt as <see cref="PromptReplayed"/>, each reply, reasoning and call as the session
    /// reports them, with when it happened. Codex keeps it; <c>thread/turns/list</c> reads it back, a page at a time.
    /// </summary>
    public async Task<IReadOnlyList<(SessionEvent Event, DateTimeOffset? Time)>> ReadTurnsAsync(CancellationToken cancellationToken = default)
    {
        var events = new List<(SessionEvent, DateTimeOffset?)>();
        string? cursor = null;
        do
        {
            var parameters = new JsonObject { ["threadId"] = SessionId, ["sortDirection"] = "asc", ["itemsView"] = "full", ["limit"] = 50 };
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }
            var page = await RequestAsync("thread/turns/list", parameters, DefaultTimeout, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.GetArray("data")?.OfType<JsonObject>().SelectMany(t => t.GetArray("items")?.OfType<JsonObject>() ?? []) ?? [])
            {
                var time = item["startedAtMs"].AsWholeNumber() is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
                foreach (var replayed in ReplayOf(item))
                {
                    events.Add((replayed, time));
                }
            }
            cursor = page.GetString("nextCursor");
        }
        while (cursor is not null);
        return events;
    }

    /// <summary>A stored item as the events its live counterpart reported.</summary>
    private IEnumerable<SessionEvent> ReplayOf(JsonObject item)
    {
        switch (item.GetString("type"))
        {
            case "userMessage":
                yield return PromptOf(item);
                yield break;
            case "contextCompaction":
                yield return CompactionNotice();
                yield break;
            case "agentMessage" or "reasoning":
                if (ReplyOf(item) is { } reply)
                {
                    yield return reply;
                }
                yield break;
        }
        if (CallsOf(item) is { } calls)
        {
            yield return calls;
        }
        if (ResultsOf(item) is { } results)
        {
            yield return results;
        }
    }

    /// <summary>Sends any request and returns Codex's result.</summary>
    public Task<JsonObject> RequestAsync(string method, JsonObject? parameters, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _rpc.RequestAsync(method, parameters, timeout ?? DefaultTimeout, cancellationToken);

    private async Task<JsonObject?> TryRequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await RequestAsync(method, parameters, DefaultTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CodexRequestException or TimeoutException)
        {
            _logger.LogDebug(ex, "Codex couldn't answer '{Method}'.", method);
            return null;
        }
    }

    private Task UpdateSettingsAsync(JsonObject settings, CancellationToken cancellationToken)
    {
        settings["threadId"] = SessionId;
        return RequestAsync("thread/settings/update", settings, DefaultTimeout, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            await foreach (var line in _transport.Output.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject message)
                    {
                        throw new JsonException("Not a JSON object.");
                    }
                    Handle(message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad message must never end the session (DESIGN.md §16).
                    Interlocked.Increment(ref _protocolErrorCount);
                    _diagnostics?.RecordParseError();
                    _logger.LogWarning(ex, "Skipped a line from Codex.");
                    Publish(new ProtocolError(line, ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        var exit = await _transport.Completion.ConfigureAwait(false);
        _rpc.FailAll(new ClaudeSessionExitedException(exit));
        foreach (var request in _pendingPermissions.Values)
        {
            request.Cancel();
        }
        _pendingPermissions.Clear();
        lock (_stateLock)
        {
            if (_state != SessionState.Exited)
            {
                _state = SessionState.Exited;
                Publish(new StateChanged(SessionState.Exited));
            }
        }
        Publish(new SessionExited(exit));
        _events.Writer.TryComplete();
    }

    private void Handle(JsonObject message)
    {
        var method = message.GetString("method");
        if (method is null)
        {
            if (!_rpc.TryComplete(message))
            {
                _logger.LogDebug("A response from Codex had no waiting request.");
            }
            return;
        }
        var parameters = message.GetObject("params") ?? [];
        if (message["id"] is { } id)
        {
            HandleRequest(id, method, parameters);
            return;
        }
        // Only this thread's events, and its subagents' (DESIGN.md §18, "Subagents").
        switch (method)
        {
            case "turn/started":
                if (IsOwnThread(parameters))
                {
                    _turnId = parameters.GetObject("turn")?.GetString("id");
                    _atTurnStart = _total;
                    _lastReply = "";
                    StartTurn();
                }
                break;
            case "turn/completed":
                if (IsOwnThread(parameters))
                {
                    EndTurn(parameters.GetObject("turn") ?? []);
                }
                break;
            case "item/agentMessage/delta" when IsOwnThread(parameters):
                if (parameters.GetString("delta") is { Length: > 0 } text)
                {
                    Publish(new TextDelta(text, null));
                }
                break;
            case "item/reasoning/summaryTextDelta" when IsOwnThread(parameters):
                if (parameters.GetString("delta") is { Length: > 0 } thinking)
                {
                    Publish(new ThinkingDelta(thinking, null));
                }
                break;
            case "item/reasoning/summaryPartAdded" when IsOwnThread(parameters):
                Publish(new ThinkingDelta("\n\n", null));
                break;
            case "item/started" when IsOwnThread(parameters):
                if (parameters.GetObject("item") is { } startedItem)
                {
                    OnItemStarted(startedItem);
                }
                break;
            case "item/completed" when IsOwnThread(parameters):
                if (parameters.GetObject("item") is { } completedItem)
                {
                    OnItemCompleted(completedItem);
                }
                break;
            case "thread/tokenUsage/updated" when IsOwnThread(parameters):
                OnTokenUsage(parameters.GetObject("tokenUsage"));
                break;
            case "account/rateLimits/updated":
                if (parameters.GetObject("rateLimits") is { } limits)
                {
                    Publish(new RateLimitsReported(limits));
                }
                break;
            case "serverRequest/resolved":
                OnRequestResolved(parameters);
                break;
            case "error" when IsOwnThread(parameters):
                OnError(parameters);
                break;
            case "item/started" or "item/completed" or "item/agentMessage/delta" or "item/reasoning/summaryTextDelta"
                or "item/reasoning/summaryPartAdded" or "thread/tokenUsage/updated" or "error":
                // Another thread's, such as a subagent's: its own group shows it.
                break;
            default:
                // Notifications Deventry doesn't use: the thread's status, names, warnings, diffs, MCP servers' startup.
                break;
        }
    }

    private bool IsOwnThread(JsonObject parameters) => parameters.GetString("threadId") is not { } thread || thread == SessionId;

    private void OnItemStarted(JsonObject item)
    {
        if (CallsOf(item) is { } calls)
        {
            Publish(calls);
        }
    }

    private void OnItemCompleted(JsonObject item)
    {
        switch (item.GetString("type"))
        {
            case "agentMessage":
                _lastReply = item.GetString("text") ?? "";
                if (ReplyOf(item) is { } reply)
                {
                    Publish(reply);
                }
                return;
            case "reasoning":
                if (ReplyOf(item) is { } thinking)
                {
                    Publish(thinking);
                }
                return;
            case "userMessage":
                Publish(PromptOf(item));
                return;
            case "contextCompaction":
                Publish(CompactionNotice());
                return;
        }
        if (ResultsOf(item) is { } results)
        {
            Publish(results);
        }
    }

    /// <summary>An item's calls, as Claude Code's tool uses; null when it isn't a call.</summary>
    private AssistantMessageReceived? CallsOf(JsonObject item)
    {
        var calls = CodexItems.CallsOf(item);
        return calls.Count == 0 ? null : new AssistantMessageReceived(new AssistantMessage(item.GetString("id"), Model,
            [.. calls.Select(c => (ContentBlock)new ToolUseBlock(c.Id, c.Name, c.Input, new JsonObject { ["type"] = "tool_use", ["id"] = c.Id }) { Call = c.Call })],
            null, null, ItemRaw(item)));
    }

    /// <summary>A finished call's results, as Claude Code's tool results; null when it has none.</summary>
    private static ToolResultsReceived? ResultsOf(JsonObject item)
    {
        var results = CodexItems.ResultsOf(item);
        return results.Count == 0 ? null : new ToolResultsReceived(new UserMessage(
            [.. results.Select(r => (ContentBlock)new ToolResultBlock(r.Id, r.Text, r.IsError, new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = r.Id }) { ExitCode = r.ExitCode })],
            null, null, ItemRaw(item)));
    }

    /// <summary>A reply or its reasoning's summary; null when it's empty.</summary>
    private AssistantMessageReceived? ReplyOf(JsonObject item)
    {
        ContentBlock? block = item.GetString("type") == "reasoning"
            ? string.Join("\n\n", item.GetStringList("summary")) is { Length: > 0 } summary ? new ThinkingBlock(summary, []) : null
            : item.GetString("text") is { Length: > 0 } text ? new TextBlock(text, []) : null;
        return block is null ? null : new AssistantMessageReceived(new AssistantMessage(item.GetString("id"), Model, [block], null, null, ItemRaw(item)));
    }

    /// <summary>A message Deventry sent, echoed with the id it was sent with, as Claude Code echoes one (DESIGN.md §5, "Rewind and branch").</summary>
    private static PromptReplayed PromptOf(JsonObject item)
    {
        var text = string.Join("\n", item.GetArray("content")?.OfType<JsonObject>().Where(c => c.GetString("type") == "text").Select(c => c.GetString("text")).OfType<string>() ?? []);
        var raw = new JsonObject
        {
            ["type"] = "user",
            ["uuid"] = item.GetString("clientId") ?? item.GetString("id"),
            ["isReplay"] = true,
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = text },
        };
        return new PromptReplayed(new UserMessage([new TextBlock(text, [])], null, null, raw));
    }

    /// <summary>Codex compacted the thread, as Claude Code's notice of it.</summary>
    private static SystemNotice CompactionNotice() => new(new SystemMessage("compact_boundary", new JsonObject
    {
        ["type"] = "system",
        ["subtype"] = "compact_boundary",
        ["compact_metadata"] = new JsonObject { ["trigger"] = "manual" },
    }));

    private void OnTokenUsage(JsonObject? usage)
    {
        if (usage is null)
        {
            return;
        }
        _contextWindow = usage["modelContextWindow"].AsWholeNumber() ?? _contextWindow;
        if (usage.GetObject("total") is { } total)
        {
            _total = CountsFrom(total);
        }
        if (usage.GetObject("last") is { } last)
        {
            Interlocked.Exchange(ref _lastInput, last["inputTokens"].AsWholeNumber() ?? 0);
        }
    }

    /// <summary>Codex's tokens as Deventry counts them: fresh input apart from the cached, and reasoning within output.</summary>
    private static UsageCounts CountsFrom(JsonObject breakdown)
    {
        var input = breakdown["inputTokens"].AsWholeNumber() ?? 0;
        var cached = breakdown["cachedInputTokens"].AsWholeNumber() ?? 0;
        var written = breakdown["cacheWriteInputTokens"].AsWholeNumber() ?? 0;
        var output = breakdown["outputTokens"].AsWholeNumber() ?? 0;
        return new UsageCounts(Math.Max(0, input - cached - written), output, written, cached, 0);
    }

    private void OnError(JsonObject parameters)
    {
        // An error Codex retries is a note; one it doesn't ends the turn, which says so.
        if (parameters.GetBool("willRetry") == true && parameters.GetObject("error")?.GetString("message") is { } message)
        {
            Publish(new SystemNotice(new SystemMessage("api_retry", new JsonObject { ["type"] = "system", ["subtype"] = "api_retry", ["error"] = message })));
        }
    }

    private void HandleRequest(JsonNode id, string method, JsonObject parameters)
    {
        switch (method)
        {
            case "item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "execCommandApproval" or "applyPatchApproval":
                AskPermission(id, method, parameters);
                break;
            default:
                // Not something Deventry answers yet: refused, so Codex doesn't wait for it.
                _logger.LogWarning("Unsupported request '{Method}' from Codex.", method);
                _ = RespondSafelyAsync(() => _rpc.RespondErrorAsync(id, -32601, $"Deventry doesn't handle {method}.", _lifetime.Token));
                break;
        }
    }

    private void AskPermission(JsonNode id, string method, JsonObject parameters)
    {
        var requestId = id.ToJsonString();
        var itemId = parameters.GetString("itemId");
        var isCommand = method.Contains("command", StringComparison.OrdinalIgnoreCase) || method == "execCommandApproval";
        var command = parameters.GetArray("commandActions")?.OfType<JsonObject>().Select(a => a.GetString("command")).OfType<string>().ToArray() is { Length: > 0 } actions
            ? string.Join(" && ", actions)
            : CodexItems.Unwrap(parameters.GetString("command") ?? "");
        var call = isCommand
            ? new ToolCall(ToolKind.Command, CodexItems.Shell) { Command = command }
            : new ToolCall(ToolKind.Edit, CodexItems.EditFile) { Path = parameters.GetString("grantRoot") };
        var input = isCommand ? new JsonObject { ["command"] = command } : [];
        var request = new PermissionRequest(requestId, call.Name, itemId, input, call, parameters.GetString("reason"));
        _pendingPermissions[requestId] = request;
        Publish(new PermissionRequested(request));
        _ = AnswerPermissionAsync(id, request);
    }

    private async Task AnswerPermissionAsync(JsonNode id, PermissionRequest request)
    {
        PermissionDecision decision;
        try
        {
            decision = await request.Decision.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!_pendingPermissions.TryRemove(request.RequestId, out _))
        {
            return;
        }
        var answer = decision switch
        {
            AllowDecision { UpdatedPermissions: not null } => "acceptForSession",
            AllowDecision => "accept",
            DenyDecision { Interrupt: true } => "cancel",
            _ => "decline",
        };
        await RespondSafelyAsync(() => _rpc.RespondAsync(id, new JsonObject { ["decision"] = answer }, _lifetime.Token)).ConfigureAwait(false);
    }

    /// <summary>Codex answered or withdrew one of its requests itself, as a turn ended: its card goes.</summary>
    private void OnRequestResolved(JsonObject parameters)
    {
        var requestId = parameters["requestId"]?.ToJsonString();
        if (requestId is not null && _pendingPermissions.TryRemove(requestId, out var request))
        {
            request.Cancel();
            Publish(new PermissionCancelled(requestId));
        }
    }

    private async Task RespondSafelyAsync(Func<ValueTask> respond)
    {
        try
        {
            await respond().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Couldn't answer Codex; the process has probably exited.");
        }
    }

    private void StartTurn()
    {
        lock (_stateLock)
        {
            var changed = _state == SessionState.Idle;
            if (changed)
            {
                _state = SessionState.Working;
            }
            var init = new SystemInitMessage(SessionId ?? "", Model, _mode, null, Version, [], new JsonObject { ["type"] = "system", ["subtype"] = "init" });
            Publish(new TurnStarted(init));
            if (changed)
            {
                Publish(new StateChanged(SessionState.Working));
            }
        }
    }

    private void EndTurn(JsonObject turn)
    {
        var status = turn.GetString("status");
        var error = turn.GetObject("error")?.GetString("message");
        var spent = _total.Minus(_atTurnStart);
        var usage = spent.IsZero ? ModelUsageTotals.Empty : new ModelUsageTotals(new Dictionary<string, UsageCounts> { [Model ?? "codex"] = spent });
        var raw = new JsonObject { ["type"] = "result", ["duration_ms"] = turn["durationMs"]?.DeepClone() };
        var result = status switch
        {
            "interrupted" => new ResultMessage("error_during_execution", true, null, "aborted_streaming", SessionId, null, null, null, raw),
            "failed" => new ResultMessage("error_during_execution", true, error ?? "Codex couldn't finish the turn.", null, SessionId, null, null, null, raw),
            _ => new ResultMessage("success", false, _lastReply, null, SessionId, null, null, null, raw),
        };
        _turnId = null;
        lock (_stateLock)
        {
            var changed = _state is not (SessionState.Idle or SessionState.Exited);
            if (changed)
            {
                _state = SessionState.Idle;
            }
            Publish(new TurnCompleted(result, usage));
            if (changed)
            {
                Publish(new StateChanged(SessionState.Idle));
            }
        }
    }

    private bool TrySetState(SessionState from, SessionState to)
    {
        lock (_stateLock)
        {
            if (_state != from)
            {
                return false;
            }
            _state = to;
            Publish(new StateChanged(to));
            return true;
        }
    }

    private void Publish(SessionEvent sessionEvent) => _events.Writer.TryWrite(sessionEvent);

    private static JsonObject ItemRaw(JsonObject item) => new() { ["type"] = "codex_item", ["uuid"] = item.GetString("id") };

    /// <summary>The models Codex offers (<c>model/list</c>), with their efforts.</summary>
    internal static IReadOnlyList<ModelInfo> ModelsFrom(JsonObject? list) =>
        list?.GetArray("data")?.OfType<JsonObject>()
            .Where(m => m.GetBool("hidden") != true)
            .Select(m => new ModelInfo(
                m.GetString("id") ?? m.GetString("model") ?? "",
                m.GetString("model"),
                m.GetString("displayName") ?? m.GetString("id") ?? "",
                m.GetString("description"),
                m.GetArray("supportedReasoningEfforts") is { Count: > 0 },
                [.. m.GetArray("supportedReasoningEfforts")?.OfType<JsonObject>().Select(e => e.GetString("reasoningEffort")).OfType<string>() ?? []]))
            .Where(m => m.Value.Length > 0)
            .ToArray() ?? [];

    private static AccountInfo AccountFrom(JsonObject? read)
    {
        var account = read?.GetObject("account");
        return new AccountInfo(account?.GetString("email"), null, account?.GetString("planType"), account?.GetString("type"), "openai");
    }

    /// <summary>The version in a user agent such as <c>deventry/0.169.0 (Windows …)</c>.</summary>
    internal static string? VersionFrom(string? userAgent)
    {
        if (userAgent is null || userAgent.IndexOf('/') is var slash && slash < 0)
        {
            return null;
        }
        var rest = userAgent[(slash + 1)..];
        var end = rest.IndexOf(' ');
        var version = end < 0 ? rest : rest[..end];
        return version.Length > 0 ? version : null;
    }

}
