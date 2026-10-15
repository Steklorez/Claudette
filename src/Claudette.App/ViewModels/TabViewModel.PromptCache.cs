using System.Globalization;
using Claudette.App.Conversation;
using Claudette.Core.Claude;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels;

/// <summary>
/// The prompt cache's chip in the composer's bar (DESIGN.md §6, "Prompt cache"): how long the next message can read
/// this tab's conversation from the prompt cache, counting down from the main agent's latest request.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The chip's text changes at most this often.</summary>
    public static readonly TimeSpan PromptCacheTickInterval = TimeSpan.FromSeconds(15);

    /// <summary>When the main conversation's cache was last refreshed, and the lifetime its writes show.</summary>
    private PromptCacheClock PromptCacheClock => field ??= new(_services.Time);

    /// <summary>Ticks while the chip shows on the selected tab and hasn't expired.</summary>
    private UiTicker PromptCacheTicker => field ??= new(_services.Time, _services.Dispatcher, PromptCacheTickInterval, RefreshPromptCache);

    /// <summary>What Claude Code's configuration says about the cache for this tab's <c>claude</c>, read as it starts.</summary>
    private PromptCacheConfig _promptCacheConfig = PromptCacheConfig.None;

    /// <summary>The variables the tab's <c>claude</c> started with over the user environment.</summary>
    private IReadOnlyDictionary<string, string?> _claudeVariables = new Dictionary<string, string?>();

    /// <summary>The session's <c>apiKeySource</c> from its latest <c>system/init</c>; null before its first turn.</summary>
    private string? _apiKeySource;

    /// <summary>
    /// The chip shows: the setting is on, the main agent has made a request in this conversation, Claude Code caches
    /// the tab's prompts, and no turn is running (its requests keep the cache fresh, and the working line shows).
    /// </summary>
    [ObservableProperty]
    public partial bool IsPromptCacheShown { get; private set; }

    /// <summary>"Cache 52m", "Cache &lt;1m" or "Cache expired".</summary>
    [ObservableProperty]
    public partial string PromptCacheText { get; private set; } = "";

    /// <summary>What the cache is, its lifetime and where that came from, and when it expires.</summary>
    [ObservableProperty]
    public partial string? PromptCacheTip { get; private set; }

    /// <summary>In its last three minutes: the chip takes the caution color.</summary>
    [ObservableProperty]
    public partial bool IsPromptCacheEnding { get; private set; }

    [ObservableProperty]
    public partial bool IsPromptCacheExpired { get; private set; }

    /// <summary>How long the cache lasts for the tab's next request, and why; null when Claude Code doesn't cache its prompts.</summary>
    internal PromptCacheTtl? PromptCacheLifetime =>
        PromptCache.Resolve(_promptCacheConfig, _modelId, PromptCacheClock.ReportedLifetime, PromptCacheBilling);

    private ClaudeBilling PromptCacheBilling =>
        PromptCache.BillingOf(Account?.IsApiKey == true, _apiKeySource, _promptCacheConfig, _services.RemoteControl.Account);

    /// <summary>Follows the main agent's requests, which refresh the cache, and what makes it no use.</summary>
    private void TrackPromptCache(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case TurnStarted started:
                _apiKeySource = started.Init.ApiKeySource ?? _apiKeySource;
                break;
            // An error Claude Code reports as a message, such as a sign-in or a usage limit, isn't a request that ran.
            case AssistantMessageReceived { Message: { ParentToolUseId: null, Error: null } message }:
                PromptCacheClock.CallEnded(message.CacheWrites);
                break;
            case TurnCompleted completed:
                PromptCacheClock.TurnEnded(completed.Result.CacheWrites);
                break;
            // Cleared or compacted: the conversation the cache holds isn't the one sent next.
            case ConversationReset:
            case SystemNotice { Message.CompactBoundary: not null }:
                PromptCacheClock.Forget();
                break;
            default:
                return;
        }
        RefreshPromptCache();
    }

    /// <summary>A main-agent call from the transcript a restored tab replays: a tab reopened within the hour shows what's left.</summary>
    private void ReplayForPromptCache(AssistantMessage message, DateTimeOffset? at)
    {
        if (at is { } time && message is { ParentToolUseId: null, Error: null })
        {
            PromptCacheClock.Replayed(time, message.CacheWrites);
        }
    }

    /// <summary>The next request goes to another model, which has a cache of its own.</summary>
    private void ForgetPromptCache()
    {
        PromptCacheClock.Forget();
        RefreshPromptCache();
    }

    /// <summary>Claude Code starts again under another account, which shares no cache and may sign in another way.</summary>
    private void ResetPromptCache()
    {
        PromptCacheClock.Reset();
        _apiKeySource = null;
        RefreshPromptCache();
    }

    /// <summary>
    /// Reads what Claude Code's configuration says about the cache for this tab's <c>claude</c>, in the background, as
    /// the session starts: the environment it starts with, and its settings files' <c>env</c> and <c>promptCacheTtl</c>
    /// (the project's files only when the tab uses them).
    /// </summary>
    internal async Task LoadPromptCacheConfigAsync()
    {
        var files = ClaudeSettingsFiles.ForSession(_services.ClaudeConfigDirectory, State.WithoutProjectSettings ? null : Folder);
        PromptCacheConfig config;
        try
        {
            var environment = await _services.ClaudeEnvironmentFor(_claudeVariables, _closing.Token).ConfigureAwait(false);
            config = await Task.Run(() => PromptCacheConfig.Read(environment, files), _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _services.Dispatcher.Post(() =>
        {
            _promptCacheConfig = config;
            RefreshPromptCache();
        });
    }

    /// <summary>
    /// Works the chip out again: after a request, a turn's start or end, a status or setting change, selecting the tab
    /// (catching up on a tab that wasn't ticking) and each tick.
    /// </summary>
    private void RefreshPromptCache()
    {
        var lifetime = PromptCacheLifetime;
        var countdown = lifetime is null ? null : PromptCacheClock.Countdown(lifetime.Lifetime);
        var shown = _services.Settings.Appearance.ShowPromptCacheExpiry && countdown is not null && Status != TabStatus.Working;
        IsPromptCacheShown = shown;
        // Only a tab on screen ticks, as the working line does; nothing changes once the cache has expired.
        PromptCacheTicker.Run(shown && IsShown && countdown is { IsExpired: false });
        if (!shown || lifetime is null || countdown is not { } left)
        {
            return;
        }
        IsPromptCacheExpired = left.IsExpired;
        IsPromptCacheEnding = left.IsEnding;
        PromptCacheText = left.IsExpired ? "Cache expired"
            : left.WholeMinutes < 1 ? "Cache <1m"
            : string.Create(CultureInfo.InvariantCulture, $"Cache {left.WholeMinutes}m");
        PromptCacheTip = PromptCacheTipFor(lifetime, left, PromptCacheBilling);
    }

    private string PromptCacheTipFor(PromptCacheTtl lifetime, PromptCacheCountdown left, ClaudeBilling billing)
    {
        var limits = billing == ClaudeBilling.Subscription;
        var what = limits
            ? "The prompt cache keeps this conversation for a while after each request. A message sent in that time reads it from the cache, which counts for much less toward your usage limits."
            : "The prompt cache keeps this conversation for a while after each request. A message sent in that time reads it from the cache, at a fraction of the price.";
        var length = lifetime.Lifetime >= PromptCache.OneHour ? "1-hour cache" : "5-minute cache";
        var why = lifetime.Source switch
        {
            PromptCacheTtlSource.ForcedFiveMinutes => $"set by {PromptCacheConfig.ForceFiveMinutesVariable}",
            PromptCacheTtlSource.TtlVariable => $"set by {PromptCacheConfig.TtlVariable}",
            PromptCacheTtlSource.Setting => $"set by {PromptCacheConfig.SettingKey} in Claude Code's settings",
            PromptCacheTtlSource.OneHourVariable => $"set by {PromptCacheConfig.OneHourVariable}",
            PromptCacheTtlSource.Reported => "seen in the last turn",
            _ => billing switch
            {
                ClaudeBilling.Subscription => "as Claude Code usually gives subscriptions",
                ClaudeBilling.ApiKey => "as Claude Code uses with an API key",
                _ => "as Claude Code uses with a cloud provider",
            },
        };
        var at = PromptCacheTime(left.ExpiresAt);
        var again = limits ? ", and all of it counts toward your usage limits." : " at the full price.";
        var when = left.IsExpired
            ? $"It expired at {at}, so your next message has Claude read all of this conversation again{again}"
            : $"Sent before {at}, your next message reads this conversation from the cache. After that, Claude reads all of it again{again}";
        return $"{what}\n{length}, {why}.\n{when}";
    }

    /// <summary>"14:52" today, "Tue 00:10" for a later day, or a past one as a message's time reads (<see cref="MessageTimes.Short"/>).</summary>
    private string PromptCacheTime(DateTimeOffset at)
    {
        var clock = _services.Time;
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).Date;
        return local.Date > today
            ? $"{local.ToString("ddd", CultureInfo.CurrentCulture)} {local.ToString("t", CultureInfo.CurrentCulture)}"
            : MessageTimes.Short(at, clock);
    }
}
