using System.Text.Json.Nodes;

namespace Deventry.Core.Codex;

/// <summary>
/// A Codex tab's modes, in Codex's own words (DESIGN.md §18, "Codex, a second agent", "Modes"): what Codex may do on its
/// own, as its sandbox and when it asks.
/// </summary>
public static class CodexModes
{
    /// <summary>Reads anything; asks before it changes a file or runs a command that would.</summary>
    public const string ReadOnly = "read-only";

    /// <summary>Changes files and runs commands in the project; asks for what's outside it or needs the network. The default.</summary>
    public const string Auto = "auto";

    /// <summary>Does anything without asking.</summary>
    public const string FullAccess = "full-access";

    /// <summary>Codex's Plan collaboration mode: it plans, and asks its questions, before it changes anything.</summary>
    public const string Plan = "plan";

    public static IReadOnlyList<string> All { get; } = [ReadOnly, Auto, FullAccess, Plan];

    /// <summary>A mode by its word, or Claude Code's word for the nearest one, for a setting chosen for Claude Code; <see cref="Auto"/> otherwise.</summary>
    public static string Normalize(string? mode) => mode switch
    {
        ReadOnly or Auto or FullAccess or Plan => mode,
        "bypassPermissions" => FullAccess,
        _ => Auto,
    };

    public static string Label(string mode) => Normalize(mode) switch
    {
        ReadOnly => "Read only",
        FullAccess => "Full access",
        Plan => "Plan",
        _ => "Auto",
    };

    /// <summary>When Codex asks, for <c>approvalPolicy</c>.</summary>
    public static string ApprovalPolicy(string mode) => Normalize(mode) == FullAccess ? "never" : "on-request";

    /// <summary>The sandbox as <c>thread/start</c> takes it (<c>sandbox</c>).</summary>
    public static string SandboxMode(string mode) => Normalize(mode) switch
    {
        ReadOnly or Plan => "read-only",
        FullAccess => "danger-full-access",
        _ => "workspace-write",
    };

    /// <summary>The sandbox as a turn or a change of settings takes it (<c>sandboxPolicy</c>).</summary>
    public static JsonObject SandboxPolicy(string mode) => Normalize(mode) switch
    {
        ReadOnly or Plan => new JsonObject { ["type"] = "readOnly" },
        FullAccess => new JsonObject { ["type"] = "dangerFullAccess" },
        _ => new JsonObject { ["type"] = "workspaceWrite" },
    };

    /// <summary>The collaboration mode for <paramref name="mode"/>, with <paramref name="model"/>: Plan, or Codex's default.</summary>
    public static JsonObject CollaborationMode(string mode, string model) => new()
    {
        ["mode"] = Normalize(mode) == Plan ? "plan" : "default",
        ["settings"] = new JsonObject { ["model"] = model, ["developer_instructions"] = null },
    };
}

/// <summary>How a Codex tab's session starts (DESIGN.md §18, "Codex, a second agent").</summary>
public sealed record CodexStartOptions
{
    public required string WorkingDirectory { get; init; }

    /// <summary>The thread to resume; null for a new one.</summary>
    public string? Resume { get; init; }

    /// <summary>Null: Codex's own default model.</summary>
    public string? Model { get; init; }

    /// <summary>The reasoning effort; null for the model's default.</summary>
    public string? Effort { get; init; }

    /// <summary>One of <see cref="CodexModes"/>.</summary>
    public string Mode { get; init; } = CodexModes.Auto;

    /// <summary>Deventry's notes for the tab, as Claude Code's are appended to its system prompt.</summary>
    public string? DeveloperInstructions { get; init; }

    /// <summary>Nothing is kept on disk: the background session's.</summary>
    public bool Ephemeral { get; init; }

    /// <summary>Deventry's version, for <c>clientInfo</c>.</summary>
    public string ClientVersion { get; init; } = "0.0.0";

    /// <summary>
    /// Ask whether Codex's Windows sandbox is ready before starting the thread, and stop with
    /// <see cref="CodexSandboxNotReadyException"/> when it isn't, so the factory can try another (DESIGN.md §18, "Codex's
    /// sandbox"). Off elsewhere than on Windows.
    /// </summary>
    public bool RequireWindowsSandbox { get; init; }

    /// <summary>Tools Deventry runs for Codex, as <c>dynamicTools</c> (DESIGN.md §18, "Threads").</summary>
    public JsonArray? DynamicTools { get; init; }
}
