using System.Globalization;
using Deventry.Core;

namespace Deventry.Usage;

/// <summary>What a <see cref="TokenFinding"/> is about, for its icon.</summary>
public enum TokenFindingKind
{
    CacheExpired,
    CostlySubagent,
    HeavyResult,
    Compaction,
    ClaudeCode,
}

/// <summary>
/// Something in a breakdown that cost more than it needed to, said in plain words with its turn and what it cost
/// (DESIGN.md §18, "The breakdown").
/// </summary>
/// <param name="Turn">The turn it's about, by its number in its tab.</param>
/// <param name="CostUsd">What it cost, which findings are ordered by.</param>
public abstract record TokenFinding(int Turn, double CostUsd)
{
    public abstract TokenFindingKind Kind { get; }

    /// <summary>The finding in words, naming each model as <paramref name="modelName"/> does (*Opus 5.5*).</summary>
    public abstract string Describe(Func<string, string> modelName);

    private protected static string Short(long tokens) => Formats.Tokens(tokens);

    private protected static string Dollars(double usd) => Formats.Dollars(usd);

    private protected static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%");

    private protected static string CallsText(int count) => count == 1 ? "1 call" : string.Create(CultureInfo.InvariantCulture, $"{count} calls");
}

/// <summary>A turn whose first call found the prompt cache expired, and wrote the whole conversation to it again.</summary>
/// <param name="PreviousTurn">The tab's turn before it; null when it's the first in the breakdown.</param>
/// <param name="Idle">How long the main agent had made no call before it.</param>
/// <param name="HourLong">The cache had the 1-hour lifetime; otherwise 5 minutes.</param>
/// <param name="Tokens">What its first call wrote.</param>
/// <param name="Share">Its first call's share of the breakdown's cost.</param>
public sealed record CacheExpiredFinding(int Turn, int? PreviousTurn, TimeSpan? Idle, bool HourLong, long Tokens, double CostUsd, double Share) : TokenFinding(Turn, CostUsd)
{
    public override TokenFindingKind Kind => TokenFindingKind.CacheExpired;

    public override string Describe(Func<string, string> modelName)
    {
        var lifetime = HourLong ? "1-hour" : "5-minute";
        var sent = (Idle, PreviousTurn) switch
        {
            ({ } idle, { } before) => $"was sent {Formats.Duration(idle)} after turn {before}, after",
            ({ } idle, null) => $"was sent {Formats.Duration(idle)} after the call before it, after",
            _ => "was sent after",
        };
        return $"Turn {Turn} {sent} the {lifetime} prompt cache had expired. Its first call wrote the whole conversation to the cache again: "
            + $"{Short(Tokens)} tokens, {Dollars(CostUsd)}, {Percent(Share)} of this tab's cost.";
    }
}

/// <summary>A subagent that cost a tenth of the breakdown or more.</summary>
/// <param name="Type">Its type, <c>Explore</c>; null when its tool call gave none.</param>
/// <param name="Report">What its report added to the main conversation; null when it isn't known.</param>
/// <param name="Cheaper">Another subagent of the same type that ran on another model for less.</param>
public sealed record CostlySubagentFinding(int Turn, string? Type, int Calls, string Model, long Tokens, double CostUsd, double Share, long? Report, CheaperRun? Cheaper)
    : TokenFinding(Turn, CostUsd)
{
    public override TokenFindingKind Kind => TokenFindingKind.CostlySubagent;

    public override string Describe(Func<string, string> modelName)
    {
        var who = Type is { Length: > 0 } type ? $"The {type} subagent" : "A subagent";
        var report = Report is { } size ? $", for a report of {Short(size)} tokens" : "";
        var cheaper = Cheaper is { } run ? $" Turn {run.Turn}'s {(Type is { Length: > 0 } t ? t : "subagent")} ran on {modelName(run.Model)} for {Dollars(run.CostUsd)}." : "";
        return $"{who} in turn {Turn} made {CallsText(Calls)} on {modelName(Model)}: {Short(Tokens)} tokens, {Dollars(CostUsd)} ({Percent(Share)}){report}.{cheaper}";
    }
}

/// <summary>Another subagent of the same type, which cost less on another model.</summary>
public sealed record CheaperRun(int Turn, string Model, double CostUsd);

/// <summary>The tool result later calls read again the most, when that's a twentieth of the breakdown's cache reads or more.</summary>
/// <param name="Tokens">What the result added to the conversation.</param>
/// <param name="Calls">How many later calls read it again.</param>
/// <param name="ReadAgain">Its tokens times those calls.</param>
/// <param name="Until">What took it out of the conversation, if anything has.</param>
public sealed record HeavyResultFinding(int Turn, string Tool, long Tokens, int Calls, long ReadAgain, double CostUsd, UsageEventKind? Until) : TokenFinding(Turn, CostUsd)
{
    public override TokenFindingKind Kind => TokenFindingKind.HeavyResult;

    public override string Describe(Func<string, string> modelName)
    {
        var until = Until switch
        {
            UsageEventKind.Compacted => ", until it was compacted",
            UsageEventKind.Cleared => ", until the conversation was cleared",
            _ => "",
        };
        var more = Calls == 1 ? "1 more call" : string.Create(CultureInfo.InvariantCulture, $"{Calls} more calls");
        return $"The {Tool} result in turn {Turn} ({Short(Tokens)} tokens) stayed in the conversation for {more}{until}: {Short(ReadAgain)} tokens read again.";
    }
}

/// <summary>A compaction: how far it shrank the context, and what the main agent's calls cost either side of it.</summary>
/// <param name="Before">The context's tokens before it, as Claude Code said, or as its call read.</param>
/// <param name="After">The context's tokens at the main agent's next call.</param>
/// <param name="CostUsd">The compaction's own call's cost, when it's known.</param>
public sealed record CompactionFinding(int Turn, long? Before, long? After, double? CallCostBefore, double? CallCostAfter, double CostUsd) : TokenFinding(Turn, CostUsd)
{
    public override TokenFindingKind Kind => TokenFindingKind.Compaction;

    public override string Describe(Func<string, string> modelName)
    {
        var size = (Before, After) switch
        {
            ({ } before, { } after) => $" took the context from {Short(before)} to {Short(after)}",
            ({ } before, null) => $" took the context from {Short(before)}",
            (null, { } after) => $" took the context down to {Short(after)}",
            _ => "",
        };
        var calls = (CallCostBefore, CallCostAfter) switch
        {
            ({ } before, { } after) => $" A model call cost {Dollars(before)} on average just before it, {Dollars(after)} after.",
            _ => "",
        };
        return $"Compacting in turn {Turn}{size}.{calls}";
    }
}

/// <summary>Calls Claude Code made itself in a turn, which it doesn't show, that cost a twentieth of the breakdown or more.</summary>
/// <param name="FetchedPages">The turn fetched web pages, which Claude Code reads with a model of its own.</param>
public sealed record ClaudeCodeFinding(int Turn, string Model, long Tokens, double CostUsd, bool FetchedPages) : TokenFinding(Turn, CostUsd)
{
    public override TokenFindingKind Kind => TokenFindingKind.ClaudeCode;

    public override string Describe(Func<string, string> modelName)
    {
        var which = FetchedPages ? ", such as reading the pages WebFetch fetched" : "";
        return $"In turn {Turn}, Claude Code made calls of its own that it doesn't show{which}: {Short(Tokens)} tokens on {modelName(Model)}, {Dollars(CostUsd)}.";
    }
}
