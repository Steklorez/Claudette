using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.Core.Composer;
using Claudette.Core.Sessions;
using Claudette.Core.Settings;
using Claudette.Core.Subagents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// One of a tab's subagents under its row in the sidebar (DESIGN.md §5, "Subagents"), with Settings → Appearance → Show
/// subagents' work on <b>Under the tab in the sidebar</b>. Selecting it shows its work in the tab, read-only.
/// </summary>
public sealed partial class SubagentRowViewModel : ObservableObject
{
    private readonly TabViewModel _tab;

    internal SubagentRowViewModel(TabViewModel tab, AgentNode node)
    {
        _tab = tab;
        Node = node;
        node.PropertyChanged += OnNodeChanged;
    }

    public AgentNode Node { get; }

    /// <summary>A nested subagent sits further in, under the one that started it.</summary>
    public double Indent => Node.Depth * 12;

    /// <summary>Its work is what its tab shows, and the tab is the one in front: the row is picked out.</summary>
    public bool IsShowing => _tab.IsSelected && ReferenceEquals(_tab.ViewedAgent, Node);

    /// <summary>It and everything it started have finished, so it can be taken off the sidebar.</summary>
    public bool CanDismiss => Node.IsSettled;

    public string Tip => $"{Node.AgentType} subagent: {Node.Title}\n{Node.StatusText}" + (Node.IsEarlier ? "\nFrom an earlier turn" : "");

    /// <summary>The tab has rows from earlier turns, for the menu's <b>Dismiss earlier subagents</b>.</summary>
    public bool CanDismissEarlier => _tab.HasEarlierSubagents;

    internal void EarlierChanged() => OnPropertyChanged(nameof(CanDismissEarlier));

    [RelayCommand]
    private void Open() => _tab.ViewAgent(Node);

    [RelayCommand]
    private void Dismiss() => _tab.DismissSubagent(Node);

    [RelayCommand]
    private void DismissEarlier() => _tab.DismissEarlierSubagents();

    [RelayCommand]
    private void Stop() => _tab.StopAgentCommand.Execute(Node);

    internal void ShowingChanged() => OnPropertyChanged(nameof(IsShowing));

    internal void Detach() => Node.PropertyChanged -= OnNodeChanged;

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentNode.IsSettled) or nameof(AgentNode.IsEarlier) or nameof(AgentNode.StatusText) or nameof(AgentNode.AgentType))
        {
            OnPropertyChanged(nameof(CanDismiss));
            OnPropertyChanged(nameof(Tip));
        }
    }
}

/// <summary>
/// Subagents (DESIGN.md §5, "Subagents"): whether Claude may use them, as Settings → Claude Code → Subagents or the tab's
/// own setting says, and where their work shows. With Settings → Appearance → Show subagents' work on <b>Under the tab
/// in the sidebar</b>, each has a row under the tab's, and the tab shows one's work in place of its conversation,
/// read-only: Claude Code has no way for the user to write to a subagent, only for Claude to.
/// </summary>
public sealed partial class TabViewModel
{
    // ---- Whether Claude may use them -------------------------------------------------------------------------------

    /// <summary>The tab's own setting, else Settings → Claude Code's.</summary>
    public SubagentUse SubagentUse => State.Overrides.Subagents ?? _services.Settings.ClaudeCode.Subagents;

    /// <summary>
    /// The note Claude Code starts with (<c>--append-system-prompt</c>), after any others, when subagents are discouraged
    /// or turned off: Claude knows from the start, and through compaction.
    /// </summary>
    private ClaudeLaunchOptions WithSubagentNote(ClaudeLaunchOptions options)
    {
        var use = SubagentUse;
        if (SubagentMessages.StartNote(use) is not { } note)
        {
            return options;
        }
        State.SubagentsTold = use;
        return options with { AppendSystemPrompt = options.AppendSystemPrompt is { Length: > 0 } existing ? $"{existing}\n\n{note}" : note };
    }

    /// <summary>
    /// The PreToolUse hook every tab registers on the <c>Agent</c> tool, so turning subagents off applies at once, in a
    /// tab already running too.
    /// </summary>
    private HookRegistration SubagentHook() => new("PreToolUse", SubagentMessages.ToolMatcher, OnAgentCallAsync);

    /// <summary>Claude calls the <c>Agent</c> tool: refused while subagents are turned off. Called off the UI thread.</summary>
    private async Task<JsonObject> OnAgentCallAsync(HookInput input, CancellationToken cancellationToken) =>
        SubagentMessages.StartsSubagent(input.ToolName) && await OnUiThreadAsync(() => SubagentUse) == SubagentUse.DontAllow
            ? HookOutputs.Deny(SubagentMessages.Refused)
            : HookOutputs.Continue();

    /// <summary>
    /// The note on subagents for the user's next message: the tab's setting changed since Claude was last told. Null when
    /// it hasn't, or when Claude Code is still to start, which tells Claude itself.
    /// </summary>
    private (string Note, string Line, SubagentUse Before)? TakeSubagentNote()
    {
        var use = SubagentUse;
        var before = State.SubagentsTold;
        if (before == use || _session is null && SubagentMessages.StartNote(use) is not null)
        {
            return null;
        }
        State.SubagentsTold = use;
        _services.SaveState();
        return (SubagentMessages.ChangeNote(use), SubagentMessages.ChangeNoteLine(use), before);
    }

    /// <summary>A message that carried a note on subagents was taken back: the next one carries it again.</summary>
    private void SubagentNoteTakenBack(SubagentUse before) => State.SubagentsTold = before;

    // ---- Where their work shows -------------------------------------------------------------------------------------

    /// <summary>Settings → Appearance → Show subagents' work is <b>Under the tab in the sidebar</b>.</summary>
    public bool SubagentsInSidebar => _services.Settings.Appearance.ShowSubagents == SubagentDisplay.InSidebar;

    /// <summary>Each subagent's card opens its work in the conversation, as it always has.</summary>
    public bool SubagentsInConversation => !SubagentsInSidebar;

    /// <summary>
    /// The subagents' rows under the tab's, in sidebar mode: every subagent until it's dismissed, each nested one under
    /// the one that started it.
    /// </summary>
    public ObservableCollection<SubagentRowViewModel> SubagentRows { get; } = [];

    public bool HasSubagentRows => SubagentRows.Count > 0;

    /// <summary>Some of the rows are from earlier turns, which <b>Dismiss earlier subagents</b> takes away.</summary>
    public bool HasEarlierSubagents => SubagentRows.Any(r => r.Node.IsEarlier);

    private readonly Dictionary<AgentNode, SubagentRowViewModel> _subagentRows = [];

    /// <summary>The subagents taken off the sidebar, with whatever they started.</summary>
    private readonly HashSet<AgentNode> _dismissedSubagents = [];

    /// <summary>The subagent whose work the tab shows in place of its conversation; null for the conversation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewingAgent), nameof(ViewedItems), nameof(ShownItems))]
    public partial AgentNode? ViewedAgent { get; private set; }

    /// <summary>The prompt the shown subagent was given is open, over its work.</summary>
    [ObservableProperty]
    public partial bool ShowsViewedPrompt { get; set; }

    [RelayCommand]
    private void ToggleViewedPrompt() => ShowsViewedPrompt = !ShowsViewedPrompt;

    /// <summary>The tab shows a subagent's work, read-only: no composer under it.</summary>
    public bool IsViewingAgent => ViewedAgent is not null;

    /// <summary>The cards of the subagent the tab shows.</summary>
    public ObservableCollection<ConversationItem>? ViewedItems => ViewedAgent?.Item?.Items;

    /// <summary>What the tab shows: the subagent's cards, or the conversation's.</summary>
    public ObservableCollection<ConversationItem> ShownItems => ViewedItems ?? Items;

    /// <summary>What the bar under a subagent's work says, in place of the composer.</summary>
    public string ViewedAgentNote => ViewedAgent is { } node && node.CanBeMessaged
        ? "A subagent's work, read-only. Claude Code doesn't let you write to a subagent, but Claude can pass it a message."
        : "A subagent's work, read-only. Claude Code doesn't let you write to a subagent.";

    partial void OnViewedAgentChanged(AgentNode? oldValue, AgentNode? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnViewedAgentPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnViewedAgentPropertyChanged;
            // Its row comes out from under a collapsed tab (DESIGN.md §4, "Sidebar").
            ExpandRows();
        }
        ShowsViewedPrompt = false;
        // Find looks through what's shown (DESIGN.md §5, "Find").
        Find.SearchIn(ShownItems);
        OnPropertyChanged(nameof(ViewedAgentNote));
        foreach (var row in SubagentRows)
        {
            row.ShowingChanged();
        }
    }

    private void OnViewedAgentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentNode.CanBeMessaged))
        {
            OnPropertyChanged(nameof(ViewedAgentNote));
        }
    }

    /// <summary>
    /// Shows a subagent's work in the tab, and selects the tab: a row in the sidebar, its card in the conversation, or
    /// its node on the Agents page. Only in sidebar mode, and only for a subagent that started.
    /// </summary>
    internal void ViewAgent(AgentNode node)
    {
        if (!SubagentsInSidebar || node.Item is null || !Agents.Contains(node))
        {
            return;
        }
        SelectedAgent = node;
        _shell.SelectTab(Id);
        ViewedAgent = node;
    }

    /// <summary><b>Back to the conversation</b>, from a subagent's work; the tab's own row does it too.</summary>
    [RelayCommand]
    private void ShowConversation() => ViewedAgent = null;

    /// <summary>Called by the shell when the tab's own row is selected: its conversation, not a subagent's work.</summary>
    internal void ShowOwnConversation() => ViewedAgent = null;

    /// <summary>
    /// A subagent card's line: its cards open under it, or, with their work under the tab in the sidebar, the tab shows
    /// its work. A subagent that never started has none to show either way.
    /// </summary>
    [RelayCommand]
    private void OpenSubagent(SubagentItem? item)
    {
        if (item is null)
        {
            return;
        }
        if (SubagentsInSidebar && Agents.Find(item.ToolUseId) is { } node)
        {
            ViewAgent(node);
            return;
        }
        item.IsExpanded = !item.IsExpanded;
    }

    /// <summary>
    /// <b>Message it through Claude</b>: back to the conversation, with the subagent named in the composer, so the user's
    /// message tells Claude to pass it on with <c>SendMessage</c> (DESIGN.md §5, "Autocomplete").
    /// </summary>
    [RelayCommand]
    private void MessageViewedAgent()
    {
        if (ViewedAgent is not { CanBeMessaged: true } node)
        {
            return;
        }
        var name = MentionTargets().FirstOrDefault(t => t.Kind == MentionKind.Subagent && t.Address == node.AgentId)?.Name ?? node.Title;
        var mention = ComposerTokens.NameMention(name);
        ViewedAgent = null;
        ComposerText = ComposerText.Length == 0 ? mention + " " : $"{mention} {ComposerText}";
        ComposerFocusRequested?.Invoke();
    }

    /// <summary>Takes a finished subagent's row off the sidebar, with the rows of those it started.</summary>
    internal void DismissSubagent(AgentNode node)
    {
        if (!node.IsSettled)
        {
            return;
        }
        _dismissedSubagents.Add(node);
        SyncSubagentRows();
    }

    /// <summary><b>Dismiss earlier subagents</b>: the rows of every finished subagent from an earlier turn.</summary>
    internal void DismissEarlierSubagents()
    {
        foreach (var node in Agents.Subagents.Where(n => n.IsEarlier))
        {
            _dismissedSubagents.Add(node);
        }
        SyncSubagentRows();
    }

    /// <summary>
    /// A restored tab brings back rows only for its last turn's subagents: the earlier ones are history, still on the
    /// Agents page and as cards in the conversation.
    /// </summary>
    private void ForgetRestoredSubagents()
    {
        foreach (var node in Agents.Subagents.Where(n => !n.IsLive && n.IsEarlier))
        {
            _dismissedSubagents.Add(node);
        }
        SyncSubagentRows();
    }

    /// <summary>The tab came to the front or went behind: the row of the subagent it shows is picked out only while it's in front.</summary>
    private void SubagentRowsShowingChanged()
    {
        foreach (var row in SubagentRows)
        {
            row.ShowingChanged();
        }
    }

    /// <summary>The map changed: the rows follow, and a subagent that's gone (after <c>/clear</c>) isn't shown.</summary>
    private void OnSubagentsChanged()
    {
        if (ViewedAgent is { } viewed && !Agents.Contains(viewed))
        {
            ViewedAgent = null;
        }
        if (!Agents.HasSubagents)
        {
            _dismissedSubagents.Clear();
        }
        SyncSubagentRows();
    }

    /// <summary>Settings → Appearance → Show subagents' work changed: it applies at once.</summary>
    private void OnSubagentDisplayChanged()
    {
        Agents.OpensInSidebar = SubagentsInSidebar;
        if (!SubagentsInSidebar)
        {
            ViewedAgent = null;
        }
        OnPropertyChanged(nameof(SubagentsInSidebar));
        OnPropertyChanged(nameof(SubagentsInConversation));
        SyncSubagentRows();
    }

    /// <summary>
    /// Brings the rows in line with the map: in its tree's order, without dismissed subagents and those under them.
    /// Rows already there are kept, so the sidebar doesn't rebuild them.
    /// </summary>
    private void SyncSubagentRows()
    {
        var wanted = new List<AgentNode>();
        if (SubagentsInSidebar)
        {
            Collect(Agents.Root);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var node = wanted[i];
            if (i < SubagentRows.Count && ReferenceEquals(SubagentRows[i].Node, node))
            {
                continue;
            }
            if (_subagentRows.TryGetValue(node, out var row))
            {
                SubagentRows.Move(SubagentRows.IndexOf(row), i);
            }
            else
            {
                row = _subagentRows[node] = new SubagentRowViewModel(this, node);
                SubagentRows.Insert(i, row);
            }
        }
        while (SubagentRows.Count > wanted.Count)
        {
            var gone = SubagentRows[^1];
            SubagentRows.RemoveAt(SubagentRows.Count - 1);
            _subagentRows.Remove(gone.Node);
            gone.Detach();
        }
        OnPropertyChanged(nameof(HasSubagentRows));
        OnPropertyChanged(nameof(HasEarlierSubagents));
        foreach (var row in SubagentRows)
        {
            row.EarlierChanged();
        }
        SubagentRowsUnderChanged();

        void Collect(AgentNode parent)
        {
            foreach (var child in parent.Children)
            {
                if (_dismissedSubagents.Contains(child))
                {
                    continue;
                }
                wanted.Add(child);
                Collect(child);
            }
        }
    }

    /// <summary>
    /// In sidebar mode, the list that holds <paramref name="item"/> comes up before the view scrolls to it: the work of
    /// the subagent whose own card it is, or the conversation. Nothing changes for an item in neither.
    /// </summary>
    private void ShowListHolding(ConversationItem item)
    {
        if (!SubagentsInSidebar || !TryFindOwner(Items, item, null, out var owner))
        {
            return;
        }
        ViewedAgent = owner is null ? null : Agents.Find(owner.ToolUseId) is { Item: not null } node ? node : ViewedAgent;
    }

    /// <summary>Finds <paramref name="item"/> at this level or in a subagent's work, and the subagent whose work it's in.</summary>
    private static bool TryFindOwner(IEnumerable<ConversationItem> items, ConversationItem item, SubagentItem? within, out SubagentItem? owner)
    {
        foreach (var top in items)
        {
            if (ReferenceEquals(top, item) || top is ToolGroupItem && ConversationTree.Holds(top, item))
            {
                owner = within;
                return true;
            }
            if (top is SubagentItem agent && ConversationTree.Holds(agent, item))
            {
                return TryFindOwner(agent.Items, item, agent, out owner);
            }
        }
        owner = null;
        return false;
    }
}
