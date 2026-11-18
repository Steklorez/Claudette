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
            if (options.Fork)
            {
                // Its settings are the thread's own, then the tab's as below.
                var fork = new JsonObject { ["threadId"] = resume, ["excludeTurns"] = true };
                if (options.ForkAfter is { } after && await TurnOfAsync(resume, after, cancellationToken).ConfigureAwait(false) is { } lastTurn)
                {
                    fork["lastTurnId"] = lastTurn;
                }
                started = await RequestAsync("thread/fork", fork, StartTimeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                started = await RequestAsync("thread/resume", thread, StartTimeout, cancellationToken).ConfigureAwait(false);
            }
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
        // A new message moves Codex on from a plan still waiting.
        WithdrawPlans();
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

    /// <summary>Stops one subagent, by its thread's id: the turn it's working on (DESIGN.md §18, "Subagents").</summary>
    public Task StopTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var parameters = new JsonObject { ["threadId"] = taskId };
        if (_subagents.TryGetValue(taskId, out var subagent) && subagent.TurnId is { } turnId)
        {
            parameters["turnId"] = turnId;
        }
        return RequestAsync("turn/interrupt", parameters, DefaultTimeout, cancellationToken);
    }

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
        // Subagents' threads, by id, to the spawn calls whose results a later item says.
        var spawned = new Dictionary<string, string>(StringComparer.Ordinal);
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
                foreach (var replayed in ReplayOf(item, spawned))
                {
                    events.Add((replayed, time));
                }
            }
            cursor = page.GetString("nextCursor");
        }
        while (cursor is not null);
        return events;
    }

    /// <summary>
    /// The turn of <paramref name="threadId"/> that holds the item <paramref name="itemId"/>, by its id or the id a message
    /// was sent with; null when none does. Read without resuming the thread.
    /// </summary>
    private async Task<string?> TurnOfAsync(string threadId, string itemId, CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            var parameters = new JsonObject { ["threadId"] = threadId, ["sortDirection"] = "asc", ["itemsView"] = "full", ["limit"] = 50 };
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }
            var page = await RequestAsync("thread/turns/list", parameters, StartTimeout, cancellationToken).ConfigureAwait(false);
            foreach (var turn in page.GetArray("data")?.OfType<JsonObject>() ?? [])
            {
                if (turn.GetArray("items")?.OfType<JsonObject>().Any(i => i.GetString("id") == itemId || i.GetString("clientId") == itemId) == true)
                {
                    return turn.GetString("id");
                }
            }
            cursor = page.GetString("nextCursor");
        }
        while (cursor is not null);
        _logger.LogWarning("No turn of Codex's thread has the item to go back to; the copy keeps the whole conversation.");
        return null;
    }

    /// <summary>A stored item as the events its live counterpart reported.</summary>
    /// <param name="spawned">The subagents spawned so far, whose ends a later item may say.</param>
    private IEnumerable<SessionEvent> ReplayOf(JsonObject item, Dictionary<string, string> spawned)
    {
        switch (item.GetString("type"))
        {
            case "collabAgentToolCall":
                // A subagent's call, and its report once a later call (a wait) says how it ended. Its own work is its
                // thread's, which isn't read back.
                if (item.GetString("tool") == "spawnAgent" && item.GetString("id") is { } spawnId)
                {
                    var prompt = item.GetString("prompt") ?? "";
                    yield return new AssistantMessageReceived(new AssistantMessage(spawnId, Model, [AgentUse(spawnId, prompt, DescriptionOf(prompt))], null, null, ItemRaw(item)));
                    foreach (var thread in item.GetStringList("receiverThreadIds"))
                    {
                        spawned[thread] = spawnId;
                    }
                }
                foreach (var (thread, node) in item.GetObject("agentsStates") ?? [])
                {
                    if (node is JsonObject state && state.GetString("status") is "completed" or "errored" or "interrupted" or "shutdown"
                        && spawned.Remove(thread, out var useId))
                    {
                        var report = state.GetString("message") is { Length: > 0 } message ? message : "Done.";
                        yield return new ToolResultsReceived(new UserMessage([new ToolResultBlock(useId, report, state.GetString("status") == "errored", ToolResultRaw(useId))],
                            null, null, ItemRaw(item)));
                    }
                }
                yield break;
            case "userMessage":
                yield return PromptOf(item);
                yield break;
            case "contextCompaction":
                yield return CompactionNotice();
                yield break;
            case "plan" when item.GetString("text") is { Length: > 0 } plan:
                // Read back, a plan is its text: its card was answered then.
                yield return new AssistantMessageReceived(new AssistantMessage(item.GetString("id"), Model, [new TextBlock(plan, [])], null, null, ItemRaw(item)));
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
                else if (parameters.GetString("threadId") is { } startedThread && _subagents.TryGetValue(startedThread, out var working))
                {
                    working.TurnId = parameters.GetObject("turn")?.GetString("id");
                }
                break;
            case "turn/completed":
                if (IsOwnThread(parameters))
                {
                    EndTurn(parameters.GetObject("turn") ?? []);
                }
                else if (parameters.GetString("threadId") is { } endedThread && _subagents.TryGetValue(endedThread, out var ended))
                {
                    // Its turn is the work it was given (DESIGN.md §18, "Subagents").
                    EndSubagent(ended, parameters.GetObject("turn")?.GetString("status") switch
                    {
                        "interrupted" => "stopped",
                        "failed" => "failed",
                        _ => "completed",
                    });
                }
                break;
            case "item/agentMessage/delta" when TryRoute(parameters, out var replyParent, out _):
                if (parameters.GetString("delta") is { Length: > 0 } text)
                {
                    Publish(new TextDelta(text, replyParent));
                }
                break;
            case "item/reasoning/summaryTextDelta" when TryRoute(parameters, out var thinkingParent, out _):
                if (parameters.GetString("delta") is { Length: > 0 } thinking)
                {
                    Publish(new ThinkingDelta(thinking, thinkingParent));
                }
                break;
            case "item/reasoning/summaryPartAdded" when TryRoute(parameters, out var partParent, out _):
                Publish(new ThinkingDelta("\n\n", partParent));
                break;
            case "item/started" when TryRoute(parameters, out var startedParent, out _):
                if (parameters.GetObject("item") is { } startedItem)
                {
                    OnItemStarted(startedItem, startedParent);
                }
                break;
            case "item/completed" when TryRoute(parameters, out var completedParent, out var subagent):
                if (parameters.GetObject("item") is { } completedItem)
                {
                    OnItemCompleted(completedItem, completedParent, subagent);
                }
                break;
            case "thread/tokenUsage/updated" when IsOwnThread(parameters):
                OnTokenUsage(parameters.GetObject("tokenUsage"));
                break;
            case "turn/plan/updated" when IsOwnThread(parameters):
                OnPlanUpdated(parameters);
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

    /// <summary>The name a to-do list's update goes by, as Claude Code's does.</summary>
    public const string TodoTool = "TodoWrite";

    /// <summary>
    /// Codex's steps for the turn (<c>turn/plan/updated</c>): the whole list each time, as Claude Code's TodoWrite gives
    /// it, so the pinned to-do list follows it (DESIGN.md §18, "To-do list, plan").
    /// </summary>
    private void OnPlanUpdated(JsonObject parameters)
    {
        var todos = new JsonArray([.. (parameters.GetArray("plan")?.OfType<JsonObject>() ?? []).Select(step => (JsonNode)new JsonObject
        {
            ["content"] = step.GetString("step"),
            ["activeForm"] = step.GetString("step"),
            ["status"] = step.GetString("status") switch
            {
                "inProgress" => "in_progress",
                "completed" => "completed",
                _ => "pending",
            },
        })]);
        var id = $"plan-{Guid.NewGuid():N}";
        var raw = new JsonObject { ["type"] = "codex_plan", ["uuid"] = id };
        var use = new ToolUseBlock(id, TodoTool, new JsonObject { ["todos"] = todos }, new JsonObject { ["type"] = "tool_use", ["id"] = id })
        {
            Call = new ToolCall(ToolKind.Todo, TodoTool),
        };
        Publish(new AssistantMessageReceived(new AssistantMessage(id, Model, [use], null, null, raw)));
        Publish(new ToolResultsReceived(new UserMessage([new ToolResultBlock(id, "Updated the to-do list.", false, new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id })], null, null, raw)));
    }

    private bool IsOwnThread(JsonObject parameters) => parameters.GetString("threadId") is not { } thread || thread == SessionId;

    /// <param name="parent">The Agent call a subagent's item shows under; null for the main thread's.</param>
    private void OnItemStarted(JsonObject item, string? parent)
    {
        if (item.GetString("type") == "collabAgentToolCall")
        {
            if (item.GetString("tool") == "spawnAgent")
            {
                OnSpawnStarted(item, parent);
            }
            return;
        }
        if (CallsOf(item, parent) is { } calls)
        {
            Publish(calls);
        }
    }

    /// <param name="parent">The Agent call a subagent's item shows under; null for the main thread's.</param>
    /// <param name="subagent">The subagent whose item it is; null for the main thread's.</param>
    private void OnItemCompleted(JsonObject item, string? parent, Subagent? subagent)
    {
        switch (item.GetString("type"))
        {
            case "agentMessage":
                if (subagent is null)
                {
                    _lastReply = item.GetString("text") ?? "";
                }
                else
                {
                    subagent.LastReply = item.GetString("text") ?? "";
                }
                if (ReplyOf(item, parent) is { } reply)
                {
                    Publish(reply);
                }
                return;
            case "reasoning":
                if (ReplyOf(item, parent) is { } thinking)
                {
                    Publish(thinking);
                }
                return;
            case "userMessage" when subagent is null:
                Publish(PromptOf(item));
                return;
            case "userMessage":
                // A subagent's task, which its call already shows.
                return;
            case "collabAgentToolCall":
                if (item.GetString("tool") == "spawnAgent")
                {
                    OnSpawnCompleted(item, parent);
                }
                return;
            case "subAgentActivity":
                OnSubagentActivity(item, parent);
                return;
            case "contextCompaction" when subagent is null:
                Publish(CompactionNotice());
                return;
            case "plan" when subagent is null && item.GetString("text") is { Length: > 0 } plan:
                OfferPlan(item.GetString("id"), plan);
                return;
        }
        if (ResultsOf(item, parent) is { } results)
        {
            Publish(results);
        }
    }

    /// <summary>An item's calls, as Claude Code's tool uses; null when it isn't a call.</summary>
    private AssistantMessageReceived? CallsOf(JsonObject item, string? parent = null)
    {
        var calls = CodexItems.CallsOf(item);
        return calls.Count == 0 ? null : new AssistantMessageReceived(new AssistantMessage(item.GetString("id"), Model,
            [.. calls.Select(c => (ContentBlock)new ToolUseBlock(c.Id, c.Name, c.Input, new JsonObject { ["type"] = "tool_use", ["id"] = c.Id }) { Call = c.Call })],
            parent, null, ItemRaw(item)));
    }

    /// <summary>A finished call's results, as Claude Code's tool results; null when it has none.</summary>
    private static ToolResultsReceived? ResultsOf(JsonObject item, string? parent = null)
    {
        var results = CodexItems.ResultsOf(item);
        return results.Count == 0 ? null : new ToolResultsReceived(new UserMessage(
            [.. results.Select(r => (ContentBlock)new ToolResultBlock(r.Id, r.Text, r.IsError, new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = r.Id }) { ExitCode = r.ExitCode })],
            null, parent, ItemRaw(item)));
    }

    /// <summary>A reply or its reasoning's summary; null when it's empty.</summary>
    private AssistantMessageReceived? ReplyOf(JsonObject item, string? parent = null)
    {
        ContentBlock? block = item.GetString("type") == "reasoning"
            ? string.Join("\n\n", item.GetStringList("summary")) is { Length: > 0 } summary ? new ThinkingBlock(summary, []) : null
            : item.GetString("text") is { Length: > 0 } text ? new TextBlock(text, []) : null;
        return block is null ? null : new AssistantMessageReceived(new AssistantMessage(item.GetString("id"), Model, [block], parent, null, ItemRaw(item)));
    }

    // ---- Subagents (DESIGN.md §18, "Subagents") ------------------------------------------------------------------------

    /// <summary>The name a subagent's call goes by, as Claude Code's Agent tool does.</summary>
    public const string AgentTool = "Agent";

    /// <summary>A subagent's thread: the Agent call its work shows under, and how its work went.</summary>
    private sealed class Subagent(string threadId, string toolUseId, string? parentToolUseId)
    {
        public string ThreadId { get; } = threadId;

        public string ToolUseId { get; } = toolUseId;

        /// <summary>The call the agent that spawned it shows under: null for the main thread's own.</summary>
        public string? ParentToolUseId { get; } = parentToolUseId;

        public string? TurnId { get; set; }

        public string LastReply { get; set; } = "";

        public bool Ended { get; set; }
    }

    private readonly ConcurrentDictionary<string, Subagent> _subagents = new();
    private readonly Lock _subagentLock = new();
    // Spawn calls whose thread Codex hasn't said yet, oldest first.
    private readonly List<(string ToolUseId, string? ParentToolUseId, string Description)> _spawning = [];

    /// <summary>
    /// Where an event belongs: the main thread's (a null <paramref name="parent"/>), or a known subagent's, under its Agent
    /// call. False for a thread Deventry doesn't know.
    /// </summary>
    private bool TryRoute(JsonObject parameters, out string? parent, out Subagent? subagent)
    {
        parent = null;
        subagent = null;
        if (IsOwnThread(parameters))
        {
            return true;
        }
        if (parameters.GetString("threadId") is { } thread && _subagents.TryGetValue(thread, out subagent))
        {
            parent = subagent.ToolUseId;
            return true;
        }
        return false;
    }

    /// <summary>
    /// A <c>spawnAgent</c> call (a <c>collabAgentToolCall</c>): an Agent call, as Claude Code's is, which the subagent's
    /// own work shows under once its thread is known.
    /// </summary>
    private void OnSpawnStarted(JsonObject item, string? parent)
    {
        if (item.GetString("id") is not { } id)
        {
            return;
        }
        var prompt = item.GetString("prompt") ?? "";
        var description = DescriptionOf(prompt);
        Publish(new AssistantMessageReceived(new AssistantMessage(id, Model, [AgentUse(id, prompt, description)], parent, null, ItemRaw(item))));
        var threads = item.GetStringList("receiverThreadIds");
        if (threads.Count == 0)
        {
            lock (_subagentLock)
            {
                _spawning.Add((id, parent, description));
            }
            return;
        }
        foreach (var thread in threads)
        {
            AddSubagent(thread, id, parent, description);
        }
    }

    private void OnSpawnCompleted(JsonObject item, string? parent)
    {
        if (item.GetString("id") is not { } id)
        {
            return;
        }
        string description;
        lock (_subagentLock)
        {
            var index = _spawning.FindIndex(s => s.ToolUseId == id);
            description = index >= 0 ? _spawning[index].Description : DescriptionOf(item.GetString("prompt") ?? "");
            if (index >= 0 && item.GetStringList("receiverThreadIds").Count > 0)
            {
                _spawning.RemoveAt(index);
            }
        }
        foreach (var thread in item.GetStringList("receiverThreadIds"))
        {
            AddSubagent(thread, id, parent, description);
        }
        if (item.GetString("status") == "failed" && !_subagents.Values.Any(s => s.ToolUseId == id))
        {
            Publish(new ToolResultsReceived(new UserMessage([new ToolResultBlock(id, "Codex couldn't start the subagent.", true, ToolResultRaw(id))], null, parent, ItemRaw(item))));
        }
    }

    /// <summary>A <c>subAgentActivity</c> item: a subagent's thread starting, or its end.</summary>
    private void OnSubagentActivity(JsonObject item, string? parent)
    {
        if (item.GetString("agentThreadId") is not { } thread)
        {
            return;
        }
        switch (item.GetString("kind"))
        {
            case "started" when !_subagents.ContainsKey(thread):
                (string ToolUseId, string? ParentToolUseId, string Description)? spawn = null;
                lock (_subagentLock)
                {
                    var index = _spawning.FindIndex(s => s.ParentToolUseId == parent);
                    if (index >= 0)
                    {
                        spawn = _spawning[index];
                        _spawning.RemoveAt(index);
                    }
                }
                if (spawn is { } known)
                {
                    AddSubagent(thread, known.ToolUseId, known.ParentToolUseId, known.Description);
                    return;
                }
                // Its spawn wasn't seen: a call of its own stands in for it.
                var id = item.GetString("id") ?? thread;
                var description = item.GetString("agentPath") is { Length: > 0 } path ? path : "Subagent";
                Publish(new AssistantMessageReceived(new AssistantMessage(id, Model, [AgentUse(id, "", description)], parent, null, ItemRaw(item))));
                AddSubagent(thread, id, parent, description);
                return;
            case "completed" when _subagents.TryGetValue(thread, out var done):
                EndSubagent(done, "completed");
                return;
            case "interrupted" when _subagents.TryGetValue(thread, out var stopped):
                EndSubagent(stopped, "stopped");
                return;
        }
    }

    /// <summary>A subagent's thread is known: its work shows under its call, and it's a task the agent map and Stop follow.</summary>
    private void AddSubagent(string thread, string toolUseId, string? parentToolUseId, string description)
    {
        if (!_subagents.TryAdd(thread, new Subagent(thread, toolUseId, parentToolUseId)))
        {
            return;
        }
        Publish(new SystemNotice(new SystemMessage("task_started", new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "task_started",
            ["task_id"] = thread,
            ["tool_use_id"] = toolUseId,
            ["description"] = description,
            ["task_type"] = "local_agent",
        })));
    }

    /// <summary>
    /// A subagent finished its work, or was stopped: its call's result is its last reply, as a Claude Code subagent's
    /// report is, and its task ends.
    /// </summary>
    private void EndSubagent(Subagent subagent, string status)
    {
        lock (_subagentLock)
        {
            if (subagent.Ended)
            {
                return;
            }
            subagent.Ended = true;
        }
        var report = subagent.LastReply.Length > 0 ? subagent.LastReply : status == "completed" ? "Done." : $"The subagent {status}.";
        Publish(new ToolResultsReceived(new UserMessage([new ToolResultBlock(subagent.ToolUseId, report, status == "failed", ToolResultRaw(subagent.ToolUseId))],
            null, subagent.ParentToolUseId, new JsonObject { ["type"] = "codex_subagent", ["uuid"] = subagent.ThreadId })));
        Publish(new SystemNotice(new SystemMessage("task_notification", new JsonObject
        {
            ["type"] = "system",
            ["subtype"] = "task_notification",
            ["task_id"] = subagent.ThreadId,
            ["tool_use_id"] = subagent.ToolUseId,
            ["status"] = status,
            ["summary"] = report,
        })));
    }

    private static ToolUseBlock AgentUse(string id, string prompt, string description) =>
        new(id, AgentTool, new JsonObject { ["description"] = description, ["prompt"] = prompt }, new JsonObject { ["type"] = "tool_use", ["id"] = id })
        {
            Call = new ToolCall(ToolKind.Agent, AgentTool) { Description = description, Prompt = prompt },
        };

    /// <summary>A subagent's name, from its task: its first line, short.</summary>
    private static string DescriptionOf(string prompt)
    {
        var line = prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length == 0 ? "Subagent" : line.Length > 60 ? line[..57] + "…" : line;
    }

    private static JsonObject ToolResultRaw(string id) => new() { ["type"] = "tool_result", ["tool_use_id"] = id };



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
            case "item/tool/requestUserInput":
                AskQuestions(id, parameters);
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

    /// <summary>The name a question card goes by, as Claude Code's clarifying questions do.</summary>
    public const string QuestionTool = "AskUserQuestion";

    /// <summary>The name a plan card goes by, as Claude Code's plan to approve does.</summary>
    public const string PlanTool = "ExitPlanMode";

    /// <summary>What a plan the user approved is carried out with, as Codex's own app says it.</summary>
    public const string ImplementPlan = "Implement the plan.";

    /// <summary>
    /// Codex's questions, in Plan mode (<c>item/tool/requestUserInput</c>): a question card, as Claude Code's clarifying
    /// questions are, whose answers go back by each question's id (DESIGN.md §18, "Modes").
    /// </summary>
    private void AskQuestions(JsonNode id, JsonObject parameters)
    {
        var requestId = id.ToJsonString();
        var questions = parameters.GetArray("questions")?.OfType<JsonObject>().ToArray() ?? [];
        var input = new JsonObject
        {
            ["questions"] = new JsonArray([.. questions.Select(q => (JsonNode)new JsonObject
            {
                ["id"] = q.GetString("id"),
                ["question"] = q.GetString("question"),
                ["header"] = q.GetString("header"),
                ["multiSelect"] = false,
                ["options"] = new JsonArray([.. (q.GetArray("options")?.OfType<JsonObject>() ?? []).Select(o => (JsonNode)new JsonObject
                {
                    ["label"] = o.GetString("label"),
                    ["description"] = o.GetString("description"),
                })]),
            })]),
        };
        var request = new PermissionRequest(requestId, QuestionTool, parameters.GetString("itemId"), input, new ToolCall(ToolKind.Question, "Question"), null);
        _pendingPermissions[requestId] = request;
        Publish(new PermissionRequested(request));
        _ = AnswerQuestionsAsync(id, request, questions);
    }

    private async Task AnswerQuestionsAsync(JsonNode id, PermissionRequest request, IReadOnlyList<JsonObject> questions)
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
        // The card answers by each question's text; Codex wants them by its id. Dismissed, none is answered.
        var answers = new JsonObject();
        if (decision is AllowDecision { UpdatedInput: var given } && given["answers"] is JsonObject byText)
        {
            foreach (var question in questions)
            {
                if (question.GetString("id") is { } questionId && question.GetString("question") is { } text && byText.GetString(text) is { } answer)
                {
                    answers[questionId] = new JsonObject { ["answers"] = new JsonArray(answer) };
                }
            }
        }
        await RespondSafelyAsync(() => _rpc.RespondAsync(id, new JsonObject { ["answers"] = answers }, _lifetime.Token)).ConfigureAwait(false);
    }

    /// <summary>
    /// Codex's plan, in Plan mode (a <c>plan</c> item): a plan card, as Claude Code's is (DESIGN.md §18, "Modes").
    /// Approving it switches to the mode the card says and starts the turn that carries it out; sending it back with
    /// feedback sends the feedback, in Plan mode still. Codex asks nothing itself: its turn ends with the plan.
    /// </summary>
    private void OfferPlan(string? itemId, string plan)
    {
        var requestId = $"plan:{itemId ?? Guid.NewGuid().ToString("N")}";
        var request = new PermissionRequest(requestId, PlanTool, itemId, new JsonObject { ["plan"] = plan }, new ToolCall(ToolKind.Plan, "Plan"), null);
        _pendingPermissions[requestId] = request;
        Publish(new PermissionRequested(request));
        _ = AnswerPlanAsync(request);
    }

    private async Task AnswerPlanAsync(PermissionRequest request)
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
        try
        {
            switch (decision)
            {
                case AllowDecision allow:
                    await SetPermissionModeAsync(ModeAfterPlan(allow), _lifetime.Token).ConfigureAwait(false);
                    await SendUserMessageAsync(ImplementPlan, [], null, new MessageStamp(Guid.NewGuid().ToString(), FromUser: true), _lifetime.Token).ConfigureAwait(false);
                    break;
                case DenyDecision deny when FeedbackOf(deny.Message) is { } feedback:
                    await SendUserMessageAsync(feedback, [], null, new MessageStamp(Guid.NewGuid().ToString(), FromUser: true), _lifetime.Token).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is CodexRequestException or TimeoutException or IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Couldn't carry on after Codex's plan.");
        }
    }

    /// <summary>
    /// The mode a plan card's approval asks for, in Codex's: auto mode and accepting edits are Auto, asking before each
    /// edit is Read only, which asks before every change.
    /// </summary>
    internal static string ModeAfterPlan(AllowDecision allow) =>
        allow.UpdatedPermissions?.OfType<JsonObject>().FirstOrDefault(p => p.GetString("type") == "setMode")?.GetString("mode") switch
        {
            "default" => CodexModes.ReadOnly,
            "bypassPermissions" => CodexModes.FullAccess,
            _ => CodexModes.Auto,
        };

    /// <summary>What the user said sending a plan back (the card's "…and said: …"); null for nothing.</summary>
    private static string? FeedbackOf(string message)
    {
        const string Said = "and said: ";
        var at = message.IndexOf(Said, StringComparison.Ordinal);
        return at < 0 || message[(at + Said.Length)..] is not { Length: > 0 } feedback ? null : feedback;
    }

    /// <summary>A new message moved Codex on from a plan still waiting: its card goes.</summary>
    private void WithdrawPlans()
    {
        foreach (var requestId in _pendingPermissions.Keys.Where(k => k.StartsWith("plan:", StringComparison.Ordinal)).ToArray())
        {
            if (_pendingPermissions.TryRemove(requestId, out var plan))
            {
                plan.Cancel();
                Publish(new PermissionCancelled(requestId));
            }
        }
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
        if (turn.GetObject("error")?["codexErrorInfo"] is { } info && info.ToJsonString().Contains("usageLimitExceeded", StringComparison.Ordinal))
        {
            // Stopped at the plan's limit, as Claude Code says it (DESIGN.md §6, "Continuing after a limit resets").
            raw["api_error_status"] = 429;
        }
        var result = status switch
        {
            "interrupted" => new ResultMessage("error_during_execution", true, null, "aborted_streaming", SessionId, null, null, null, raw),
            // Anything else that failed isn't a limit: it says so, rather than leaving the tab to wait for a reset.
            "failed" => new ResultMessage("error_during_execution", true, error ?? "Codex couldn't finish the turn.", raw["api_error_status"] is null ? "error" : null, SessionId, null, null, null, raw),
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
