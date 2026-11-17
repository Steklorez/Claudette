using Deventry.Core.Claude;

namespace Deventry.App.ViewModels;

/// <summary>A permission mode in the tab's mode picker (DESIGN.md §7).</summary>
public sealed record PermissionModeChoice(string Value, string Label, string Description)
{
    /// <summary>Asks first and gives the tab a warning style: Claude Code's Bypass, Codex's Full access.</summary>
    public bool IsBypass => Value is PermissionModeInfo.Bypass or Core.Codex.CodexModes.FullAccess;

    public bool IsAuto => Value == PermissionModeInfo.Auto;
}

/// <summary>Claude Code's permission modes, with the names Deventry shows for them.</summary>
public static class PermissionModeInfo
{
    /// <summary>Manual mode, whose config value is <c>default</c>.</summary>
    public const string Manual = StartingPermissionMode.Manual;

    public const string Auto = StartingPermissionMode.Auto;

    public const string Bypass = "bypassPermissions";

    public static IReadOnlyList<PermissionModeChoice> Choices { get; } =
    [
        new(Manual, "Manual", "Ask before edits and commands"),
        new("acceptEdits", "Accept edits", "Edit files without asking; still ask before commands"),
        new("plan", "Plan", "Look around and plan; don't change anything"),
        new(Auto, "Auto", "Don't ask; a safety check reviews actions and blocks risky ones"),
        new(Bypass, "Bypass permissions", "Skip permission prompts; Claude Code's own deny and ask rules still apply"),
    ];

    /// <summary>A Codex tab's modes, in Codex's words (DESIGN.md §18, "Codex, a second agent", "Modes").</summary>
    public static IReadOnlyList<PermissionModeChoice> CodexChoices { get; } =
    [
        new(Core.Codex.CodexModes.ReadOnly, "Read only", "Read anything; ask before changing a file or running a command that would"),
        new(Core.Codex.CodexModes.Auto, "Auto", "Change files and run commands in the project; ask for anything outside it or the network"),
        new(Core.Codex.CodexModes.Plan, "Plan", "Plan, and ask its questions, before changing anything"),
        new(Core.Codex.CodexModes.FullAccess, "Full access", "Do anything without asking, outside the sandbox"),
    ];

    /// <summary>"Accept edits" for <c>acceptEdits</c>; modes Deventry doesn't offer (such as <c>dontAsk</c>) keep their name.</summary>
    public static string Label(string? mode) => mode switch
    {
        null or "manual" => "Manual",
        "dontAsk" => "Don't ask",
        Core.Codex.CodexModes.ReadOnly or Core.Codex.CodexModes.FullAccess => Core.Codex.CodexModes.Label(mode),
        _ => Choices.FirstOrDefault(c => c.Value == mode)?.Label ?? mode,
    };
}
