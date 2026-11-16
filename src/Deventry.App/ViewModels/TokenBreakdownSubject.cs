using Deventry.App.Conversation;
using Deventry.Core;

namespace Deventry.App.ViewModels;

/// <summary>
/// What a token breakdown window is of (DESIGN.md §18, "Opening it"): an open tab, or one the Usage panel lists that's
/// closed or another machine's, from what the usage history has of it.
/// </summary>
public interface ITokenBreakdownSubject
{
    /// <summary>The tab's id, which its turns are recorded under: one window per tab.</summary>
    string Id { get; }

    /// <summary>The window's title: the tab's name, and another machine's after it.</summary>
    string Name { get; }

    /// <summary>The project's folder name, for the window's toolbar; null when the history doesn't know it.</summary>
    string? Project { get; }

    string? Branch { get; }

    string? SessionId { get; }

    /// <summary>It runs on the plan, so it has a share of the session limit; a tab on an API key's account doesn't.</summary>
    bool UsesPlan { get; }

    /// <summary>Its conversation, for each turn's prompt; nothing for a tab that isn't open.</summary>
    IEnumerable<ConversationItem> Conversation { get; }

    /// <summary>Its context window, as its context indicator says it; null when it isn't known.</summary>
    long? ContextWindow { get; }

    /// <summary>A model's name, as the tab names it, or from its id.</summary>
    string ModelName(string model);
}

/// <summary>An open tab's breakdown: everything from the tab itself.</summary>
public sealed class OpenTabBreakdown(TabViewModel tab) : ITokenBreakdownSubject
{
    public TabViewModel Tab => tab;

    public string Id => tab.Id;

    public string Name => tab.DisplayName;

    public string? Project => tab.FolderName;

    public string? Branch => tab.TokenBranchName;

    public string? SessionId => tab.State.SessionId;

    public bool UsesPlan => tab.Account is null;

    public IEnumerable<ConversationItem> Conversation => tab.Items;

    public long? ContextWindow => tab.Context.Window;

    public string ModelName(string model) => tab.ModelNameFor(model);
}

/// <summary>
/// A tab that isn't open here, from the usage history (DESIGN.md §18, "The Usage panel"): closed, or another machine's
/// synced tab. Its prompts aren't known, so its turns go by their numbers.
/// </summary>
/// <param name="Machine">The machine whose tab it is, by name; null for this one's.</param>
public sealed record HistoryTabBreakdown(string Id, string TabName, string? Machine, string? Project, string? SessionId, bool UsesPlan) : ITokenBreakdownSubject
{
    public string Name => Machine is null ? TabName : $"{TabName} · {Machine}";

    public string? Branch => null;

    public IEnumerable<ConversationItem> Conversation => [];

    public long? ContextWindow => null;

    public string ModelName(string model) => Formats.ModelName(model);
}
