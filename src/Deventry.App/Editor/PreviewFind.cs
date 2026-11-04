using System.Text.RegularExpressions;
using Deventry.Core.ProjectFiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Editor;

/// <summary>A match in the preview: in its <see cref="Block"/>th text block, in the order they're laid out, from <see cref="Start"/>.</summary>
public readonly record struct PreviewMatch(int Block, int Start, int Length);

/// <summary>
/// Finding text in a Markdown file's preview (DESIGN.md §18, "Markdown preview", "Finding in the preview"), as the
/// editor's find box finds in its text: what's typed, with <b>Aa</b> (match case), <b>W</b> (whole words) and <b>.*</b>
/// (a regular expression), every match marked, the one it's on more so, and <i>3 of 12</i>. The view finds the matches in
/// the text as the preview lays it out (<see cref="Find"/>) and says how many there are; this keeps which one it's on.
/// </summary>
public sealed partial class PreviewFind : ObservableObject
{
    /// <summary>The find box shows over the preview.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    public partial bool WholeWords { get; set; }

    [ObservableProperty]
    public partial bool IsRegex { get; set; }

    /// <summary>How many matches the preview has, as the view last found them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; private set; }

    /// <summary>The match it's on, from 0; -1 for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Current { get; private set; } = -1;

    /// <summary>Why what's typed can't be looked for, such as a regular expression that isn't one; null when it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem), nameof(CountText))]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is not null;

    /// <summary><i>3 of 12</i>, <i>No results</i>, or nothing while there's nothing to look for.</summary>
    public string CountText => Problem is not null || Query.Length == 0 ? "" : Count == 0 ? "No results" : $"{Current + 1} of {Count}";

    /// <summary>What to look for, or null while there's nothing to, or it can't be looked for.</summary>
    public FindMatcher? Matcher { get; private set; }

    /// <summary>What's looked for changed: the view finds the matches again, from the first.</summary>
    public event Action? QueryChanged;

    /// <summary>The box should take the keyboard, with its text selected.</summary>
    public event Action? FocusRequested;

    /// <summary>The match it's on changed: the view brings it into view.</summary>
    public event Action? CurrentChanged;

    /// <summary><c>Ctrl/Cmd+F</c> in the preview: the box shows, or takes the keyboard again, with its text selected.</summary>
    public void Open()
    {
        IsOpen = true;
        FocusRequested?.Invoke();
    }

    /// <summary>Esc, or ×: the box goes, and the marks with it.</summary>
    [RelayCommand]
    public void Close() => IsOpen = false;

    /// <summary>Enter: the next match, after the last the first.</summary>
    [RelayCommand]
    private void Next() => Step(1);

    /// <summary>Shift+Enter: the match before, before the first the last.</summary>
    [RelayCommand]
    private void Previous() => Step(-1);

    private void Step(int by)
    {
        if (Count == 0)
        {
            return;
        }
        Current = ((Current < 0 ? (by > 0 ? -1 : 0) : Current) + by + Count) % Count;
        CurrentChanged?.Invoke();
    }

    /// <summary>The view found <paramref name="count"/> matches: the one it's on stays, within them.</summary>
    public void Found(int count)
    {
        Count = count;
        var current = count == 0 ? -1 : Math.Clamp(Current < 0 ? 0 : Current, 0, count - 1);
        if (current != Current)
        {
            Current = current;
            CurrentChanged?.Invoke();
        }
    }

    partial void OnQueryChanged(string value) => Rebuild();

    partial void OnMatchCaseChanged(bool value) => Rebuild();

    partial void OnWholeWordsChanged(bool value) => Rebuild();

    partial void OnIsRegexChanged(bool value) => Rebuild();

    partial void OnIsOpenChanged(bool value) => QueryChanged?.Invoke();

    private void Rebuild()
    {
        Matcher = FindMatcher.TryCreate(new FindQuery(Query, MatchCase, WholeWords, IsRegex), out var matcher, out var problem) ? matcher : null;
        // Within a line of the preview, which is what's looked in: the find-in-files wording of it doesn't fit here.
        Problem = problem is not null && !IsRegex ? "What's found can't hold a line break." : problem;
        Current = -1;
        Count = 0;
        OnPropertyChanged(nameof(CountText));
        QueryChanged?.Invoke();
    }

    /// <summary>
    /// Every match of <paramref name="matcher"/> in the preview's <paramref name="blocks"/>' text, in order: each block's
    /// line by line, so none spans a line, as in the editor without a regular expression that spans them. A regular
    /// expression that takes too long on a line skips it.
    /// </summary>
    public static IReadOnlyList<PreviewMatch> Find(IReadOnlyList<string> blocks, FindMatcher matcher)
    {
        var found = new List<PreviewMatch>();
        var inLine = new List<(int Start, int Length)>();
        for (var block = 0; block < blocks.Count; block++)
        {
            var text = blocks[block];
            var lineStart = 0;
            while (lineStart <= text.Length)
            {
                var end = text.IndexOf('\n', lineStart);
                var line = text[lineStart..(end < 0 ? text.Length : end)];
                inLine.Clear();
                try
                {
                    matcher.Find(line, inLine);
                }
                catch (RegexMatchTimeoutException)
                {
                    inLine.Clear();
                }
                found.AddRange(inLine.Select(m => new PreviewMatch(block, lineStart + m.Start, m.Length)));
                if (end < 0)
                {
                    break;
                }
                lineStart = end + 1;
            }
        }
        return found;
    }
}
