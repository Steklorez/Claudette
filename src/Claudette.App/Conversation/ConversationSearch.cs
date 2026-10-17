using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.Conversation;

/// <summary>
/// Find in the conversation (DESIGN.md §5, "Find"): the items whose text has the query, ignoring case, in order, with
/// one of them current. Looks through prompts, replies, thinking, tool calls with their output, notes, hook runs and
/// subagents' items. Each call in a Tool calls card is a match of its own. The view scrolls to <see cref="Current"/>
/// and marks the card that holds it.
/// </summary>
public sealed partial class ConversationSearch : ObservableObject
{
    private ObservableCollection<ConversationItem> _items;
    private List<ConversationItem> _matches = [];

    // The Tool calls cards whose calls are watched: a call joining one is new to the conversation as an item would be.
    private readonly HashSet<ToolGroupItem> _watched = [];

    public ConversationSearch(ObservableCollection<ConversationItem> items)
    {
        _items = items;
        // New items while the bar is open count too, without moving the current one.
        _items.CollectionChanged += OnItemsChanged;
    }

    /// <summary>
    /// Looks through another list from now on, such as a subagent's work while its tab shows that in place of the
    /// conversation (DESIGN.md §5, "Subagents"); an open search runs again there.
    /// </summary>
    public void SearchIn(ObservableCollection<ConversationItem> items)
    {
        if (ReferenceEquals(items, _items))
        {
            return;
        }
        _items.CollectionChanged -= OnItemsChanged;
        foreach (var group in _watched)
        {
            group.Calls.CollectionChanged -= OnCallsChanged;
        }
        _watched.Clear();
        _items = items;
        _items.CollectionChanged += OnItemsChanged;
        if (IsOpen)
        {
            Search(keepCurrent: false);
        }
    }

    /// <summary>What's searched, in order: the top-level items, with each card of calls standing for its calls.</summary>
    private static IEnumerable<ConversationItem> Searched(IEnumerable<ConversationItem> items) =>
        items.SelectMany<ConversationItem, ConversationItem>(item => item is ToolGroupItem group ? group.Calls : [item]);

    private void Watch()
    {
        foreach (var group in _items.OfType<ToolGroupItem>())
        {
            if (_watched.Add(group))
            {
                group.Calls.CollectionChanged += OnCallsChanged;
            }
        }
        foreach (var gone in _watched.Where(g => !_items.Contains(g)).ToArray())
        {
            gone.Calls.CollectionChanged -= OnCallsChanged;
            _watched.Remove(gone);
        }
    }

    /// <summary>A call joined a card: the card and the items before it, as for a new item.</summary>
    private void OnCallsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsOpen || Query.Trim().Length == 0 || sender is not ObservableCollection<ToolUseItem> calls)
        {
            return;
        }
        if (e.Action == NotifyCollectionChangedAction.Add && _items.Count > 0 && _items[^1] is ToolGroupItem last && ReferenceEquals(last.Calls, calls))
        {
            Refresh(Math.Max(0, _items.Count - 1 - StillFillingIn));
            return;
        }
        Search(keepCurrent: true);
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            Query = "";
        }
    }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    partial void OnQueryChanged(string value) => Search(keepCurrent: false);

    /// <summary>The match the view shows: the newest one after a new search, as the conversation is read bottom up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial ConversationItem? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> changes to a match, so the view brings it into view.</summary>
    public event Action<ConversationItem>? CurrentChanged;

    public int Count => _matches.Count;

    /// <summary>"3 of 12", "No matches", or empty before anything is typed.</summary>
    public string CountText => Query.Trim().Length == 0 ? ""
        : _matches.Count == 0 ? "No matches"
        : $"{_matches.IndexOf(Current!) + 1} of {_matches.Count}";

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Next() => Step(1);

    [RelayCommand]
    private void Previous() => Step(-1);

    private void Step(int by)
    {
        if (_matches.Count == 0)
        {
            return;
        }
        var at = Current is null ? -1 : _matches.IndexOf(Current);
        SetCurrent(_matches[(at + by + _matches.Count) % _matches.Count]);
    }

    private void SetCurrent(ConversationItem? item)
    {
        Current = item;
        OnPropertyChanged(nameof(CountText));
        if (item is not null)
        {
            CurrentChanged?.Invoke(item);
        }
    }

    /// <summary>
    /// How many items before newly added ones are looked through again: they may still have been filling in, such as a
    /// reply streaming in before the next item came.
    /// </summary>
    private const int StillFillingIn = 3;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsOpen || Query.Trim().Length == 0)
        {
            return;
        }
        Watch();
        if (e is { Action: NotifyCollectionChangedAction.Add, NewItems: { } added } && e.NewStartingIndex == _items.Count - added.Count)
        {
            // Only the new items, and the few before them, rather than the whole conversation again.
            Refresh(Math.Max(0, e.NewStartingIndex - StillFillingIn));
            return;
        }
        Search(keepCurrent: true);
    }

    /// <summary>Looks through the items from <paramref name="from"/> on again, keeping the matches before them.</summary>
    private void Refresh(int from)
    {
        var query = Query.Trim();
        var again = Searched(_items.Skip(from)).ToList();
        _matches.RemoveAll(again.Contains);
        _matches.AddRange(again.Where(item => Matches(item, query)));
        OnPropertyChanged(nameof(Count));
        if (Current is null || !_matches.Contains(Current))
        {
            SetCurrent(_matches.LastOrDefault());
        }
        else
        {
            OnPropertyChanged(nameof(CountText));
        }
    }

    private void Search(bool keepCurrent)
    {
        Watch();
        var query = Query.Trim();
        var previous = Current;
        _matches = query.Length == 0 ? [] : [.. Searched(_items).Where(item => Matches(item, query))];
        OnPropertyChanged(nameof(Count));
        if (keepCurrent && previous is not null && _matches.Contains(previous))
        {
            OnPropertyChanged(nameof(CountText));
            return;
        }
        SetCurrent(_matches.LastOrDefault());
    }

    /// <summary>
    /// The match is only in what a collapsed card hides (a tool's input and output, a thought, a subagent's work), so
    /// the card opens to show it.
    /// </summary>
    public bool IsHidden(ConversationItem item)
    {
        var query = Query.Trim();
        return item switch
        {
            ThinkingItem thinking => !thinking.IsExpanded,
            ToolUseItem tool => !tool.IsExpanded && !Has(tool.Name, query) && !Has(tool.Summary, query),
            HookRunItem hook => !hook.IsExpanded && !Has(hook.Title, query),
            UserMessageItem user => user.IsTextCut && !Has(user.ShownText, query) && !Has(user.SuffixText, query),
            _ => false,
        };
    }

    private static bool Matches(ConversationItem item, string query) => item switch
    {
        SubagentItem agent => Has(agent.Name, query) || Has(agent.FullSummary, query) || Has(agent.Output, query) || agent.Items.Any(child => Matches(child, query)),
        ToolGroupItem group => group.Calls.Any(call => Matches(call, query)),
        _ => TextOf(item).Any(text => Has(text, query)),
    };

    /// <summary>What Find looks through in an item.</summary>
    internal static IEnumerable<string?> TextOf(ConversationItem item) => item switch
    {
        UserMessageItem user => [user.CopyText],
        AssistantTextItem reply => [reply.Text],
        ThinkingItem thinking => [thinking.Text],
        ToolUseItem tool => [tool.Name, tool.FullSummary, tool.Detail, tool.Output, tool.ReadText, tool.ResultSummary, tool.Doc?.Text],
        NoteItem note => [note.Text],
        HookRunItem hook => [hook.Title, hook.Output],
        McpInputItem input => [input.Title, input.Message],
        PromptItem prompt => [prompt.Outcome, prompt.Request.ToolName, prompt is PlanItem plan ? plan.Plan.ToString() : null],
        TurnSummaryItem summary => [summary.StatsText, .. summary.Files.Select(f => f.DisplayPath)],
        TaskStartItem start => [start.Todo.Title],
        TasksSummaryItem tasks => [tasks.Text],
        DocSummaryItem doc => [doc.Text],
        _ => [],
    };

    private static bool Has(string? text, string query) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
