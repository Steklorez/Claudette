using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Deventry.App.Services;
using Deventry.Core.Media;
using Deventry.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Conversation;

/// <summary>Whose a piece of media is in the Media window (DESIGN.md §18, "Media").</summary>
public enum MediaSource
{
    /// <summary>The user's: attached to a message, or named in one.</summary>
    User,

    /// <summary>Claude's, or a subagent's: an image a tool handed it, a file a tool wrote or read, or one a reply names.</summary>
    Agent,
}

/// <summary>
/// An image, video or sound in the Media window (DESIGN.md §18, "Media"): an image in the conversation itself (attached,
/// or in a tool's result), or a media file the conversation named that's on disk.
/// </summary>
public sealed partial class MediaEntry : ObservableObject
{
    internal MediaEntry(MediaKind kind, string name, string origin, string? path, ToolImage? image, ConversationItem? item, DateTimeOffset? at, string key, long order)
    {
        Kind = kind;
        Name = name;
        Origin = origin;
        Path = path;
        Image = image;
        Item = item;
        At = at;
        Key = key;
        Order = order;
    }

    public MediaKind Kind { get; }

    public bool IsImage => Kind == MediaKind.Image;

    public bool IsVideo => Kind == MediaKind.Video;

    public bool IsAudio => Kind == MediaKind.Audio;

    /// <summary>The icon its tile shows without a thumbnail: a picture, a film or a note.</summary>
    public string KindIcon => Kind switch
    {
        MediaKind.Video => "IconVideo",
        MediaKind.Audio => "IconAudio",
        _ => "IconImage",
    };

    /// <summary>What it's called: its file's name, or the name its tool's image has (<i>report.pdf, page 3</i>, <i>Image</i>).</summary>
    public string Name { get; }

    /// <summary>Where it came from: <i>Attached</i>, <i>Named in your message</i>, the tool (<i>Read</i>, <i>Bash</i>) or <i>Named in a reply</i>.</summary>
    public string Origin { get; }

    /// <summary>Its file's full path; null for an image that's only in the conversation, such as an attachment or a screenshot.</summary>
    public string? Path { get; }

    public bool IsFile => Path is not null;

    /// <summary>The image itself, as the conversation has it, for one that's in it; null for a file named.</summary>
    public ToolImage? Image { get; }

    /// <summary>The message, tool call or reply it came from, which <b>Show in conversation</b> goes to.</summary>
    public ConversationItem? Item { get; }

    public bool CanShowInConversation => Item is not null;

    /// <summary>When it came: the message's or the call's time.</summary>
    public DateTimeOffset? At { get; }

    /// <summary>What makes it the same as another in its section: its file, or its bytes.</summary>
    internal string Key { get; }

    /// <summary>Where it came in the conversation: a later one is newer, and shows first.</summary>
    internal long Order { get; }

    /// <summary>A small image for its tile, made off the UI thread; null until then, and for video and sound.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail), nameof(HasNoThumbnail))]
    public partial byte[]? Thumbnail { get; internal set; }

    public bool HasThumbnail => Thumbnail is not null;

    public bool HasNoThumbnail => Thumbnail is null;

    /// <summary>"Open bug.png", its tile's name for a screen reader.</summary>
    public string OpenName => $"Open {Name}";

    /// <summary>Whose it is: the section it's in, or that its Cleared group is under.</summary>
    public MediaSource Source { get; internal set; }

    /// <summary>In its section's Cleared group (DESIGN.md §18, "Media", "Clearing").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClearName), nameof(ClearTip), nameof(ClearText))]
    public partial bool IsCleared { get; internal set; }

    /// <summary>"Clear bug.png" or "Restore bug.png": its tile's X, for a screen reader.</summary>
    public string ClearName => $"{ClearText} {Name}";

    /// <summary><b>Clear</b>, or <b>Restore</b> once it's cleared: its menu's and the image window's button.</summary>
    public string ClearText => IsCleared ? "Restore" : "Clear";

    /// <summary>Where clearing or restoring it moves it to, for its X's and the image window's tooltips.</summary>
    public string ClearTip => IsCleared ? $"Move it back to {SourceTitle}" : $"Move it to {SourceTitle} (Cleared)";

    private string SourceTitle => Source == MediaSource.User ? "User" : "Agent";

    /// <summary>Its tile's tooltip: its name, where it came from and when, and its file.</summary>
    public string Tip { get; internal set; } = "";
}

/// <summary>
/// A section of the Media window, the user's or the agent's, or what's cleared from it (DESIGN.md §18, "Media"), which
/// folds to its header.
/// </summary>
public sealed partial class MediaSection : ObservableObject
{
    private readonly Dictionary<string, MediaEntry> _byKey = new(StringComparer.Ordinal);

    internal MediaSection(MediaSource source, string title, string emptyText, bool cleared = false)
    {
        Source = source;
        Title = title;
        EmptyText = emptyText;
        IsCleared = cleared;
        // A Cleared group starts folded.
        IsExpanded = !cleared;
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowsEmptyText));
            OnPropertyChanged(nameof(IsShown));
        };
    }

    public MediaSource Source { get; }

    /// <summary><i>User</i> or <i>Agent</i>, or <i>User (Cleared)</i> or <i>Agent (Cleared)</i>.</summary>
    public string Title { get; }

    /// <summary>What's cleared from the user's or the agent's section (DESIGN.md §18, "Media", "Clearing").</summary>
    public bool IsCleared { get; }

    /// <summary>User and Agent always; a Cleared group once it has anything.</summary>
    public bool IsShown => !IsCleared || !IsEmpty;

    /// <summary>Its header's menu: <b>Clear all</b>, or <b>Restore all</b> on a Cleared group's.</summary>
    public string AllText => IsCleared ? "Restore all" : "Clear all";

    /// <summary>What it says while it has nothing.</summary>
    public string EmptyText { get; }

    /// <summary>Newest first.</summary>
    public ObservableCollection<MediaEntry> Entries { get; } = [];

    public int Count => Entries.Count;

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    public bool IsEmpty => Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmptyText), nameof(ToggleName))]
    public partial bool IsExpanded { get; set; } = true;

    public bool ShowsEmptyText => IsExpanded && IsEmpty;

    /// <summary>Its header's name for a screen reader: <i>Collapse User</i>, <i>Expand Agent</i>.</summary>
    public string ToggleName => $"{(IsExpanded ? "Collapse" : "Expand")} {Title}";

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    /// <summary>
    /// Puts <paramref name="entry"/> where its order says, newest first. The same file or image seen again takes the place
    /// of the one before, so a file Claude made again shows once, as it is now; one older than what's there is dropped.
    /// Returns whether it went in.
    /// </summary>
    internal bool Put(MediaEntry entry)
    {
        if (_byKey.TryGetValue(entry.Key, out var known))
        {
            if (known.Order > entry.Order)
            {
                return false;
            }
            Entries.Remove(known);
        }
        _byKey[entry.Key] = entry;
        var at = 0;
        while (at < Entries.Count && Entries[at].Order > entry.Order)
        {
            at++;
        }
        Entries.Insert(at, entry);
        return true;
    }

    /// <summary>What's here for <paramref name="key"/>, its file or its bytes.</summary>
    internal MediaEntry? Find(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Whether <paramref name="entry"/> is here, rather than a newer one for its file or image.</summary>
    internal bool Contains(MediaEntry entry) => _byKey.TryGetValue(entry.Key, out var here) && here == entry;

    internal void Remove(MediaEntry entry)
    {
        if (Contains(entry))
        {
            _byKey.Remove(entry.Key);
            Entries.Remove(entry);
        }
    }

    internal void Clear()
    {
        _byKey.Clear();
        Entries.Clear();
    }
}

/// <summary>
/// The Media window's gallery (DESIGN.md §18, "Media"): the images, video and sound in a session's conversation, in two
/// sections. <b>User</b>: images attached to a message, and the media files a message names. <b>Agent</b>: the images in
/// a tool's result (a Read of an image, a PDF's pages, an MCP tool's screenshot), and the media files a tool wrote, read or
/// ran a command on, or a reply names, Claude's and its subagents'. <see cref="ConversationBuilder"/> tells it as the
/// conversation is built, read back, cleared and rewound. A file named is looked for off the UI thread, only where that
/// can't reach another machine (<see cref="ReplyFileFinder"/>), and counts once it's there. What the user clears goes to
/// its section's Cleared group, and stays there, kept with the tab, until they restore it or it's seen again.
/// </summary>
public sealed partial class MediaGallery : ObservableObject
{
    /// <summary>How many cleared files and images a tab remembers: the oldest are forgotten first.</summary>
    internal const int MostCleared = 1000;

    // The conversation it belongs to was cleared since a file was looked for, or a thumbnail made: drop what comes back.
    private int _generation;
    private long _order;
    private Task _thumbnails = Task.CompletedTask;

    public MediaGallery()
    {
        User = new MediaSection(MediaSource.User, "User", "Images you attach, and the media files your messages name.");
        Agent = new MediaSection(MediaSource.Agent, "Agent", "Images Claude's tools see, and the media files Claude makes, reads or names.");
        UserCleared = new MediaSection(MediaSource.User, "User (Cleared)", "", cleared: true);
        AgentCleared = new MediaSection(MediaSource.Agent, "Agent (Cleared)", "", cleared: true);
        Sections = [User, Agent];
        ClearedSections = [UserCleared, AgentCleared];
        foreach (var section in Sections.Concat(ClearedSections))
        {
            section.Entries.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAnything));
                OnPropertyChanged(nameof(HasCleared));
                OnPropertyChanged(nameof(Tip));
            };
        }
    }

    public MediaSection User { get; }

    public MediaSection Agent { get; }

    /// <summary>What's cleared from <see cref="User"/> (DESIGN.md §18, "Media", "Clearing").</summary>
    public MediaSection UserCleared { get; }

    /// <summary>What's cleared from <see cref="Agent"/>.</summary>
    public MediaSection AgentCleared { get; }

    /// <summary>The user's, then the agent's.</summary>
    public IReadOnlyList<MediaSection> Sections { get; }

    /// <summary>Under them, past a line: what's cleared from the user's, then from the agent's.</summary>
    public IReadOnlyList<MediaSection> ClearedSections { get; }

    /// <summary>The window's icon shows while there's anything (DESIGN.md §3, "Tool windows"), cleared or not.</summary>
    public bool HasAnything => User.Count + Agent.Count + ClearedCount > 0;

    /// <summary>Anything is cleared: the line and the Cleared groups show.</summary>
    public bool HasCleared => ClearedCount > 0;

    private int ClearedCount => UserCleared.Count + AgentCleared.Count;

    /// <summary>What the icon's tip says after the title: <i>User 2 · Agent 5</i>, and <i>· Cleared 3</i> with any.</summary>
    public string Tip => HasCleared
        ? string.Create(CultureInfo.CurrentCulture, $"User {User.Count} · Agent {Agent.Count} · Cleared {ClearedCount}")
        : string.Create(CultureInfo.CurrentCulture, $"User {User.Count} · Agent {Agent.Count}");

    /// <summary>
    /// What's cleared, kept with the tab (<see cref="Core.Settings.TabState.ClearedMedia"/>): <c>user:</c> or
    /// <c>agent:</c> and an entry's key, oldest first.
    /// </summary>
    public List<string> Cleared { get; set; } = [];

    /// <summary><see cref="Cleared"/> changed, for the tab to save it.</summary>
    public Action? ClearedChanged { get; set; }

    /// <summary>Says what "today" is in a tile's tooltip.</summary>
    public TimeProvider? Time { get; set; }

    /// <summary>Looks for the file a reference names, where that can't reach another machine; the likeliest first.</summary>
    public Func<ReplyFileReference, Task<IReadOnlyList<string>>>? FindFile { get; set; }

    /// <summary>Makes a tile's thumbnail from an image's bytes, off the UI thread; none, and the image is its own.</summary>
    public Func<byte[], byte[]?>? ThumbnailOf { get; set; }

    /// <summary>Makes a tile's thumbnail from an image file, off the UI thread; none, and a file's tile has its kind's icon.</summary>
    public Func<string, byte[]?>? ThumbnailOfFile { get; set; }

    /// <summary>Where what's found off the UI thread goes back to it.</summary>
    public IUiDispatcher? Dispatcher { get; set; }

    /// <summary>Shows a path in a tile's tooltip: inside the tab's folder from there, else in full.</summary>
    public Func<string, string>? ShownPath { get; set; }

    /// <summary>
    /// Called on the UI thread for each image the agent showed live, Claude's or a subagent's, as it's put in the gallery:
    /// the tab keeps the ones a turn made for the remote clients (DESIGN.md §18, "Remote clients", "Images").
    /// </summary>
    public Action<MediaEntry>? AgentImageSeen { get; set; }

    private readonly Lock _findingLock = new();
    private int _finding;
    private TaskCompletionSource? _settled;

    /// <summary>
    /// Completes once every file named so far has been looked for, and put in the gallery if it was found: what a reply
    /// names is looked for after the reply is whole, so a turn's end waits for it.
    /// </summary>
    internal Task SettledAsync()
    {
        lock (_findingLock)
        {
            return _finding == 0 ? Task.CompletedTask : (_settled ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void StartFinding()
    {
        lock (_findingLock)
        {
            _finding++;
        }
    }

    private void DoneFinding()
    {
        TaskCompletionSource? settled = null;
        lock (_findingLock)
        {
            if (--_finding == 0)
            {
                (settled, _settled) = (_settled, null);
            }
        }
        settled?.TrySetResult();
    }

    /// <summary>The section for <paramref name="source"/>.</summary>
    public MediaSection SectionOf(MediaSource source) => source == MediaSource.User ? User : Agent;

    /// <summary>The Cleared group for <paramref name="source"/>.</summary>
    public MediaSection ClearedOf(MediaSource source) => source == MediaSource.User ? UserCleared : AgentCleared;

    /// <summary>
    /// A tile's X, its menu's <b>Clear</b> or <b>Restore</b>, and the image window's button (DESIGN.md §18, "Media",
    /// "Clearing"): moves <paramref name="entry"/> to its section's Cleared group, or back, and remembers it with the tab.
    /// One seen again since, and so no longer here, stays as it is.
    /// </summary>
    public void ToggleCleared(MediaEntry entry)
    {
        if (Move(entry))
        {
            ClearedChanged?.Invoke();
        }
    }

    /// <summary>
    /// A section header's <b>Clear all</b>, or <b>Restore all</b> on a Cleared group's: everything in
    /// <paramref name="section"/> to its Cleared group, or back, remembered with the tab once for them all.
    /// </summary>
    public void ToggleAll(MediaSection section)
    {
        var moved = false;
        // Oldest first, so the newest are the last forgotten when there are more than the tab remembers.
        foreach (var entry in section.Entries.Reverse().ToArray())
        {
            moved |= Move(entry);
        }
        if (moved)
        {
            ClearedChanged?.Invoke();
        }
    }

    /// <summary>Moves <paramref name="entry"/> to its section's Cleared group, or back; false when it's no longer here.</summary>
    private bool Move(MediaEntry entry)
    {
        var from = entry.IsCleared ? ClearedOf(entry.Source) : SectionOf(entry.Source);
        if (!from.Contains(entry))
        {
            return false;
        }
        from.Remove(entry);
        entry.IsCleared = !entry.IsCleared;
        (entry.IsCleared ? ClearedOf(entry.Source) : SectionOf(entry.Source)).Put(entry);
        SetCleared(ClearedKey(entry.Source, entry.Key), entry.IsCleared);
        return true;
    }

    /// <summary>Remembers <paramref name="key"/> as cleared, or forgets it; the caller tells <see cref="ClearedChanged"/>.</summary>
    private void SetCleared(string key, bool cleared)
    {
        Cleared.RemoveAll(k => k == key);
        if (cleared)
        {
            Cleared.Add(key);
            if (Cleared.Count > MostCleared)
            {
                Cleared.RemoveRange(0, Cleared.Count - MostCleared);
            }
        }
    }

    private static string ClearedKey(MediaSource source, string key) => (source == MediaSource.User ? "user:" : "agent:") + key;

    /// <summary>
    /// The user's message: the images attached to it (DESIGN.md §5, "Attachments"), then the media files it names, in
    /// <paramref name="named"/>. <paramref name="live"/> is false for one read back from the transcript.
    /// </summary>
    internal void AddMessage(UserMessageItem message, IReadOnlyList<ReplyFileReference> named, DateTimeOffset? at, bool live = true)
    {
        var images = message.Images;
        var first = Reserve(images.Count + named.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var name = images.Count == 1 ? "Attached image" : string.Create(CultureInfo.InvariantCulture, $"Attached image {i + 1}");
            var image = new ToolImage(images[i], "Your message", name, ToolImages.SaveName("attached-image", images[i].MediaType), null, null);
            AddImage(MediaSource.User, image, "Attached", message, at, first - i, live);
        }
        AddNamed(MediaSource.User, named, "Named in your message", message, at, first - images.Count, live);
    }

    /// <summary>
    /// A tool call whose result is back: the images in it (DESIGN.md §18, "Images from Claude's tools"), then the media
    /// files in <paramref name="named"/>, which it read, wrote or ran a command on.
    /// </summary>
    internal void AddToolResult(ToolUseItem tool, IReadOnlyList<ReplyFileReference> named, bool live = true)
    {
        var first = Reserve(tool.Images.Count + named.Count);
        for (var i = 0; i < tool.Images.Count; i++)
        {
            AddImage(MediaSource.Agent, tool.Images[i], tool.Name, tool, tool.StartedAt, first - i, live);
        }
        AddNamed(MediaSource.Agent, named, tool.Name, tool, tool.StartedAt, first - tool.Images.Count, live);
    }

    /// <summary>A whole reply, Claude's or a subagent's: the media files it names, in <paramref name="named"/>.</summary>
    internal void AddReply(AssistantTextItem reply, IReadOnlyList<ReplyFileReference> named, bool live = true) =>
        AddNamed(MediaSource.Agent, named, "Named in a reply", reply, reply.SentAt, Reserve(named.Count), live);

    /// <summary>
    /// Places for <paramref name="count"/> entries from one message, call or reply, after everything before it: the first
    /// is the one returned, and the rest count down from it, so they show in the order they came, above what's older.
    /// </summary>
    private long Reserve(int count)
    {
        _order += count;
        return _order;
    }

    /// <summary>
    /// The media files <paramref name="references"/> name, each once it's found on disk, at the places from
    /// <paramref name="first"/> down, however long the looking takes. <paramref name="origin"/> says where they're from.
    /// </summary>
    private void AddNamed(MediaSource source, IReadOnlyList<ReplyFileReference> references, string origin, ConversationItem item, DateTimeOffset? at, long first, bool live)
    {
        if (references.Count == 0 || FindFile is not { } find)
        {
            return;
        }
        var generation = _generation;
        for (var i = 0; i < references.Count; i++)
        {
            Task<IReadOnlyList<string>> finding;
            try
            {
                finding = find(references[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            StartFinding();
            _ = AddFoundAsync(finding, source, origin, item, at, first - i, generation, live);
        }
    }

    private async Task AddFoundAsync(Task<IReadOnlyList<string>> finding, MediaSource source, string origin, ConversationItem item, DateTimeOffset? at, long order, int generation, bool live)
    {
        string? path;
        try
        {
            // A folder named like a file isn't one.
            path = (await finding.ConfigureAwait(false)).FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Post(DoneFinding);
            return;
        }
        if (path is null || MediaFiles.KindOf(path) is not { } kind)
        {
            Post(DoneFinding);
            return;
        }
        Post(() =>
        {
            try
            {
                if (generation != _generation)
                {
                    return;
                }
                var entry = new MediaEntry(kind, System.IO.Path.GetFileName(path), origin, path, null, item, at, PathKey(path), order);
                if (!Put(source, entry, live) || kind != MediaKind.Image)
                {
                    return;
                }
                if (ViewerImages.HasExtension(path) && ThumbnailOfFile is { } make)
                {
                    MakeThumbnail(entry, () => make(path));
                }
                if (source == MediaSource.Agent && live)
                {
                    AgentImageSeen?.Invoke(entry);
                }
            }
            finally
            {
                // After the entry's in, so a turn's end waiting for it sees it.
                DoneFinding();
            }
        });
    }

    private void AddImage(MediaSource source, ToolImage image, string origin, ConversationItem item, DateTimeOffset? at, long order, bool live)
    {
        var key = image.FilePath is { } path ? PathKey(path) : BytesKey(image.Image.Data);
        var entry = new MediaEntry(MediaKind.Image, image.Name, origin, image.FilePath, image, item, at, key, order);
        if (!Put(source, entry, live))
        {
            return;
        }
        if (ThumbnailOf is { } make)
        {
            MakeThumbnail(entry, () => make(image.Image.Data));
        }
        else
        {
            entry.Thumbnail = image.Image.Data;
        }
        if (source == MediaSource.Agent && live)
        {
            AgentImageSeen?.Invoke(entry);
        }
    }

    /// <summary>
    /// Puts <paramref name="entry"/> in its section, or its Cleared group if it's cleared, in place of what's there for its
    /// file or image, unless that's newer. Read back from the transcript, a cleared one stays cleared: everything read back
    /// came before it was cleared, as anything after would have restored it. Seen again live, it's restored.
    /// </summary>
    private bool Put(MediaSource source, MediaEntry entry, bool live)
    {
        entry.Tip = TipOf(entry);
        entry.Source = source;
        var shown = SectionOf(source);
        var cleared = ClearedOf(source);
        var known = shown.Find(entry.Key) ?? cleared.Find(entry.Key);
        if (known is not null && known.Order > entry.Order)
        {
            return false;
        }
        var key = ClearedKey(source, entry.Key);
        var isCleared = Cleared.Contains(key);
        if (isCleared && live)
        {
            SetCleared(key, false);
            ClearedChanged?.Invoke();
            isCleared = false;
        }
        if (known is not null)
        {
            shown.Remove(known);
            cleared.Remove(known);
        }
        entry.IsCleared = isCleared;
        return (isCleared ? cleared : shown).Put(entry);
    }

    /// <summary>"bug.png", then "Read · 14:02", then where its file is.</summary>
    private string TipOf(MediaEntry entry)
    {
        var when = entry.At is { } at && Time is { } clock ? $" · {MessageTimes.Short(at, clock)}" : "";
        var where = entry.Path is { } path ? $"\n{ShownPath?.Invoke(path) ?? path}" : "";
        return $"{entry.Name}\n{entry.Origin}{when}{where}";
    }

    /// <summary>
    /// Makes <paramref name="entry"/>'s thumbnail with the others, one at a time off the UI thread, so a transcript of many
    /// screenshots read back doesn't take every thread the pool has.
    /// </summary>
    private void MakeThumbnail(MediaEntry entry, Func<byte[]?> make)
    {
        var generation = _generation;
        _thumbnails = _thumbnails.ContinueWith(_ =>
        {
            byte[]? thumbnail;
            try
            {
                thumbnail = make();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return;
            }
            if (thumbnail is not null)
            {
                Post(() =>
                {
                    if (generation == _generation)
                    {
                        entry.Thumbnail = thumbnail;
                    }
                });
            }
        }, CancellationToken.None, TaskContinuationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// The conversation was cleared, or rewound and read back: it starts again. What's cleared is kept, for what's read
    /// back.
    /// </summary>
    internal void Clear()
    {
        _generation++;
        foreach (var section in Sections.Concat(ClearedSections))
        {
            section.Clear();
        }
    }

    private void Post(Action action)
    {
        if (Dispatcher is { } dispatcher)
        {
            dispatcher.Post(action);
        }
        else
        {
            action();
        }
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>A file's key: its full path, as the OS compares them.</summary>
    private static string PathKey(string path)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = path;
        }
        return "file:" + (PathComparer == StringComparer.OrdinalIgnoreCase ? full.ToUpperInvariant() : full);
    }

    /// <summary>An image's key when it has no file: its bytes, so the same screenshot or attachment twice shows once.</summary>
    private static string BytesKey(byte[] data) => "image:" + Convert.ToHexString(SHA256.HashData(data));
}
