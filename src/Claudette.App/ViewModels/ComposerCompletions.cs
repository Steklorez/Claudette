using System.Collections.ObjectModel;
using Claudette.App.Services;
using Claudette.Core.Composer;
using Claudette.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels;

public enum CompletionKind
{
    None,
    SlashCommand,
    File,
}

/// <summary>Where the <c>@</c> list finds files and folders (DESIGN.md §5, "Autocomplete").</summary>
/// <param name="Project">The working folder's listing.</param>
/// <param name="ExtraFolders">The listings of the tab's extra folders, which Claude can read too.</param>
/// <param name="Folders">Reads a folder typed after the <c>@</c> from disk.</param>
/// <param name="Home">The home folder, for <c>~/</c>.</param>
public sealed record MentionFiles(ProjectFileIndex Project, IReadOnlyList<ProjectFileIndex> ExtraFolders, MentionFolderReader Folders, string Home);

/// <summary>An entry of the composer's autocomplete popup: a slash command, a file or folder, or a session or agent.</summary>
public sealed partial class CompletionItem : ObservableObject
{
    /// <summary>The command name, the path relative to the working folder (folders end in <c>/</c>), or a session's name.</summary>
    public required string Value { get; init; }

    /// <summary><c>/compact</c>, or a file's name.</summary>
    public required string Title { get; init; }

    /// <summary>A command's description, or the folder a file is in.</summary>
    public string? Detail { get; init; }

    /// <summary>A command's arguments, such as <c>[name]</c>.</summary>
    public string? Hint { get; init; }

    public bool IsFolder { get; init; }

    /// <summary>Git ignores it (outside a repository, a folder the list skips): listed dimmed, in a typed folder.</summary>
    public bool IsIgnored { get; init; }

    /// <summary>A session or agent to mention by name (DESIGN.md §5, "Autocomplete"), not a path.</summary>
    public bool IsName { get; init; }

    public string IconKey { get; init; } = Conversation.ToolIcons.Default;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The composer's autocomplete (DESIGN.md §5, "Autocomplete"): <c>/</c> at the start of a message lists slash
/// commands, and <c>@</c> lists other sessions and agents Claude can message, then the files and folders of the working
/// folder and the tab's extra folders, filtered as you type. A folder typed after the <c>@</c> lists what's in it from
/// disk: outside the working folder, that's all it lists. Keyboard first: the view passes the text and caret on every
/// change, and arrow keys, Enter, Tab and Esc while it's open.
/// </summary>
public sealed partial class ComposerCompletions : ObservableObject
{
    private const int MaxItems = 50;

    /// <summary>The most sessions and agents listed ahead of the files.</summary>
    private const int MaxTargets = 8;

    private readonly Func<SlashCommandCatalog> _commands;
    private readonly Func<MentionFiles> _files;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<IReadOnlyList<MentionTarget>>? _targets;
    private readonly Action? _mentionStarted;
    private string _text = "";
    private ComposerToken? _token;
    private (CompletionKind Kind, int Start)? _dismissed;
    private int _version;
    private string? _shownQuery;
    private int? _mentionStart;
    private IReadOnlyList<CompletionItem> _targetItems = [];
    private (IReadOnlyList<CompletionItem> Items, string EmptyMessage, string Query)? _shownFiles;

    /// <summary>The folder outside the working folder being listed, which the header names.</summary>
    private MentionFolder? _outside;

    /// <param name="targets">The sessions and agents an <c>@</c> can name.</param>
    /// <param name="mentionStarted">An <c>@</c> was typed: a chance to look for sessions again, then <see cref="RefreshTargets"/>.</param>
    public ComposerCompletions(Func<SlashCommandCatalog> commands, Func<MentionFiles> files, IUiDispatcher dispatcher,
        Func<IReadOnlyList<MentionTarget>>? targets = null, Action? mentionStarted = null)
    {
        _commands = commands;
        _files = files;
        _dispatcher = dispatcher;
        _targets = targets;
        _mentionStarted = mentionStarted;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    public partial CompletionKind Kind { get; set; }

    public string Header => Kind == CompletionKind.SlashCommand ? "Commands"
        : _outside is { } folder ? folder.Mention
        : _targetItems.Count > 0 ? "Sessions and files" : "Files and folders";

    public ObservableCollection<CompletionItem> Items { get; } = [];

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    partial void OnSelectedIndexChanged(int oldValue, int newValue)
    {
        if (oldValue >= 0 && oldValue < Items.Count)
        {
            Items[oldValue].IsSelected = false;
        }
        if (newValue >= 0 && newValue < Items.Count)
        {
            Items[newValue].IsSelected = true;
        }
    }

    /// <summary>Shown instead of items: nothing matches, or the list is still loading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public CompletionItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>
    /// What's typed is already the highlighted entry, such as <c>/compact</c> in full: Enter sends the message rather
    /// than completing it again.
    /// </summary>
    public bool IsTypedInFull => SelectedItem is { } item && _token is { } token && string.Equals(item.Value, token.Query, StringComparison.OrdinalIgnoreCase);

    /// <summary>The composer's text or caret changed: opens, filters or closes the popup.</summary>
    public void Update(string text, int caret)
    {
        _text = text;
        var slash = ComposerTokens.FindSlashCommand(text, caret);
        var mention = slash is null ? ComposerTokens.FindMention(text, caret) : null;
        var kind = slash is not null ? CompletionKind.SlashCommand : mention is not null ? CompletionKind.File : CompletionKind.None;
        if ((slash ?? mention) is not { } token)
        {
            _dismissed = null;
            _mentionStart = null;
            Close();
            return;
        }
        // Esc closes it until the caret leaves that command or mention.
        if (_dismissed is { } dismissed && dismissed.Kind == kind && dismissed.Start == token.Start)
        {
            Close();
            return;
        }
        _dismissed = null;
        _token = token;
        Kind = kind;
        if (kind == CompletionKind.SlashCommand)
        {
            ShowCommands(token.Query);
        }
        else
        {
            if (_mentionStart != token.Start)
            {
                _mentionStart = token.Start;
                _mentionStarted?.Invoke();
            }
            var files = _files();
            var folder = MentionFolder.From(token.Query, files.Project.Folder, files.Home);
            // A path outside the working folder names no session.
            _outside = folder is { IsOutside: true } ? folder : null;
            _targetItems = _outside is null ? TargetItems(token.Query) : [];
            OnPropertyChanged(nameof(Header));
            _ = _outside is { } outside ? ShowFolderAsync(outside, files.Folders, token.Query) : ShowFilesAsync(token.Query, files, folder);
        }
    }

    /// <summary>The sessions and agents changed: an open <c>@</c> list shows them as they are now.</summary>
    public void RefreshTargets()
    {
        if (!IsOpen || Kind != CompletionKind.File || _token is not { } token || _outside is not null)
        {
            return;
        }
        _targetItems = TargetItems(token.Query);
        OnPropertyChanged(nameof(Header));
        if (_shownFiles is { } files)
        {
            Show(files.Items, files.EmptyMessage, files.Query);
        }
    }

    private CompletionItem[] TargetItems(string query) => _targets is null ? [] :
        [.. SessionMentions.Match(_targets(), query, MaxTargets).Select(t => new CompletionItem
        {
            Value = t.Name,
            Title = t.Name,
            Detail = t.Kind switch
            {
                MentionKind.SubThread => "Sub-thread",
                MentionKind.Tab => "Tab",
                MentionKind.Subagent => t.About is { Length: > 0 } type ? $"Subagent · {type}" : "Subagent",
                _ => t.About is { Length: > 0 } folder ? $"Session · {folder}" : "Session",
            },
            IsName = true,
            IconKey = t.Kind == MentionKind.Subagent ? "IconToolAgent" : "IconThread",
        })];

    /// <summary>Up and Down: moves the highlight, wrapping around.</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }
        SelectedIndex = ((SelectedIndex < 0 ? (delta > 0 ? -1 : 0) : SelectedIndex) + delta + Items.Count) % Items.Count;
    }

    /// <summary>
    /// Enter, Tab or a click: the composer's text with the picked command or path in place of what was typed. Null when
    /// nothing is highlighted.
    /// </summary>
    public ComposerEdit? Accept(CompletionItem? item = null)
    {
        item ??= SelectedItem;
        if (item is null || _token is not { } token)
        {
            return null;
        }
        var edit = Kind == CompletionKind.SlashCommand
            ? ComposerTokens.ReplaceSlashCommand(_text, token, item.Value)
            : ComposerTokens.ReplaceMention(_text, token, item.Value, isName: item.IsName);
        Close();
        return edit;
    }

    /// <summary>Esc: closes the popup, and keeps it closed while the caret stays in the same command or mention.</summary>
    public void Dismiss()
    {
        if (_token is { } token && IsOpen)
        {
            _dismissed = (Kind, token.Start);
        }
        Close();
    }

    public void Close()
    {
        _version++;
        _shownQuery = null;
        _shownFiles = null;
        _targetItems = [];
        _outside = null;
        _token = null;
        IsOpen = false;
        Kind = CompletionKind.None;
        Items.Clear();
        SelectedIndex = -1;
        Message = null;
    }

    private void ShowCommands(string query)
    {
        _version++;
        var catalog = _commands();
        var commands = catalog.Filter(query, MaxItems);
        Show(commands.Select(c => new CompletionItem
        {
            Value = c.Name,
            Title = "/" + c.Name,
            Detail = c.Description,
            Hint = string.IsNullOrWhiteSpace(c.ArgumentHint) ? null : c.ArgumentHint,
            IconKey = "IconToolSkill",
        }).ToArray(), catalog.Commands.Count == 0 ? "Claude Code hasn't listed its commands yet." : "No matching commands", query);
    }

    /// <summary>
    /// A folder typed outside the working folder: what's in it, read from disk once while you type in it, off the UI
    /// thread.
    /// </summary>
    private async Task ShowFolderAsync(MentionFolder folder, MentionFolderReader reader, string query)
    {
        var version = ++_version;
        Interlocked.Exchange(ref _matching, null)?.Cancel();
        var reading = reader.ReadAsync(folder, project: null);
        if (!reading.IsCompleted)
        {
            Show([], $"Listing {folder.Mention}…", query);
        }
        var entries = await reading.ConfigureAwait(false);
        CompletionItem[] items = entries is null ? [] : FolderItems(folder, entries);
        _dispatcher.Post(() =>
        {
            if (version == _version)
            {
                Show(items, entries is null ? "No such folder" : entries.Count == 0 ? "Nothing in this folder" : "No matching files", query);
            }
        });
    }

    /// <summary>
    /// Filters off the UI thread. A stale listing is used straight away and refreshed behind it, so the popup never
    /// waits on git once the tab has listed its files.
    /// </summary>
    /// <param name="folder">A folder typed inside the working folder, whose own entries go first; null for none.</param>
    private async Task ShowFilesAsync(string query, MentionFiles files, MentionFolder? folder)
    {
        var version = ++_version;
        IReadOnlyList<ProjectFileIndex> indexes = [files.Project, .. files.ExtraFolders];
        if (files.Project.Current.Count == 0 && !files.Project.IsFresh)
        {
            Show([], "Listing files…", query);
        }
        else
        {
            await ShowMatchesAsync(version, [.. indexes.Select(i => i.Current)], files, query, folder).ConfigureAwait(false);
        }
        if (indexes.Any(i => !i.IsFresh))
        {
            var fresh = await Task.WhenAll(indexes.Select(i => i.GetAsync())).ConfigureAwait(false);
            await ShowMatchesAsync(version, fresh, files, query, folder).ConfigureAwait(false);
        }
    }

    /// <summary>The match under way: a keystroke that makes it stale stops it rather than letting it finish.</summary>
    private CancellationTokenSource? _matching;

    /// <param name="lists">The working folder's listing, then each extra folder's, as <paramref name="files"/> has them.</param>
    private async Task ShowMatchesAsync(int version, IReadOnlyList<IReadOnlyList<IndexedPath>> lists, MentionFiles files, string query, MentionFolder? folder)
    {
        var matching = new CancellationTokenSource();
        Interlocked.Exchange(ref _matching, matching)?.Cancel();
        IReadOnlyList<CompletionItem> items;
        try
        {
            var listing = folder is null ? null : files.Folders.ReadAsync(folder, files.Project);
            items = await Task.Run(() => Matches(lists, files.ExtraFolders, query, matching.Token), matching.Token).ConfigureAwait(false);
            if (listing is not null && await listing.ConfigureAwait(false) is { } entries)
            {
                matching.Token.ThrowIfCancellationRequested();
                var listed = FolderItems(folder!, entries);
                var values = listed.Select(i => i.Value).ToHashSet(StringComparer.Ordinal);
                items = [.. listed.Concat(items.Where(i => !values.Contains(i.Value))).Take(MaxItems)];
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _dispatcher.Post(() =>
        {
            if (version == _version)
            {
                Show(items, lists.All(l => l.Count == 0) ? "No files in this folder" : "No matching files", query);
            }
        });
    }

    /// <summary>
    /// The working folder's matches and the extra folders', best first: on a tie, the working folder's. With nothing
    /// typed, the top of the working folder, then each extra folder itself.
    /// </summary>
    private static IReadOnlyList<CompletionItem> Matches(IReadOnlyList<IReadOnlyList<IndexedPath>> lists, IReadOnlyList<ProjectFileIndex> extraFolders,
        string query, CancellationToken cancellationToken)
    {
        var scored = PathMatcher.MatchScored(lists[0], query, MaxItems, cancellationToken)
            .Select(s => (Item: PathItem(s.Path, s.Path.Path, s.Path.Parent), s.Score, Source: 0))
            .ToList();
        for (var i = 0; i < extraFolders.Count && i + 1 < lists.Count; i++)
        {
            var mention = AsMention(extraFolders[i].Folder);
            // A drive's root has no name of its own.
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(extraFolders[i].Folder)) is { Length: > 0 } own ? own : mention.TrimEnd('/');
            // The folder itself, by its name.
            foreach (var root in PathMatcher.MatchScored([new IndexedPath(name + "/")], query, 1, cancellationToken))
            {
                scored.Add((PathItem(root.Path, mention, "Extra folder"), root.Score, i + 1));
            }
            if (query.Length > 0)
            {
                scored.AddRange(PathMatcher.MatchScored(lists[i + 1], query, MaxItems, cancellationToken)
                    .Select(s => (PathItem(s.Path, mention + s.Path.Path, name + "/" + s.Path.Parent), s.Score, i + 1)));
            }
        }
        if (query.Length == 0)
        {
            // Every extra folder makes the list, after as much of the top of the working folder as fits.
            var roots = scored.Where(s => s.Source > 0).ToArray();
            return [.. scored.Where(s => s.Source == 0).Take(MaxItems - roots.Length).Concat(roots).Select(s => s.Item)];
        }
        // A stable sort, so each folder's own order stands among matches as good as each other.
        return [.. scored.OrderByDescending(s => s.Score).ThenBy(s => s.Source).Take(MaxItems).Select(s => s.Item)];
    }

    private static CompletionItem PathItem(IndexedPath path, string value, string? detail) => new()
    {
        Value = value,
        Title = path.IsFolder ? path.Name + "/" : path.Name,
        Detail = string.IsNullOrEmpty(detail) ? null : detail,
        IsFolder = path.IsFolder,
        IconKey = path.IsFolder ? "IconToolFolder" : "IconToolRead",
    };

    /// <summary>A typed folder's entries whose names start with what's typed after it, folders first.</summary>
    private static CompletionItem[] FolderItems(MentionFolder folder, IReadOnlyList<TreeEntry> entries) =>
        [.. entries.Where(e => e.Name.StartsWith(folder.Prefix, StringComparison.OrdinalIgnoreCase)).Take(MaxItems).Select(e => new CompletionItem
        {
            Value = folder.Mention + e.Name + (e.IsFolder ? "/" : ""),
            Title = e.IsFolder ? e.Name + "/" : e.Name,
            // Outside, the header names the folder.
            Detail = folder.IsOutside || folder.Mention.Length == 0 ? null : folder.Mention,
            IsFolder = e.IsFolder,
            IsIgnored = e.IsIgnored,
            IconKey = e.IsFolder ? "IconToolFolder" : "IconToolRead",
        })];

    /// <summary>A folder in full, as a mention writes it: forward slashes, ending in one.</summary>
    private static string AsMention(string folder)
    {
        var path = Path.TrimEndingDirectorySeparator(folder).Replace('\\', '/');
        return path.EndsWith('/') ? path : path + "/";
    }

    private void Show(IReadOnlyList<CompletionItem> items, string emptyMessage, string query)
    {
        if (Kind == CompletionKind.File)
        {
            // Sessions and agents go ahead of the files: there are few of them.
            _shownFiles = (items, emptyMessage, query);
            items = [.. _targetItems, .. items];
        }
        // A refresh of the same query keeps the highlight where it was; new typing starts again at the best match.
        var selected = query == _shownQuery ? SelectedItem?.Value : null;
        _shownQuery = query;
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
        Message = items.Count == 0 ? emptyMessage : null;
        var keep = selected is null ? -1 : items.ToList().FindIndex(i => i.Value == selected);
        SelectedIndex = -1;
        SelectedIndex = items.Count == 0 ? -1 : Math.Max(0, keep);
        IsOpen = true;
    }
}
