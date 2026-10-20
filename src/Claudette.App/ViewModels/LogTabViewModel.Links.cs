using Claudette.Core.ProjectTools;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The links in a log (DESIGN.md §18, "Project tools", "A run's log"): web addresses, and paths once they're found to be
/// there, and a click on one.
/// </summary>
public abstract partial class LogTabViewModel
{
    /// <summary>At most this many paths are remembered as there or not, so a long log can't grow the list without end.</summary>
    private const int MaxRemembered = 2048;

    /// <summary>What each path, as the log wrote it, was found to be: its full path, or null when nothing's there.</summary>
    private readonly Dictionary<string, string?> _found = new(StringComparer.Ordinal);

    /// <summary>The paths being looked for now.</summary>
    private readonly HashSet<string> _looking = new(StringComparer.Ordinal);

    /// <summary>
    /// Where a path in the log is: read against <see cref="Folder"/>, then its tab's. A path elsewhere is looked at
    /// before a click only where looking can't reach another machine, as for a file a reply names.
    /// </summary>
    internal ReplyFileFinder Finder => field ??= CreateFinder();

    protected abstract ReplyFileFinder CreateFinder();

    /// <summary>Runs <paramref name="action"/> on the UI thread, for what a lookup off it found.</summary>
    protected abstract void Post(Action action);

    /// <summary>A path in the log turned out to be there: its view draws it as a link now.</summary>
    public event Action? LinksFound;

    /// <summary>
    /// The links in <paramref name="line"/> to show: every web address, and each path once it's found. A path not looked for
    /// yet is looked for now, off the UI thread, and <see cref="LinksFound"/> says when one is there.
    /// </summary>
    public IReadOnlyList<RunLogLink> LinksIn(string line)
    {
        var links = RunLogLinks.Find(line);
        return links.Any(l => l.File is not null) ? [.. links.Where(l => l.File is not { } file || IsFound(file))] : links;
    }

    private bool IsFound(ReplyFileReference file)
    {
        if (_found.TryGetValue(file.Path, out var full))
        {
            return full is not null;
        }
        if (_looking.Add(file.Path))
        {
            _ = LookForAsync(file);
        }
        return false;
    }

    private async Task LookForAsync(ReplyFileReference file)
    {
        string? full;
        try
        {
            full = (await Finder.FindAsync(file, [], clicked: false).ConfigureAwait(false)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            full = null;
        }
        Post(() =>
        {
            _looking.Remove(file.Path);
            if (_found.Count >= MaxRemembered)
            {
                _found.Clear();
            }
            _found[file.Path] = full;
            if (full is not null)
            {
                LinksFound?.Invoke();
            }
        });
    }

    /// <summary>
    /// A click on a link: a web address opens in the browser, and a file in Claudette's editor at the line named, or in the
    /// OS's app, as a file a reply names does; a program shows in its folder rather than running. Clicks don't wait for
    /// each other.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenLinkAsync(RunLogLink? link) => link is null ? Task.CompletedTask : FollowLinkAsync(link);

    protected abstract Task FollowLinkAsync(RunLogLink link);
}
