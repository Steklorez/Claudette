using Claudette.App.Conversation;
using Claudette.App.ViewModels.ToolWindows;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The tab's side of the tool windows (DESIGN.md §3, "Tool windows"). The windows that follow the session show the
/// focused session tab's data; while they show this tab's, it does the work they need: the changed files read again, the
/// MCP servers asked, the scratch pad synced and the processes sampled more often. And the tab's ways to a window: the
/// composer bar's buttons and a reply shown as the plan.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The kinds of the tool windows on screen showing this tab's data.</summary>
    private IReadOnlySet<string> _toolWindowsShowing = new HashSet<string>();

    /// <summary>A tool window of <paramref name="kind"/> is on screen showing this tab's data.</summary>
    public bool ShowsToolWindow(ToolWindowKind kind) => _toolWindowsShowing.Contains(kind.Id);

    /// <summary>The Processes window shows this tab's processes, so the composer bar leaves the summary to it.</summary>
    public bool IsProcessesWindowShowing => ShowsToolWindow(ToolWindowKinds.Processes);

    /// <summary>
    /// The shell's word of which tool windows show this tab's data, as they come and go: those coming on screen catch up.
    /// </summary>
    internal void ShowToolWindows(IReadOnlySet<string> showing)
    {
        var was = _toolWindowsShowing;
        if (was.SetEquals(showing))
        {
            return;
        }
        _toolWindowsShowing = showing;
        bool Came(ToolWindowKind kind) => showing.Contains(kind.Id) && !was.Contains(kind.Id);
        if (Came(ToolWindowKinds.Files))
        {
            _ = ChangedFiles.RefreshAsync();
        }
        if (Came(ToolWindowKinds.Mcp))
        {
            _ = McpServers.RefreshAsync();
        }
        if (Came(ToolWindowKinds.ScratchPad))
        {
            _ = ScratchPad.SyncAsync();
        }
        ProcessMonitor.IsPanelVisible = IsProcessesWindowShowing;
        ProcessMonitor.OnProcessesWindowChanged();
    }

    /// <summary>
    /// Shows a tool window (DESIGN.md §3, "Tool windows"): docked, its tab; auto-hidden, it slides out; closed, it opens
    /// where it was.
    /// </summary>
    internal void ShowToolWindow(ToolWindowKind kind) => _shell.ToolWindows.Show(kind);

    /// <summary>The composer bar's <b>Files (n)</b>: shows the Changed files window.</summary>
    [RelayCommand]
    private void OpenFilesPage() => ShowToolWindow(ToolWindowKinds.Files);

    /// <summary>The composer bar's way to the Tasks window.</summary>
    [RelayCommand]
    private void OpenTasksPage() => ShowToolWindow(ToolWindowKinds.Tasks);

    /// <summary>The composer bar's process summary: shows the Processes window.</summary>
    [RelayCommand]
    private void OpenProcessesPage() => ShowToolWindow(ToolWindowKinds.Processes);

    /// <summary>
    /// A reply's <b>Show as the plan</b> (DESIGN.md §5, "Tasks"): for a plan Claude wrote in a reply rather than through
    /// Plan mode. It heads the Tasks window as an approved plan does, and the window shows. Kept with the tab.
    /// </summary>
    [RelayCommand]
    private void ShowAsPlan(AssistantTextItem? reply)
    {
        if (reply is null || reply.IsStreaming || string.IsNullOrWhiteSpace(reply.Text))
        {
            return;
        }
        TodoList.ChooseReply(reply.Text, reply.SentAt);
        State.Plan = new ChosenPlan { Text = TodoList.Plan!, WrittenAt = reply.SentAt, ChosenAt = _services.Time.GetUtcNow() };
        _services.SaveState();
        ShowToolWindow(ToolWindowKinds.Tasks);
        _shell.Announce("Shown as the plan in Tasks.");
    }

    /// <summary>
    /// A restored tab's chosen plan, once its conversation is read back: shown while its reply is still there (a rewind
    /// can go back past it) and no plan was approved after it was chosen; forgotten otherwise.
    /// </summary>
    private void RestoreChosenPlan()
    {
        if (State.Plan is not { } chosen)
        {
            return;
        }
        var approvedLater = TodoList.Plans.Any(p => p is { State: PlanState.Approved, At: { } approved } && approved > chosen.ChosenAt);
        if (approvedLater || !Replies(Items).Any(r => r.Text.Trim() == chosen.Text))
        {
            State.Plan = null;
            return;
        }
        TodoList.ChooseReply(chosen.Text, chosen.WrittenAt);
    }

    /// <summary>Every reply in the conversation, subagents' groups' too.</summary>
    private static IEnumerable<AssistantTextItem> Replies(IEnumerable<ConversationItem> items) => items.Descendants().OfType<AssistantTextItem>();
}
