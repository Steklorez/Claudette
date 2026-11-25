using Deventry.App.ViewModels;
using Deventry.Core.Power;
using Deventry.Core.RemoteControl;
using Deventry.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.Services;

/// <summary>
/// What Claude has on across the tabs, for <b>Keep this computer awake</b> (DESIGN.md §9, "Keeping the computer awake").
/// </summary>
/// <param name="Working">Tabs running a turn, not waiting on the user.</param>
/// <param name="Background">Tasks and agents Claude Code keeps running after a turn, in every tab.</param>
/// <param name="WaitingForLimit">Tabs waiting for a usage limit to reset, to carry on by themselves.</param>
public readonly record struct ClaudeWork(int Working, int Background, int WaitingForLimit)
{
    public bool Any => Working + Background + WaitingForLimit > 0;
}

/// <summary>
/// Keeps the computer from sleeping (DESIGN.md §9, "Keeping the computer awake"): while Settings → General's <b>Keep
/// this computer awake</b> is on, only while Claude has work to finish or whenever Deventry runs, and while tabs are
/// connected to the Claude app (DESIGN.md §18). Only system sleep: the screen can still turn off. The header's cup
/// shows it and turns the setting on and off. Owns the <see cref="ISleepBlocker"/>. Use on the UI thread.
/// </summary>
public sealed partial class KeepAwakeService : ObservableObject, IDisposable
{
    /// <summary>
    /// How long sleep stays held off after Claude's work ends, while only that held it: the next turn often starts
    /// straight after (a queued message, a check-in), and holding on is cheaper than letting go and taking it back.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly AppServices _services;
    private readonly ISleepBlocker _sleep;
    private readonly UiTimeout _grace;
    private readonly UiTimeout _timedGrace;
    private readonly UiTimeout _lettingSleep;
    private ClaudeWork _work;
    private bool _timedDue;
    private int _timedWorking;
    private string? _nextTimedMessage;
    private int _connectedTabs;
    private bool _remoteClientOn;
    private WhenDone _whenDone;
    private string? _whenDoneLine;

    public KeepAwakeService(AppServices services, ISleepBlocker sleep)
    {
        _services = services;
        _sleep = sleep;
        _grace = new UiTimeout(services.Time, services.Dispatcher);
        _timedGrace = new UiTimeout(services.Time, services.Dispatcher);
        _lettingSleep = new UiTimeout(services.Time, services.Dispatcher);
        Update();
    }

    private GeneralSettings Settings => _services.Settings.General;

    /// <summary>
    /// <b>Keep this computer awake</b>, which the header's cup turns on and off. Saved, so it holds at the next launch
    /// too.
    /// </summary>
    public bool IsOn
    {
        get => Settings.KeepAwake;
        set
        {
            if (value != Settings.KeepAwake)
            {
                Settings.KeepAwake = value;
                _services.SaveSettings();
            }
        }
    }

    /// <summary>When it holds sleep off, while it's on.</summary>
    public KeepAwakeWhen When => Settings.KeepAwakeWhen;

    public bool WhileClaudeHasWork => When == KeepAwakeWhen.ClaudeHasWork;

    public bool WhileDeventryRuns => When == KeepAwakeWhen.DeventryRuns;

    /// <summary>The cup's menu: when to keep the computer awake, which turns it on too.</summary>
    [RelayCommand]
    private void Choose(KeepAwakeWhen when)
    {
        Settings.KeepAwakeWhen = when;
        Settings.KeepAwake = true;
        _services.SaveSettings();
    }

    /// <summary>Sleep is held off now, for whatever reason: the cup steams.</summary>
    public bool IsKeepingAwake => _sleep.IsBlocking;

    /// <summary>This machine can be kept awake. When it can't, the cup is disabled and says why.</summary>
    public bool IsAvailable => _sleep.UnavailableReason is null;

    /// <summary>What the cup's tip says: what's happening and why, and what a click does.</summary>
    public string Tooltip => string.Join("\n", TooltipLines());

    /// <summary>The cup's name for screen readers, with its state.</summary>
    public string AccessibleName => IsKeepingAwake ? "Keep this computer awake: keeping it awake" : "Keep this computer awake";

    private IEnumerable<string> TooltipLines()
    {
        if (_sleep.UnavailableReason is { } reason)
        {
            yield return $"Deventry can't keep this computer awake here: {reason}";
            yield break;
        }
        if (IsKeepingAwake)
        {
            yield return "Keeping this computer awake. The screen can still turn off.";
            foreach (var why in Reasons())
            {
                yield return why;
            }
        }
        else
        {
            yield return IsOn ? "This computer can sleep: Claude has no work to finish." : "This computer can sleep.";
        }
        yield return IsOn
            ? "Click to let it sleep."
            : WhileClaudeHasWork ? "Click to keep it awake while Claude has work to finish." : "Click to keep it awake while Deventry runs.";
        yield return "Right-click to choose when.";
        if (_nextTimedMessage is { } next)
        {
            yield return next;
        }
    }

    /// <summary>The next timed message, for the end of the tip (DESIGN.md §18, "Timed messages", "The header's cup").</summary>
    public void SetNextTimedMessage(string? line)
    {
        if (line != _nextTimedMessage)
        {
            _nextTimedMessage = line;
            OnPropertyChanged(nameof(Tooltip));
        }
    }

    /// <summary>Why sleep is held off, a line each.</summary>
    private IEnumerable<string> Reasons()
    {
        if (IsOn && WhileDeventryRuns)
        {
            yield return "While Deventry runs, as Settings → General says.";
        }
        else if (IsOn && _work.Any)
        {
            yield return $"While Claude has work to finish: {DescribeWork(_work)}.";
        }
        else if (IsOn && _grace.IsPending)
        {
            yield return "Claude's work just finished: it can sleep again in a couple of minutes.";
        }
        if (_connectedTabs > 0 && _services.Settings.ClaudeCode.KeepAwakeWhileConnected)
        {
            yield return _connectedTabs == 1 ? "1 tab is connected to the Claude app." : $"{_connectedTabs} tabs are connected to the Claude app.";
        }
        if (WhileRemoteClient)
        {
            yield return "A remote client is on: a sleeping computer can't answer it.";
        }
        if (_timedDue)
        {
            yield return "A timed message is due.";
        }
        if (_timedWorking > 0 || _timedGrace.IsPending)
        {
            yield return _timedWorking > 1 ? $"{_timedWorking} timed messages' tabs are working." : "A timed message's tab is working.";
        }
        if (_whenDoneLine is { } whenDone)
        {
            yield return whenDone;
        }
    }

    /// <summary>"2 tabs working, 1 waiting for a usage limit to reset".</summary>
    internal static string DescribeWork(ClaudeWork work)
    {
        var parts = new List<string>(3);
        if (work.Working > 0)
        {
            parts.Add(work.Working == 1 ? "1 tab working" : $"{work.Working} tabs working");
        }
        if (work.Background > 0)
        {
            parts.Add(work.Background == 1 ? "1 task running in the background" : $"{work.Background} tasks running in the background");
        }
        if (work.WaitingForLimit > 0)
        {
            parts.Add(work.WaitingForLimit == 1 ? "1 tab waiting for a usage limit to reset" : $"{work.WaitingForLimit} tabs waiting for a usage limit to reset");
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// What Claude has on, from the tabs. When the last of it ends, sleep is held off for <see cref="Grace"/> more, in
    /// case more starts.
    /// </summary>
    public void SetWork(ClaudeWork work)
    {
        if (work == _work)
        {
            return;
        }
        if (_work.Any && !work.Any)
        {
            _grace.Restart(Grace, Update);
        }
        else if (work.Any)
        {
            _grace.Cancel();
        }
        _work = work;
        Update();
    }

    /// <summary>How many tabs are connected to the Claude app (DESIGN.md §18, "Remote Control").</summary>
    public void SetConnectedTabs(int count)
    {
        if (count != _connectedTabs)
        {
            _connectedTabs = count;
            Update();
        }
    }

    /// <summary>
    /// A remote client is on, or not (DESIGN.md §18, "Remote clients"): while one is, sleep is held off, if Settings →
    /// General allows, since a sleeping computer can't answer it.
    /// </summary>
    public void SetRemoteClientOn(bool on)
    {
        if (on != _remoteClientOn)
        {
            _remoteClientOn = on;
            Update();
        }
    }

    private bool WhileRemoteClient => _remoteClientOn && _services.Settings.RemoteClients.KeepAwake;

    /// <summary>
    /// A timed message is due within a couple of minutes, and how many timed messages' tabs are working on their first
    /// turn (DESIGN.md §18, "Timed messages", "Keeping the computer awake for it"). Sleep is held off for them, and two
    /// minutes after, whatever <b>Keep this computer awake</b> says: Windows puts a computer a timer woke back to sleep
    /// a couple of minutes later unless something holds it.
    /// </summary>
    public void SetTimedMessages(bool due, int working)
    {
        if (due == _timedDue && working == _timedWorking)
        {
            return;
        }
        if (_timedWorking > 0 && working == 0)
        {
            _timedGrace.Restart(Grace, Update);
        }
        else if (working > 0)
        {
            _timedGrace.Cancel();
        }
        _timedDue = due;
        _timedWorking = working;
        Update();
    }

    private bool ForTimedMessages => _timedDue || _timedWorking > 0 || _timedGrace.IsPending;

    /// <summary>
    /// <b>When Claude is done</b> is on, or off (DESIGN.md §9, "When Claude is done"): until it happens, sleep is held off,
    /// so the computer gets there, whatever <b>Keep this computer awake</b> says; <paramref name="line"/> is the tip's line
    /// for it, what it waits for.
    /// </summary>
    public void SetWhenDone(WhenDone choice, string? line)
    {
        if (choice != _whenDone || line != _whenDoneLine)
        {
            _whenDone = choice;
            _whenDoneLine = choice == WhenDone.Nothing ? null : line;
            Update();
        }
    }

    /// <summary>Settings → General, or Settings → Claude Code's <b>Keep this computer awake while tabs are connected</b>, may have changed.</summary>
    public void OnSettingsChanged() => Update();

    /// <summary>
    /// <b>Sleep</b> when Claude is done is putting the computer to sleep (DESIGN.md §9, "When Claude is done"): for
    /// <paramref name="span"/>, nothing of Deventry's holds sleep off, whatever else would (a remote client on, say), so a
    /// computer that sleeps once its display is off does. Then, on waking or when it didn't sleep, they hold it again.
    /// </summary>
    public void LetSleep(TimeSpan span)
    {
        _lettingSleep.Restart(span, Update);
        Update();
    }

    private bool Wanted => !_lettingSleep.IsPending && (
        (IsOn && (WhileDeventryRuns || _work.Any || _grace.IsPending))
        || (_connectedTabs > 0 && _services.Settings.ClaudeCode.KeepAwakeWhileConnected)
        || WhileRemoteClient
        || ForTimedMessages
        || _whenDone != WhenDone.Nothing);

    private void Update()
    {
        _sleep.SetBlocking(Wanted);
        // The cup follows: its state, its tip and its menu's checks.
        OnPropertyChanged(string.Empty);
    }

    /// <summary>For Settings → Advanced → Diagnostics.</summary>
    public string Describe()
    {
        var setting = !IsOn ? "Keep this computer awake is off."
            : WhileDeventryRuns ? "Keep this computer awake is on, while Deventry runs."
            : _work.Any ? $"Keep this computer awake is on, while Claude has work to finish: {DescribeWork(_work)}."
            : _grace.IsPending ? "Keep this computer awake is on, while Claude has work to finish: it just finished."
            : "Keep this computer awake is on, while Claude has work to finish: it has none.";
        var tabs = _connectedTabs switch
        {
            0 => "No tab is connected to the Claude app.",
            1 => "1 tab is connected to the Claude app.",
            var n => $"{n} tabs are connected to the Claude app.",
        };
        var connected = _services.Settings.ClaudeCode.KeepAwakeWhileConnected ? "" : " Keeping the computer awake for them is turned off in Settings → General.";
        var remote = !_remoteClientOn ? "" : _services.Settings.RemoteClients.KeepAwake
            ? " A remote client is on."
            : " A remote client is on; keeping the computer awake for it is turned off in Settings → General.";
        var timed = !ForTimedMessages ? "" : _timedDue ? " A timed message is due." : " A timed message's tab is working.";
        var whenDone = _whenDoneLine is { } line ? $" {line}" : "";
        return $"{setting} {tabs}{connected}{remote}{timed}{whenDone} {_sleep.Describe()}";
    }

    /// <summary>At exit: the computer can sleep again.</summary>
    public void Dispose()
    {
        _grace.Dispose();
        _timedGrace.Dispose();
        _lettingSleep.Dispose();
        _sleep.SetBlocking(false);
        _sleep.Dispose();
    }
}
