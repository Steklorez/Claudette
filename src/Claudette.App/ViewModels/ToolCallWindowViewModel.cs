using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Claudette.App.Conversation;
using Claudette.App.Diffs;
using Claudette.App.Services;
using Claudette.Core;
using Claudette.Core.Diffs;
using Claudette.Core.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>What a tool call's window shows first (DESIGN.md §18, "Conversation cards").</summary>
public enum ToolCallView
{
    /// <summary>A command and its output.</summary>
    Command,

    /// <summary>The file as Claude read it, with its line numbers and colors.</summary>
    File,

    /// <summary>The change an Edit or Write made.</summary>
    Diff,

    /// <summary>What a Grep or Glob found, file by file.</summary>
    Search,

    /// <summary>Any other tool: its arguments and its result.</summary>
    Other,
}

/// <summary>A line of a file a Read read, with its number and syntax colors.</summary>
public sealed record CodeLine(string Number, string Text, IReadOnlyList<ColoredRun>? Runs)
{
    /// <summary>What a screen reader reads for its row.</summary>
    public override string ToString() => Text;
}

/// <summary>A file a search found, with the lines that matched, when it says which.</summary>
public sealed record SearchFile(string Path, IReadOnlyList<SearchMatch> Matches)
{
    public bool HasMatches => Matches.Count > 0;
}

/// <summary>A line a search matched: its number, when the search gave one, and its text.</summary>
public sealed record SearchMatch(string Number, string Text);

/// <summary>
/// A tool call in a window of its own (DESIGN.md §18, "Conversation cards"), opened from its tile. It follows the call
/// while it runs: the running time ticks by the injected clock, and the result fills in when it comes. Claude Code
/// sends a command's output only when it ends, so a running command shows its time, not its output.
/// </summary>
public sealed partial class ToolCallWindowViewModel : ObservableObject, IDisposable
{
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private readonly UiTicker _ticker;
    private readonly Action<ToolUseItem>? _openDiff;
    private bool _dark;
    private int _highlights;

    /// <param name="openDiff">Opens the call's file in the diff view, as the tile's Open diff does.</param>
    public ToolCallWindowViewModel(ToolUseItem tool, TimeProvider time, IUiDispatcher dispatcher, bool dark, Action<ToolUseItem>? openDiff = null)
    {
        Tool = tool;
        _time = time;
        _dispatcher = dispatcher;
        _dark = dark;
        _openDiff = openDiff;
        _ticker = new UiTicker(time, dispatcher, TimeSpan.FromSeconds(1), () => OnPropertyChanged(nameof(StatusLine)));
        Tool.PropertyChanged += OnToolChanged;
        _ticker.Run(!tool.IsComplete);
        Fill();
    }

    public ToolUseItem Tool { get; }

    /// <summary>"Read · src/auth.cs", for the window's title and header.</summary>
    public string Title => Tool.Summary.Length > 0 ? $"{Tool.Name} · {Tool.Summary}" : Tool.Name;

    /// <summary>
    /// How the call stands: "Running · 12s" while it runs, then its tile's state, with an Edit's counts: "Done · 0.4s",
    /// "120 lines · 0.3s", "Failed · 2.1s".
    /// </summary>
    public string StatusLine
    {
        get
        {
            if (!Tool.IsComplete)
            {
                return Tool.StartedAt is { } started ? $"Running · {Formats.Elapsed(_time.GetUtcNow() - started)}" : "Running…";
            }
            return Tool.HasDiff && !Tool.IsError ? $"{Tool.DiffStats} · {Tool.StateText}" : Tool.StateText;
        }
    }

    public ToolCallView View => Tool switch
    {
        { IsBash: true } => ToolCallView.Command,
        { Name: "Read" } when Tool.HasReadText || !Tool.IsComplete => ToolCallView.File,
        { HasDiff: true } => ToolCallView.Diff,
        { Name: "Grep" or "Glob" } => ToolCallView.Search,
        _ => ToolCallView.Other,
    };

    public bool IsCommand => View == ToolCallView.Command;

    public bool IsFile => View == ToolCallView.File;

    public bool IsDiff => View == ToolCallView.Diff;

    public bool IsSearch => View == ToolCallView.Search;

    public bool IsOther => View == ToolCallView.Other;

    /// <summary>The file as Claude read it, line by line, numbered from where it started reading.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CodeLine> FileLines { get; private set; } = [];

    /// <summary>What a search found, file by file.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SearchFile> SearchFiles { get; private set; } = [];

    /// <summary>A search found nothing, or its output couldn't be read file by file: it shows as text instead.</summary>
    public bool ShowsSearchText => IsSearch && SearchFiles.Count == 0;

    /// <summary>The call's arguments, as the tool received them.</summary>
    public string InputText => Tool.Input.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>What the tool returned, as text: its output, or the file a Read read.</summary>
    public string? ResultText => Tool.Output ?? Tool.ReadText ?? Tool.ResultSummary;

    public bool HasResultText => !string.IsNullOrEmpty(ResultText);

    public bool CanOpenDiff => Tool.CanOpenDiff && _openDiff is not null;

    [RelayCommand]
    private void OpenDiff() => _openDiff?.Invoke(Tool);

    /// <summary>The syntax colors follow the theme while the window is open, as the diff view's do.</summary>
    public void UseDarkColors(bool dark)
    {
        if (dark != _dark)
        {
            _dark = dark;
            FillFile();
        }
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ToolUseItem.IsComplete):
                _ticker.Run(!Tool.IsComplete);
                Fill();
                break;
            case nameof(ToolUseItem.Output) or nameof(ToolUseItem.ReadText) or nameof(ToolUseItem.Diff) or nameof(ToolUseItem.ResultSummary):
                Fill();
                break;
        }
        OnPropertyChanged(nameof(StatusLine));
    }

    private void Fill()
    {
        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(IsCommand));
        OnPropertyChanged(nameof(IsFile));
        OnPropertyChanged(nameof(IsDiff));
        OnPropertyChanged(nameof(IsSearch));
        OnPropertyChanged(nameof(IsOther));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(HasResultText));
        OnPropertyChanged(nameof(CanOpenDiff));
        FillFile();
        SearchFiles = IsSearch ? Search(Tool) : [];
        OnPropertyChanged(nameof(ShowsSearchText));
    }

    /// <summary>The Read's lines, first without colors, then with them once they're worked out away from the UI thread.</summary>
    private void FillFile()
    {
        if (Tool.ReadText is not { } text)
        {
            FileLines = [];
            return;
        }
        var lines = LineDiff.SplitLines(text);
        var first = Tool.ReadStartLine;
        FileLines = Lines(lines, first, null);
        var path = Tool.Input.GetString("file_path") ?? "";
        var dark = _dark;
        var highlight = ++_highlights;
        _ = Task.Run(() => SyntaxHighlighter.Highlight(lines, Path.GetFileName(path), dark)).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result is { } runs)
            {
                _dispatcher.Post(() =>
                {
                    // A later fill, or the theme changing, made this one stale.
                    if (highlight == _highlights)
                    {
                        FileLines = Lines(lines, first, runs);
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    private static List<CodeLine> Lines(IReadOnlyList<string> lines, int first, IReadOnlyList<IReadOnlyList<ColoredRun>>? runs) =>
        [.. lines.Select((line, i) => new CodeLine((first + i).ToString(System.Globalization.CultureInfo.InvariantCulture), line, runs is not null && i < runs.Count ? runs[i] : null))];

    // ripgrep's content lines: "path:12:text" or, around a match, "path-12-text"; a Windows path starts with its drive.
    [GeneratedRegex(@"^(?<path>(?:[A-Za-z]:)?[^:\r\n]+?)[:\-](?<line>\d+)[:\-](?<text>.*)$")]
    private static partial Regex ContentLine();

    /// <summary>
    /// What a Grep or Glob found, file by file: the files it lists, and for a Grep's content mode the lines under each.
    /// Empty when its output doesn't read that way.
    /// </summary>
    internal static IReadOnlyList<SearchFile> Search(ToolUseItem tool)
    {
        if (!tool.IsComplete || tool.IsError)
        {
            return [];
        }
        if (tool.Name == "Grep" && tool.ResultInfo?.GrepMode == "content" && tool.Output is { } output)
        {
            var files = new List<(string Path, List<SearchMatch> Matches)>();
            foreach (var line in output.ReplaceLineEndings("\n").Split('\n'))
            {
                if (ContentLine().Match(line) is not { Success: true } match)
                {
                    continue;
                }
                var path = match.Groups["path"].Value;
                if (files.Count == 0 || files[^1].Path != path)
                {
                    files.Add((path, []));
                }
                files[^1].Matches.Add(new SearchMatch(match.Groups["line"].Value, match.Groups["text"].Value));
            }
            return [.. files.Select(f => new SearchFile(f.Path, f.Matches))];
        }
        return tool.ResultInfo?.Filenames is { Count: > 0 } names ? [.. names.Select(n => new SearchFile(n, []))] : [];
    }

    public void Dispose()
    {
        Tool.PropertyChanged -= OnToolChanged;
        _ticker.Dispose();
    }
}
