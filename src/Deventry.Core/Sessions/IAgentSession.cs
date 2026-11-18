using System.Text.Json.Nodes;
using System.Threading.Channels;
using Deventry.Core.Agents;
using Deventry.Core.Protocol;

namespace Deventry.Core.Sessions;

/// <summary>
/// One tab's running agent (DESIGN.md §18, "Codex, a second agent"): what every agent's session can do, and the events
/// it reports. What only some agents have is an interface of its own that their session implements as well
/// (<see cref="IRewindsFiles"/>, <see cref="IRemoteControllable"/> and the rest), so a tab asks
/// <c>session is IRemoteControllable</c> rather than which agent it runs.
/// </summary>
public interface IAgentSession : IAsyncDisposable
{
    /// <summary>The agent the session runs.</summary>
    IAgentType Agent { get; }

    /// <summary>Everything the session reports, in order. Completes after <see cref="SessionExited"/>.</summary>
    ChannelReader<SessionEvent> Events { get; }

    /// <summary>The host shows MCP servers' requests for input; off, they're declined at once.</summary>
    bool ShowsElicitations { get; set; }

    SessionState State { get; }

    /// <summary>The clock the session times its requests and its stop by.</summary>
    TimeProvider Time { get; }

    /// <summary>What the agent said as it started: its models, commands and account.</summary>
    InitializeResult? Initialization { get; }

    /// <summary>The id the conversation is resumed by.</summary>
    string? SessionId { get; }

    string? Model { get; }

    string? PermissionMode { get; }

    /// <summary>The agent's version, as it reported it.</summary>
    string? Version { get; }

    /// <summary>Why the agent refused to start, when it said; null otherwise.</summary>
    StartupFailure? StartupFailure { get; }

    /// <summary>The agent's process id, for the process monitor (DESIGN.md §4). Null for test transports.</summary>
    int? ProcessId { get; }

    /// <summary>Messages of a type Deventry doesn't know yet, skipped so far.</summary>
    int UnknownMessageCount { get; }

    /// <summary>Lines that couldn't be parsed, skipped so far.</summary>
    int ProtocolErrorCount { get; }

    /// <summary>Completes when the process has exited and every event has been published.</summary>
    Task Completion { get; }

    /// <summary>
    /// Sends a message with its images and quick suffixes, stamped with its id and, for one the user typed, its origin
    /// (DESIGN.md §5, "Attachments", "Quick suffixes").
    /// </summary>
    ValueTask SendUserMessageAsync(string text, IReadOnlyList<MessageImage> images, string? suffix, MessageStamp? stamp, CancellationToken cancellationToken = default);

    /// <summary>Stops the current turn. The turn still ends with a <see cref="TurnCompleted"/> event.</summary>
    Task InterruptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current turn and, with <paramref name="cancelQueued"/>, the messages waiting behind it, where the agent
    /// can (DESIGN.md §5, "Queued messages"). The receipt says which were still waiting and which were cancelled.
    /// </summary>
    Task<InterruptReceipt> InterruptAsync(bool cancelQueued, CancellationToken cancellationToken = default);

    /// <summary>Takes one message that's waiting its turn back, by the id it was sent with. False when it had already gone.</summary>
    Task<bool> CancelQueuedMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>Switches model in place; the conversation is kept. Null returns to the agent's default model.</summary>
    Task SetModelAsync(string? model, CancellationToken cancellationToken = default);

    /// <summary>Sets the effort from the next request. Null returns to the model's default.</summary>
    Task SetEffortAsync(string? level, CancellationToken cancellationToken = default);

    Task SetPermissionModeAsync(string mode, CancellationToken cancellationToken = default);

    /// <summary>Gives the session a name of its own, which the agent keeps with the conversation.</summary>
    Task RenameSessionAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>How full the context window is.</summary>
    Task<ContextUsage> GetContextUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops a background task, or one subagent, by its id, so the agent knows it ended.</summary>
    Task StopTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the agent to exit by closing its input, and ends it if it hasn't exited within <paramref name="grace"/>.
    /// Waits at most another <paramref name="grace"/> for the rest of its output.
    /// </summary>
    Task StopAsync(TimeSpan grace);
}

/// <summary>A session that writes a title for itself from the first prompt (DESIGN.md §13, "Session naming").</summary>
public interface IWritesSessionTitles
{
    /// <summary>A title for the session from <paramref name="description"/>; null when the agent gave none.</summary>
    Task<string?> GenerateSessionTitleAsync(string description, bool persist = true, CancellationToken cancellationToken = default);
}

/// <summary>A session that keeps copies of files before it changes them, and can put them back (DESIGN.md §5, "Rewind and branch").</summary>
public interface IRewindsFiles
{
    /// <summary>
    /// Puts the files back as they were when <paramref name="userMessageId"/> was sent, or with <paramref name="dryRun"/>
    /// says what that would change.
    /// </summary>
    Task<RewindResult> RewindFilesAsync(string userMessageId, bool dryRun = false, CancellationToken cancellationToken = default);
}

/// <summary>A session the Claude app can reach (DESIGN.md §18, "Remote Control").</summary>
public interface IRemoteControllable
{
    /// <summary>Connects the session; the answer has its address, and an error says why it can't.</summary>
    Task<JsonObject> EnableRemoteControlAsync(string? name, CancellationToken cancellationToken = default);

    Task DisableRemoteControlAsync(CancellationToken cancellationToken = default);
}

/// <summary>A session with Ultracode and output styles (DESIGN.md §5, "Model and effort").</summary>
public interface IHasSessionStyles
{
    Task SetUltracodeAsync(bool on, CancellationToken cancellationToken = default);

    Task SetOutputStyleAsync(string style, CancellationToken cancellationToken = default);
}

/// <summary>A session whose MCP servers can be listed, connected, signed in to and turned off (DESIGN.md §4, "MCP servers").</summary>
public interface IHasMcpServers
{
    /// <param name="timeout">How long to wait for the answer; the session's default when null.</param>
    Task<IReadOnlyList<McpServerStatus>> GetMcpStatusAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    Task ReconnectMcpServerAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>Starts signing in to an MCP server: the answer has the address to open.</summary>
    Task<McpSignIn> SignInToMcpServerAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>Finishes a sign-in with the address the browser ended on.</summary>
    Task FinishMcpSignInAsync(string serverName, string callbackUrl, CancellationToken cancellationToken = default);

    Task SignOutOfMcpServerAsync(string serverName, CancellationToken cancellationToken = default);

    /// <summary>Turns an MCP server on or off for this session.</summary>
    Task SetMcpServerEnabledAsync(string serverName, bool enabled, CancellationToken cancellationToken = default);
}
