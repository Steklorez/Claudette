using Deventry.Core.Protocol;
using Deventry.Core.Sessions;

namespace Deventry.Core.Transcripts;

/// <summary>
/// A stretch of a session's transcript from one prompt to the next (DESIGN.md §18, "Older turns: reading the transcript"):
/// a turn as Claude Code recorded it, its subagents' calls included.
/// </summary>
/// <param name="Start">The prompt's time; null for what came before the transcript's first prompt.</param>
/// <param name="Prompt">What the user typed; null before the first prompt.</param>
/// <param name="Items">Its calls, tools' results and compactions, in order.</param>
public sealed record TranscriptTurn(DateTimeOffset? Start, string? Prompt, IReadOnlyList<TranscriptItem> Items)
{
    /// <summary>The time of its last entry: about when the turn ended.</summary>
    public DateTimeOffset? End => Items.LastOrDefault(i => i.Time is not null)?.Time ?? Start;

    /// <summary>The main agent made a call in it: a turn Claude worked in, rather than a note or an interrupt.</summary>
    public bool HasCalls => Items.Any(i => i is TranscriptMessage { Message: AssistantMessage { ParentToolUseId: null, Model: not null and not "<synthetic>" } });
}

/// <summary>
/// The turns of a session's transcript, for the token breakdown's older turns (DESIGN.md §18, "Older turns: reading the
/// transcript"), and the calls in them, as <see cref="TurnCalls"/> gathers them live.
/// </summary>
public static class TranscriptTurns
{
    /// <summary>
    /// The transcript cut at each prompt. A compaction's summary carries on its turn, and a background task's
    /// notification doesn't start one: Claude Code may hand it over mid-turn.
    /// </summary>
    public static IReadOnlyList<TranscriptTurn> Of(Transcript transcript)
    {
        var turns = new List<TranscriptTurn>();
        DateTimeOffset? start = null;
        string? prompt = null;
        var items = new List<TranscriptItem>();
        foreach (var item in transcript.Items)
        {
            switch (item)
            {
                case TranscriptPrompt { IsCompactSummary: false } next:
                    if (prompt is not null || items.Count > 0)
                    {
                        turns.Add(new TranscriptTurn(start, prompt, [.. items]));
                    }
                    (start, prompt) = (next.Time, next.Text);
                    items.Clear();
                    break;
                case TranscriptMessage or TranscriptCompaction:
                    items.Add(item);
                    break;
            }
        }
        if (prompt is not null || items.Count > 0)
        {
            turns.Add(new TranscriptTurn(start, prompt, [.. items]));
        }
        return turns;
    }

    /// <summary>One of a transcript turn's items, as the live stream gives it to <see cref="TurnCalls"/>.</summary>
    public static void Feed(TurnCalls calls, TranscriptItem item)
    {
        var at = item.Time ?? DateTimeOffset.MinValue;
        switch (item)
        {
            case TranscriptMessage { Message: AssistantMessage assistant }:
                calls.Add(assistant, at);
                break;
            case TranscriptMessage { Message: UserMessage results }:
                calls.AddResults(results, at);
                break;
            case TranscriptCompaction compaction:
                calls.Compacted(new CompactBoundaryNotice(compaction.Trigger, compaction.PreTokens), at);
                break;
        }
    }
}
