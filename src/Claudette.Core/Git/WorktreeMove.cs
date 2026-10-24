using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Git;

/// <summary>
/// Claude moving the session into a worktree mid-session, or out of it again (DESIGN.md §4, "Worktree tabs"): what an
/// <c>EnterWorktree</c> or <c>ExitWorktree</c> call's result says. The next turn's <c>system/init</c> names the same
/// folder as its <c>cwd</c> (checked with 2.1.302 and 2.1.308).
/// </summary>
/// <param name="Folder">Where the session works from now on: the worktree, or the folder it went back to.</param>
/// <param name="Worktree">The worktree moved into or out of.</param>
/// <param name="Branch">The worktree's branch, such as <c>worktree-brisk-otter</c>, when the result names it.</param>
/// <param name="Entered">True for EnterWorktree, false for ExitWorktree.</param>
/// <param name="Removed">ExitWorktree removed the worktree (<c>action: "remove"</c>) rather than keeping it.</param>
public sealed record WorktreeMove(string Folder, string Worktree, string? Branch, bool Entered, bool Removed)
{
    public static bool IsWorktreeTool(string toolName) => toolName is "EnterWorktree" or "ExitWorktree";

    /// <summary>The move a successful call's structured result (<c>tool_use_result</c>) describes, or null.</summary>
    public static WorktreeMove? From(string toolName, JsonNode? toolUseResult)
    {
        if (toolUseResult is not JsonObject result)
        {
            return null;
        }
        var worktree = result.GetString("worktreePath");
        return toolName switch
        {
            "EnterWorktree" when worktree is { Length: > 0 } =>
                new WorktreeMove(worktree, worktree, result.GetString("worktreeBranch"), Entered: true, Removed: false),
            "ExitWorktree" when (result.GetString("restoredCwd") ?? result.GetString("originalCwd")) is { Length: > 0 } back =>
                new WorktreeMove(back, worktree ?? "", result.GetString("worktreeBranch"), Entered: false, Removed: result.GetString("action") == "remove"),
            _ => null,
        };
    }
}
