using System.Collections.ObjectModel;
using Claudette.Core.Docs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.Conversation;

/// <summary>
/// A doc Claude made or changed through claude.ai's Docs connector (DESIGN.md §5, "Claude Docs"): it lives on claude.ai,
/// and opens there in the browser.
/// </summary>
public sealed partial class DocRow(string id, string url) : ObservableObject
{
    /// <summary>The connector's id for it, which every call on it names.</summary>
    public string Id { get; } = id;

    public string Url { get; } = url;

    /// <summary>Its title, from the call that made it; null for a doc Claude only edited or commented on here.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string? Title { get; set; }

    public string DisplayTitle => Title ?? "Claude Doc";

    /// <summary>When Claude made it, by the tab's clock or its transcript entry; null when it was made elsewhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Whether Claude last edited it or commented on it; null when it hasn't since making it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial ClaudeDocChange? LastChange { get; set; }

    /// <summary>When it did; null when not known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    public partial DateTimeOffset? ChangedAt { get; set; }

    /// <summary>"Created 14:02 · edited 14:30", for the Tasks page.</summary>
    public string? TimeText
    {
        get
        {
            var created = CreatedAt is { } at ? $"Created {at.ToLocalTime():t}" : null;
            var changed = LastChange is { } change
                ? $"{Verb(change)}{(ChangedAt is { } time ? $" {time.ToLocalTime():t}" : "")}"
                : null;
            return created is not null && changed is not null ? $"{created} · {char.ToLowerInvariant(changed[0])}{changed[1..]}"
                : created ?? changed;
        }
    }

    /// <summary>"Created “Enemies: your task list”", or "Edited a Claude Doc" without a title.</summary>
    public string LinkText(ClaudeDocChange change) => Title is { } title ? $"{Verb(change)} “{title}”" : $"{Verb(change)} a Claude Doc";

    private static string Verb(ClaudeDocChange change) => change switch
    {
        ClaudeDocChange.Created => "Created",
        ClaudeDocChange.Commented => "Commented on",
        _ => "Edited",
    };
}

/// <summary>A link to the doc a call made or changed, on its card in the conversation (DESIGN.md §5, "Claude Docs").</summary>
public sealed record DocLink(string Text, string Url);

/// <summary>The Claude Docs the session made or changed, listed on the Tasks page (DESIGN.md §5, "Claude Docs").</summary>
public sealed partial class TodoList
{
    public ObservableCollection<DocRow> Docs { get; } = [];

    public bool HasDocs => Docs.Count > 0;

    /// <summary>
    /// A call that made or changed a doc: the doc's row, added the first time. Null for a doc first seen without its
    /// address (an edit says none), which there's no way to open.
    /// </summary>
    internal DocRow? OnDocCall(ClaudeDocCall call)
    {
        var at = Now();
        var doc = Docs.FirstOrDefault(d => d.Id == call.DocId);
        if (doc is null)
        {
            if (call.Url is not { } url)
            {
                return null;
            }
            doc = new DocRow(call.DocId, url);
            Docs.Add(doc);
            DocsChanged();
        }
        doc.Title ??= call.Title;
        if (call.Change == ClaudeDocChange.Created)
        {
            doc.CreatedAt ??= at;
        }
        else
        {
            doc.LastChange = call.Change;
            doc.ChangedAt = at;
        }
        return doc;
    }

    private void ClearDocs()
    {
        Docs.Clear();
        DocsChanged();
    }

    private void DocsChanged()
    {
        OnPropertyChanged(nameof(HasDocs));
        OnPropertyChanged(nameof(HasAnything));
        OnPropertyChanged(nameof(PageTip));
    }
}
