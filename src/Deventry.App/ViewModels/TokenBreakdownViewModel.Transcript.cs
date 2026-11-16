using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Transcripts;
using Deventry.Usage;

namespace Deventry.App.ViewModels;

/// <summary>
/// Older turns read from Claude Code's transcript (DESIGN.md §18, "Older turns: reading the transcript"): the first time
/// the window opens on turns that haven't been checked against their session's transcript, it reads it off the UI thread,
/// with a bar saying how far it has got and <b>Cancel</b>, checks each turn against it, takes in what it found, and says
/// how it went. A tab that isn't open has its turns' prompts from there too, each time.
/// </summary>
public sealed partial class TokenBreakdownViewModel
{
    private bool _transcriptStarted;
    private CancellationTokenSource? _transcriptCancel;
    private IReadOnlyDictionary<string, string> _transcriptPrompts = new Dictionary<string, string>();

    /// <summary>The transcript is being read: the bar shows its progress and <b>Cancel</b>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTranscriptBar))]
    public partial bool IsReadingTranscript { get; private set; }

    /// <summary>*Reading the transcript of turns 1–6 · 2.1 of 5.6 MB*.</summary>
    [ObservableProperty]
    public partial string TranscriptProgressText { get; private set; } = "";

    /// <summary>How far it has got, 0 to 1.</summary>
    [ObservableProperty]
    public partial double TranscriptProgress { get; private set; }

    /// <summary>How the check went, a line each, until it's dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTranscriptBar))]
    public partial IReadOnlyList<string> TranscriptLines { get; private set; } = [];

    public bool ShowsTranscriptBar => IsReadingTranscript || TranscriptLines.Count > 0;

    /// <summary>The latest reading of the transcript, for tests to wait on.</summary>
    internal Task TranscriptRead { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void CancelTranscript() => _transcriptCancel?.Cancel();

    [RelayCommand]
    private void DismissTranscript() => TranscriptLines = [];

    /// <summary>
    /// Once the breakdown has been read, the transcript, when some of the tab's turns haven't been checked against it, or
    /// for a tab that isn't open, for its prompts.
    /// </summary>
    private void StartTranscriptRead()
    {
        if (_transcriptStarted || _services.Usage is not { } usage || _breakdown is not { Turns.Count: > 0 })
        {
            return;
        }
        _transcriptStarted = true;
        _transcriptCancel = new CancellationTokenSource();
        TranscriptRead = ReadTranscriptAsync(usage, _transcriptCancel.Token);
    }

    private async Task ReadTranscriptAsync(UsageTracker usage, CancellationToken cancellationToken)
    {
        var store = usage.Store;
        var tabId = _tab.Id;
        var forPrompts = !_tab.Conversation.Any();
        // Every turn of the sessions to read, whatever the period: a session's turns are told apart by the ones around them.
        IReadOnlyList<IGrouping<string, TurnBreakdown>> sessions;
        TokenPrices prices;
        IReadOnlyDictionary<string, string> machines;
        try
        {
            (sessions, prices, machines) = await Task.Run(() =>
            {
                var all = TokenBreakdown.Read(store, tabId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
                IReadOnlyList<IGrouping<string, TurnBreakdown>> read = [.. all.Turns.Where(t => t.Records[0].SessionId is not null)
                    .GroupBy(t => t.Records[0].SessionId!, StringComparer.Ordinal)
                    .Where(g => forPrompts || g.Any(t => t.Check is null))];
                return (read, all.Prices, store.GetMachineNames());
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }
        if (sessions.Count == 0)
        {
            return;
        }
        var found = await Task.Run(() => sessions.Select(s => (Session: s, Path: TranscriptOf(s.Key, s.Any(t => t.Records[0].Machine is not null)))).ToList(),
            CancellationToken.None).ConfigureAwait(false);
        var reading = found.Where(f => f.Path is not null).ToList();
        var toCheck = reading.SelectMany(f => f.Session).Where(t => t.Check is null).Select(t => t.Number).ToList();
        var total = reading.Sum(f => SizeOf(f.Path!));
        var subagents = reading.Sum(f => TranscriptReader.FilesOf(f.Path!).Count - 1);
        var what = toCheck.Count > 0 ? $"the transcript of {TurnList(toCheck, lower: true)}" : "the transcript for the turns' prompts";
        _services.Dispatcher.Post(() =>
        {
            IsReadingTranscript = reading.Count > 0;
            TranscriptProgress = 0;
            TranscriptProgressText = Reading(what, 0, total, subagents);
        });

        var checks = new List<SessionCheck>();
        var cancelled = false;
        long done = 0;
        foreach (var (session, path) in reading)
        {
            var before = done;
            var progress = new Reporter(read => _services.Dispatcher.Post(() =>
            {
                if (IsReadingTranscript)
                {
                    TranscriptProgress = total > 0 ? Math.Clamp((double)(before + read) / total, 0, 1) : 0;
                    TranscriptProgressText = Reading(what, before + read, total, subagents);
                }
            }));
            try
            {
                var transcript = await TranscriptReader.ReadAsync(path!, endAt: null, progress, cancellationToken).ConfigureAwait(false);
                checks.Add(await Task.Run(() => TurnCheck.Check(session.Key, [.. session], TranscriptTurns.Of(transcript), prices, () => Guid.NewGuid().ToString("N")),
                    CancellationToken.None).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable now: its turns are tried again next time.
            }
            done += SizeOf(path!);
        }

        // What it found is kept, so a transcript is read once; a cancelled read keeps nothing.
        var turns = cancelled ? [] : checks.SelectMany(c => c.Turns).ToList();
        if (turns.Count > 0)
        {
            try
            {
                await Task.Run(() => store.ApplyCheck(tabId, turns), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                turns = [];
            }
        }
        var missing = found.Where(f => f.Path is null).SelectMany(f => f.Session).Where(t => t.Check is null).Select(t => t.Number).ToList();
        var lines = cancelled
            ? [$"Stopped reading the transcript. {TurnList(toCheck)} keep their totals without their calls, and it's read again next time."]
            : Describe(checks, missing, machines, forPrompts && toCheck.Count == 0);
        _services.Dispatcher.Post(() =>
        {
            _transcriptPrompts = checks.SelectMany(c => c.Prompts).GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            IsReadingTranscript = false;
            TranscriptLines = lines;
            if (turns.Count > 0)
            {
                Refresh();
            }
            else
            {
                Build();
            }
        });
    }

    /// <summary>
    /// A session's transcript: the open tab's own, this machine's in Claude Code's projects folder, or the session
    /// library's, first for a synced session other machines ran turns in, whose copy there has them.
    /// </summary>
    private string? TranscriptOf(string sessionId, bool synced)
    {
        string? Local() => _tab.TranscriptPathOf(sessionId) is { } own && File.Exists(own) ? own
            : _services.ProjectsDirectory is { } projects ? TranscriptReader.Find(projects, sessionId) : null;
        string? Library()
        {
            try
            {
                return _services.Library.Library.GetTranscriptPath(sessionId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return synced ? Library() ?? Local() : Local() ?? Library();
    }

    private static long SizeOf(string transcript) => TranscriptReader.FilesOf(transcript).Sum(f =>
    {
        try
        {
            return new FileInfo(f).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0L;
        }
    });

    private static string Reading(string what, long read, long total, int subagents) =>
        $"Reading {what} · {Formats.Size(read)} of {Formats.Size(total)}"
        + (subagents switch { 0 => "", 1 => " · with 1 subagent's transcript", _ => string.Create(CultureInfo.CurrentCulture, $" · with {subagents} subagents' transcripts") });

    /// <summary>How the check went, in the window's words (DESIGN.md §18, "Older turns").</summary>
    /// <param name="promptsOnly">The read was for a closed tab's prompts alone: nothing to say about its checks.</param>
    internal static IReadOnlyList<string> Describe(IReadOnlyList<SessionCheck> checks, IReadOnlyList<int> missing, IReadOnlyDictionary<string, string> machines, bool promptsOnly)
    {
        var lines = new List<string>();
        var turns = checks.SelectMany(c => c.Turns).ToList();
        var mine = turns.Where(t => t.Machine is null).ToList();
        var gotCalls = mine.Where(t => t.Outcome == TurnCheckOutcome.Agreed && t.Detail.Calls.Count > 0).Select(t => t.Number).ToList();
        if (gotCalls.Count > 0)
        {
            lines.Add($"{TurnList(gotCalls)} {Have(gotCalls)} {(gotCalls.Count == 1 ? "its" : "their")} model calls now, read from the transcript, and {Agree(gotCalls)} with the usage history.");
        }
        var agreed = mine.Where(t => t.Outcome == TurnCheckOutcome.Agreed && t.Detail.Calls.Count == 0).Select(t => t.Number).ToList();
        if (agreed.Count > 0)
        {
            lines.Add($"{TurnList(agreed)} {Agree(agreed)} with the transcript.");
        }
        foreach (var machine in turns.Where(t => t.Machine is not null).GroupBy(t => t.Machine!))
        {
            var name = MachineNames.Of(machine.Key, machines);
            var same = machine.Where(t => t.Outcome == TurnCheckOutcome.Agreed).Select(t => t.Number).ToList();
            var differ = machine.Where(t => t.Outcome == TurnCheckOutcome.Differs).Select(t => t.Number).ToList();
            if (same.Count > 0)
            {
                lines.Add($"{TurnList(same)} ran on {name}. {(same.Count == 1 ? "Its" : "Their")} figures came through the session library, and agree with the transcript.");
            }
            if (differ.Count > 0)
            {
                lines.Add($"{TurnList(differ)} ran on {name}, and {(differ.Count == 1 ? "its figures don't" : "their figures don't")} agree with the transcript. They're kept as {name} recorded them.");
            }
        }
        foreach (var corrected in mine.Where(t => t.Outcome == TurnCheckOutcome.Corrected))
        {
            lines.Add($"Turn {corrected.Number.ToString(CultureInfo.CurrentCulture)} didn't agree with the transcript, Claude Code's own record of each call, so it's corrected to it: "
                + $"{Formats.Tokens(corrected.Tokens)} tokens, {Formats.Dollars(corrected.CostUsd)}, from {Formats.Tokens(corrected.Before.Sum(r => r.Total))} tokens, {Formats.Dollars(corrected.Before.Sum(r => r.CostUsd))}.");
        }
        foreach (var added in mine.Where(t => t.Outcome == TurnCheckOutcome.Added))
        {
            lines.Add(string.Create(CultureInfo.CurrentCulture, $"A turn after turn {added.Number} wasn't recorded, perhaps cut short when Deventry closed. ")
                + string.Create(CultureInfo.CurrentCulture, $"Its {added.Calls} call{(added.Calls == 1 ? " is" : "s are")} added from the transcript ({Formats.Tokens(added.Tokens)} tokens, {Formats.Dollars(added.CostUsd)}), and marked."));
        }
        var notThere = turns.Where(t => t.Outcome == TurnCheckOutcome.NotInTranscript).Select(t => t.Number).ToList();
        if (notThere.Count > 0)
        {
            lines.Add($"{TurnList(notThere)} {(notThere.Count == 1 ? "isn't" : "aren't")} in the transcript, which has no turn that ran when {(notThere.Count == 1 ? "it" : "they")} did. {(notThere.Count == 1 ? "It's" : "They're")} kept as recorded.");
        }
        if (missing.Count > 0)
        {
            lines.Add($"The transcript of {TurnList(missing, lower: true)} isn't there: Claude Code deletes old ones. {(missing.Count == 1 ? "It keeps its totals" : "They keep their totals")} without calls.");
        }
        return promptsOnly && lines.Count == 0 ? [] : lines;

        static string Have(List<int> numbers) => numbers.Count == 1 ? "has" : "have";
        static string Agree(List<int> numbers) => numbers.Count == 1 ? "agrees" : "agree";
    }

    /// <summary>*Turn 4*, *Turns 3 and 4*, *Turns 1–6*, *Turns 1, 3 and 5–7*.</summary>
    internal static string TurnList(IEnumerable<int> numbers, bool lower = false)
    {
        var sorted = numbers.Distinct().Order().ToList();
        var runs = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
            {
                j++;
            }
            runs.Add(j - i >= 2 ? string.Create(CultureInfo.CurrentCulture, $"{sorted[i]}–{sorted[j]}")
                : j > i ? string.Create(CultureInfo.CurrentCulture, $"{sorted[i]}, {sorted[j]}")
                : sorted[i].ToString(CultureInfo.CurrentCulture));
            i = j + 1;
        }
        var parts = string.Join(", ", runs).Split(", ");
        var list = parts.Length == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
        var word = sorted.Count == 1 ? "Turn" : "Turns";
        return $"{(lower ? word.ToLowerInvariant() : word)} {list}";
    }

    /// <summary>Progress said from the read's thread, posted to the UI's by the callback.</summary>
    private sealed class Reporter(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
