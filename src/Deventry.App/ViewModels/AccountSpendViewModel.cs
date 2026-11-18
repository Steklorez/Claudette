using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Deventry.App.ViewModels;

/// <summary>
/// The header while the selected tab runs under an API key or a cloud provider (DESIGN.md §4, "Accounts"): the account,
/// and its estimated spend today, this week and this month, from Claude Code's own per-turn estimates on this machine,
/// and other machines' under an account of the same name while they share their usage, with the turns running under it
/// as they go, estimated from their calls until their own figures come. The plan meters are the Default account's, so
/// they make way.
/// </summary>
public sealed partial class AccountSpendViewModel : ViewModelBase, IDisposable
{
    /// <summary>How often the numbers are read again while shown, so a new day, week or month starts from nothing.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private readonly AppServices _services;
    private readonly UsageTracker _tracker;
    private readonly UiTicker _ticker;
    private readonly ILogger _logger;

    /// <summary>Bumped by each read, so an older read finishing late doesn't show.</summary>
    private int _reads;

    /// <summary>The recorded spend, as last read: the turns running are added to it as they go, without reading it again.</summary>
    private (double Today, double Week, double Month) _recorded;

    public AccountSpendViewModel(AppServices services, UsageTracker tracker)
    {
        _services = services;
        _tracker = tracker;
        _logger = services.Loggers.CreateLogger("Accounts");
        _ticker = new UiTicker(services.Time, services.Dispatcher, RefreshInterval, () => _ = RefreshAsync());
        tracker.SpendRecorded += OnSpendRecorded;
        tracker.RunningSpendChanged += OnRunningSpendChanged;
        tracker.TurnsImported += OnTurnsImported;
    }

    public void Dispose()
    {
        _tracker.SpendRecorded -= OnSpendRecorded;
        _tracker.RunningSpendChanged -= OnRunningSpendChanged;
        _tracker.TurnsImported -= OnTurnsImported;
        _ticker.Dispose();
    }

    /// <summary>The selected tab's API-key or cloud provider account; null while it runs under Default, and the plan meters show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown), nameof(Tag), nameof(KindText))]
    public partial ClaudeAccount? Account { get; private set; }

    public bool IsShown => Account is not null;

    public AccountTag? Tag => AccountTag.For(Account, showDefault: false);

    /// <summary>What the account is, beside its name: "API key, estimated", "Amazon Bedrock, estimated".</summary>
    public string KindText => Account is null ? "" : $"{Account.KindName}, estimated";

    [ObservableProperty]
    public partial string TodayText { get; private set; } = "";

    [ObservableProperty]
    public partial string WeekText { get; private set; } = "";

    [ObservableProperty]
    public partial string MonthText { get; private set; } = "";

    /// <summary>Whose tabs the spend is from: other machines' too, while they share their usage (DESIGN.md §6, "Sharing across machines").</summary>
    public string Tooltip =>
        (_services.Settings.Usage.ShareThroughLibrary
            ? $"Estimated spend on this {SpendSubject} from this machine's tabs, and other machines' tabs that sync to the session library under an account of the same name"
            : $"Estimated spend on this {SpendSubject} from this machine's tabs")
        + ", with the turns running counted as they go: Claude Code's own estimates at list price, not your bill. Click for usage details.";

    private string SpendSubject => Account?.Provider is { } provider ? $"{provider.Name} account" : "API key";

    /// <summary>The selected tab changed, or its account did.</summary>
    public void Show(ClaudeAccount? account)
    {
        Account = account;
        OnPropertyChanged(nameof(Tooltip));
        _ticker.Run(account is not null);
        if (account is null)
        {
            TodayText = WeekText = MonthText = "";
            return;
        }
        // Until it's read, the account's turns running aren't added to the last account's spend.
        _recorded = default;
        _ = RefreshAsync();
    }

    private void OnSpendRecorded(string accountId)
    {
        if (Account?.Id == accountId)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>A turn running under the account spent more: added to what was recorded, without reading it again.</summary>
    private void OnRunningSpendChanged(string accountId)
    {
        if (Account?.Id == accountId && _reads > 0)
        {
            Show(_recorded, _tracker.RunningSpend(accountId));
        }
    }

    private void Show((double Today, double Week, double Month) recorded, double running)
    {
        TodayText = Formats.Dollars(recorded.Today + running);
        WeekText = Formats.Dollars(recorded.Week + running);
        MonthText = Formats.Dollars(recorded.Month + running);
    }

    /// <summary>Another machine's turns under an account of this one's name count toward its spend.</summary>
    private void OnTurnsImported(IReadOnlyCollection<string> accountNames)
    {
        if (Account is { } account && accountNames.Contains(account.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Reads the spend off the UI thread.</summary>
    internal async Task RefreshAsync()
    {
        if (Account is not { } account)
        {
            return;
        }
        var read = ++_reads;
        var now = _services.Time.GetLocalNow();
        var name = account.Name;
        double today, week, month;
        try
        {
            (today, week, month) = await Task.Run(() => (
                _tracker.Store.GetSpend(account.Id, SpendPeriods.StartOfDay(now), name),
                _tracker.Store.GetSpend(account.Id, SpendPeriods.StartOfWeek(now), name),
                _tracker.Store.GetSpend(account.Id, SpendPeriods.StartOfMonth(now), name)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't read {Account}'s spend for the header.", account.Name);
            return;
        }
        _services.Dispatcher.Post(() =>
        {
            if (read != _reads || Account?.Id != account.Id)
            {
                return;
            }
            _recorded = (today, week, month);
            Show(_recorded, _tracker.RunningSpend(account.Id));
        });
    }
}
