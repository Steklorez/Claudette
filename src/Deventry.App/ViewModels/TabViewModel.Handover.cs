using System.Text;
using Deventry.App.Conversation;
using Deventry.Core.Agents;
using Deventry.Core.Sessions;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>
/// <b>Continue in Codex</b> (or <b>in Claude Code</b>) on the limit bar (DESIGN.md §18, "Continuing in the other agent"):
/// while Settings → Agents → Codex offers it, a tab stopped at its agent's usage limit opens a tab of the other agent in
/// its folder, whose first message, a draft the user sends, hands the work over.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The agent the work would go on in: the other one.</summary>
    public IAgentType OtherAgent => IsCodex ? ClaudeCodeAgent.Instance : CodexAgent.Instance;

    /// <summary>The bar offers it: the tab waits for its agent's limit, the setting is on, and the other agent can start.</summary>
    public bool OffersOtherAgent => LimitWait is { Cause: LimitWaitCause.Limit } && _services.Settings.Codex.OfferOtherAgentAtLimit
        && (IsCodex ? _services.Sessions is not null : _services.CodexSessions is not null);

    public string ContinueInOtherAgentText => $"Continue in {OtherAgent.Name}";

    public string ContinueInOtherAgentTip =>
        $"Open a {OtherAgent.Name} tab in this folder, with a first message that hands this work over: what you asked, the to-do list, the files changed and the last reply. It's yours to send. This tab waits for its reset.";

    [RelayCommand]
    private Task ContinueInOtherAgentAsync() =>
        _shell.ContinueInAgentAsync(this, OtherAgent, $"{DisplayName} ({OtherAgent.ShortName})", HandoverMessage());

    /// <summary>
    /// The first message of the other agent's tab: what the user asked, the to-do list and its state, the files changed
    /// so far, and the last reply.
    /// </summary>
    internal string HandoverMessage()
    {
        var text = new StringBuilder();
        text.AppendLine($"Please carry on with this work. {Agent.Name} was doing it in this folder and stopped at its usage limit.");
        var prompts = Items.OfType<UserMessageItem>().Select(m => m.Text.Trim()).Where(t => t.Length > 0).ToList();
        if (prompts.Count > 0)
        {
            text.AppendLine().AppendLine("What I asked:");
            foreach (var prompt in prompts.Count > 3 ? [prompts[0], .. prompts[^2..]] : prompts)
            {
                text.AppendLine(Quoted(prompt));
            }
        }
        if (TodoList.Items.Count > 0)
        {
            text.AppendLine().AppendLine("The to-do list:");
            foreach (var todo in TodoList.Items)
            {
                text.AppendLine($"- [{(todo.Status == "completed" ? "x" : todo.Status == "in_progress" ? "~" : " ")}] {todo.Content}");
            }
        }
        var changed = ChangedFiles.Files.Select(f => f.DisplayPath).Distinct(StringComparer.Ordinal).ToList();
        if (changed.Count > 0)
        {
            text.AppendLine().AppendLine("Files changed so far:");
            foreach (var path in changed)
            {
                text.AppendLine($"- {path}");
            }
        }
        if (Items.OfType<AssistantTextItem>().LastOrDefault()?.Text.Trim() is { Length: > 0 } reply)
        {
            text.AppendLine().AppendLine($"{AgentShortName}'s last reply:").AppendLine(Quoted(reply));
        }
        // The same on every system, as the composer keeps it.
        return text.ToString().ReplaceLineEndings("\n").TrimEnd();

        static string Quoted(string text) => string.Join("\n", text.ReplaceLineEndings("\n").Split('\n').Select(line => $"> {line}"));
    }
}
