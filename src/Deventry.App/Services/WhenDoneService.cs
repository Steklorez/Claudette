using System.Globalization;
using Deventry.App.ViewModels;
using Deventry.Core.Power;
using Deventry.Core.Remote;
using Deventry.Core.TimedMessages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>What's left before <b>When Claude is done</b> can happen, across the tabs (DESIGN.md §9, "When Claude is done").</summary>
/// <param name="Claude">What keeping the computer awake counts: turns running, tasks in the background, tabs waiting for a usage limit to reset.</param>
/// <param name="WaitingOnYou">Tabs with a prompt, a question or a plan waiting.</param>
/// <param name="ProjectJobs">Project tools' jobs running, such as a build.</param>
public readonly record struct WorkLeft(ClaudeWork Claude, int WaitingOnYou, int ProjectJobs)
{
    public bool Any => Claude.Any || WaitingOnYou > 0 || ProjectJobs > 0;
}

/// <summary>
/// <b>When Claude is done</b>, in the header cup's menu (DESIGN.md §9): <b>Shut down</b> or <b>Sleep</b>, once nothing is
/// left: no tab working, waiting for a usage limit to reset or waiting on the user, no project job running, and no timed
/// message due within the hour (or, for sleep on a computer Deventry can wake, about to go). First a minute's countdown,
/// which the banner, a notification and the remote clients show, any of them cancelling it. Once only: it's off once it
/// happens or is cancelled, and Deventry starts without it. Use on the UI thread.
/// </summary>
public sealed partial class WhenDoneService : ObservableObject, IDisposable
{
    /// <summary>How long the countdown gives the user to cancel.</summary>
    public static readonly TimeSpan Countdown = TimeSpan.FromMinutes(1);

    /// <summary>A timed message due this soon holds a shutdown off, so its turn runs first: a computer that's off can't be woken for it.</summary>
    public static readonly TimeSpan TimedMessageHorizon = TimeSpan.FromHours(1);

    /// <summary>How long the remote clients have to say the computer is going to sleep before it does.</summary>
    internal static readonly TimeSpan SayBeforeSleeping = TimeSpan.FromSeconds(3);

    /// <summary>How long nothing of Deventry's holds sleep off as the computer goes to sleep: past it, it didn't, or it woke.</summary>
    internal static readonly TimeSpan LetSleepFor = TimeSpan.FromMinutes(2);

    private readonly AppServices _services;
    private readonly UiTimeout _countdown;
    private readonly UiTicker _tick;
    private readonly ILogger _logger;
    private WorkLeft _work;
    private DateTimeOffset? _nextTimed;
    private bool _timedDue;
    private int _timedWorking;
    private DateTimeOffset? _endsAt;
    private string? _problemTitle;

    public WhenDoneService(AppServices services, IComputerPower power)
    {
        _services = services;
        Power = power;
        _countdown = new UiTimeout(services.Time, services.Dispatcher);
        _tick = new UiTicker(services.Time, services.Dispatcher, TimeSpan.FromSeconds(1), () => OnPropertyChanged(nameof(BannerTitle)));
        _logger = services.Loggers.CreateLogger("WhenDone");
    }

    /// <summary>What shuts the computer down or puts it to sleep.</summary>
    public IComputerPower Power { get; }

    /// <summary>What happens once Claude is done: nothing, unless the cup's menu says.</summary>
    public WhenDone Choice { get; private set; }

    public bool ShutsDown => Choice == WhenDone.ShutDown;

    public bool Sleeps => Choice == WhenDone.Sleep;

    /// <summary>Deventry is closing to shut the computer down, which it does once it has closed.</summary>
    public bool ShutsDownOnClose { get; private set; }

    public bool IsCountingDown => _endsAt is not null;

    /// <summary>Why it couldn't be turned on, or couldn't happen, for the banner until it's dismissed.</summary>
    public string? Problem { get; private set; }

    // ---- The cup's menu ---------------------------------------------------------------------------------------------

    /// <summary><b>Shut down</b>: on, or off again.</summary>
    [RelayCommand(CanExecute = nameof(CanShutDown))]
    private Task ToggleShutDownAsync() => ToggleAsync(WhenDone.ShutDown);

    /// <summary><b>Sleep</b>: on, or off again.</summary>
    [RelayCommand(CanExecute = nameof(CanSleep))]
    private Task ToggleSleepAsync() => ToggleAsync(WhenDone.Sleep);

    private bool CanShutDown() => !ShutsDownOnClose && Power.UnavailableReason(WhenDone.ShutDown) is null;

    private bool CanSleep() => !ShutsDownOnClose && Power.UnavailableReason(WhenDone.Sleep) is null;

    private async Task ToggleAsync(WhenDone action)
    {
        if (Choice == action)
        {
            TurnOff(RemoteWhenDoneEnd.CancelledAtDesk, null);
            return;
        }
        SetProblem(null, null);
        // What it needs asked, it asks now, while the user is here: macOS's permission to tell System Events to shut down.
        if (await Power.PrepareAsync(action).ConfigureAwait(true) is { } why)
        {
            SetProblem(action == WhenDone.Sleep ? "Can't sleep when Claude is done:" : "Can't shut down when Claude is done:", why);
            return;
        }
        // The other one's countdown, if it was counting, ends here.
        StopCountdown(RemoteWhenDoneEnd.CancelledAtDesk, null);
        Choice = action;
        Check();
    }

    // ---- The banner -------------------------------------------------------------------------------------------------

    public bool ShowsBanner => Choice != WhenDone.Nothing || Problem is not null;

    public bool ShowsProblem => Choice == WhenDone.Nothing && Problem is not null;

    /// <summary>Waiting for Claude: the banner is quiet, as an info one; its countdown and a problem are cautions.</summary>
    public bool IsWaiting => Choice != WhenDone.Nothing && _endsAt is null && !ShutsDownOnClose;

    /// <summary>"Shutting down in 42 seconds.", "Sleeps once Claude is done:", or the problem's title.</summary>
    public string BannerTitle => Choice switch
    {
        WhenDone.Nothing => _problemTitle ?? "",
        _ when ShutsDownOnClose => "Shutting down:",
        _ when _endsAt is { } ends => $"{(Sleeps ? "Going to sleep" : "Shutting down")} in {Seconds(ends - _services.Time.GetUtcNow())}.",
        _ => Sleeps ? "Sleeps once Claude is done:" : "Shuts down once Claude is done:",
    };

    /// <summary>What it waits for, or why it can't.</summary>
    public string BannerText => Choice switch
    {
        WhenDone.Nothing => Problem ?? "",
        _ when ShutsDownOnClose => "Deventry is closing first.",
        _ when _endsAt is not null => "Claude is done.",
        _ => $"waiting for {WaitingFor()}.",
    };

    /// <summary><b>Shut down now</b> or <b>Sleep now</b>, while it counts down.</summary>
    public string NowLabel => Sleeps ? "Sleep now" : "Shut down now";

    public bool CanCancel => Choice != WhenDone.Nothing && !ShutsDownOnClose;

    [RelayCommand]
    private void Cancel() => TurnOff(RemoteWhenDoneEnd.CancelledAtDesk, null);

    [RelayCommand]
    private void GoNow()
    {
        if (_endsAt is not null)
        {
            Fire();
        }
    }

    [RelayCommand]
    private void DismissProblem() => SetProblem(null, null);

    private static string Seconds(TimeSpan left)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));
        return seconds == 1 ? "1 second" : string.Create(CultureInfo.InvariantCulture, $"{seconds} seconds");
    }

    // ---- What's left ------------------------------------------------------------------------------------------------

    /// <summary>The tabs' work changed. Kept while it's off, and looked at only while it's on.</summary>
    public void SetWork(WorkLeft work)
    {
        if (work != _work)
        {
            _work = work;
            CheckIfOn();
        }
    }

    /// <summary>The next timed message, whether one is due within a couple of minutes, and how many timed messages' tabs are on their first turn.</summary>
    public void SetTimedMessages(DateTimeOffset? next, bool due, int working)
    {
        if (next != _nextTimed || due != _timedDue || working != _timedWorking)
        {
            _nextTimed = next;
            _timedDue = due;
            _timedWorking = working;
            CheckIfOn();
        }
    }

    private void CheckIfOn()
    {
        if (Choice != WhenDone.Nothing && !ShutsDownOnClose)
        {
            Check();
        }
    }

    /// <summary>A timed message within the horizon: for sleep, none on a computer Deventry can wake for it, but one about to go.</summary>
    private bool TimedMessageSoon => _timedWorking > 0 || _timedDue
        || (_nextTimed is { } at && at - _services.Time.GetUtcNow() <= (Sleeps && CanWake ? TimeSpan.Zero : TimedMessageHorizon));

    private bool CanWake => _services.TimedMessages.Wake is { UnavailableReason: null, Caveat: null, Setup: not WakeSetup.NeedsHelper };

    private bool IsDone => !_work.Any && !TimedMessageSoon;

    /// <summary>"2 tabs working, 1 tab waiting on you, a timed message at 09:30".</summary>
    private string WaitingFor()
    {
        var parts = new List<string>();
        if (_work.Claude.Any)
        {
            parts.Add(KeepAwakeService.DescribeWork(_work.Claude));
        }
        if (_work.WaitingOnYou > 0)
        {
            parts.Add(_work.WaitingOnYou == 1 ? "1 tab waiting on you" : $"{_work.WaitingOnYou} tabs waiting on you");
        }
        if (_work.ProjectJobs > 0)
        {
            parts.Add(_work.ProjectJobs == 1 ? "1 project job running" : $"{_work.ProjectJobs} project jobs running");
        }
        if (_timedWorking > 0)
        {
            parts.Add(_timedWorking == 1 ? "a timed message's tab working" : $"{_timedWorking} timed messages' tabs working");
        }
        else if (TimedMessageSoon && _nextTimed is { } at)
        {
            parts.Add($"a timed message at {TimeZoneInfo.ConvertTime(at, _services.Time.LocalTimeZone).ToString("t", CultureInfo.CurrentCulture)}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "Claude";
    }

    /// <summary>Counts down once nothing is left, and stops counting once something is again.</summary>
    private void Check()
    {
        if (Choice == WhenDone.Nothing || ShutsDownOnClose)
        {
            _services.KeepAwake.SetWhenDone(WhenDone.Nothing, null);
            Changed();
            return;
        }
        var done = IsDone;
        if (done && _endsAt is null)
        {
            StartCountdown();
        }
        else if (!done && _endsAt is not null)
        {
            StopCountdown(RemoteWhenDoneEnd.WorkStarted, null);
        }
        // Held awake until it happens, so it gets there.
        _services.KeepAwake.SetWhenDone(Choice, done
            ? $"{(Sleeps ? "Going to sleep" : "Shutting down")} in a minute: Claude is done."
            : $"{(Sleeps ? "Sleeps" : "Shuts down")} once Claude is done: waiting for {WaitingFor()}.");
        Changed();
    }

    // ---- The countdown ----------------------------------------------------------------------------------------------

    private void StartCountdown()
    {
        var ends = _services.Time.GetUtcNow() + Countdown;
        // Set before the timer starts: a clock can run it at once.
        _endsAt = ends;
        _tick.Run(true);
        _countdown.Restart(Countdown, Fire);
        _services.Notifications.Notify(NotificationKind.WhenDone, Sleeps ? "Going to sleep in a minute" : "Shutting down in a minute",
            "Claude is done. Open Deventry to cancel.");
        _services.RemoteClients.OnWhenDoneCounting(Choice, ends);
    }

    /// <summary>The countdown ends, if it was counting, and the clients hear how.</summary>
    private void StopCountdown(RemoteWhenDoneEnd how, string? client)
    {
        if (_endsAt is null)
        {
            return;
        }
        _endsAt = null;
        _countdown.Cancel();
        _tick.Stop();
        _services.Notifications.Clear(NotificationKind.WhenDone);
        _services.RemoteClients.OnWhenDoneEnded(Choice, how, client);
    }

    private void TurnOff(RemoteWhenDoneEnd how, string? client)
    {
        if (ShutsDownOnClose)
        {
            return;
        }
        StopCountdown(how, client);
        Choice = WhenDone.Nothing;
        Check();
    }

    /// <summary>The minute is up, or <b>Shut down now</b>: Deventry closes and the computer shuts down, or it sleeps.</summary>
    private void Fire()
    {
        var action = Choice;
        if (_endsAt is null || action == WhenDone.Nothing)
        {
            return;
        }
        StopCountdown(RemoteWhenDoneEnd.Now, null);
        _logger.LogInformation("Claude is done: {Action}.", action == WhenDone.Sleep ? "going to sleep" : "closing to shut down");
        if (action == WhenDone.ShutDown)
        {
            // Closing as from the window, which shuts the computer down once it's done (App.OnMainWindowClosing).
            ShutsDownOnClose = true;
            Check();
            ToggleShutDownCommand.NotifyCanExecuteChanged();
            ToggleSleepCommand.NotifyCanExecuteChanged();
            CloseRequested?.Invoke();
            return;
        }
        // Once only: Deventry carries on after the computer wakes, without it.
        Choice = WhenDone.Nothing;
        Check();
        _ = SleepAsync();
    }

    private async Task SleepAsync()
    {
        // The clients have a moment to say so first.
        await Task.Delay(SayBeforeSleeping, _services.Time).ConfigureAwait(true);
        // A remote client on, or tabs connected to the Claude app, would hold it awake: they let go for it.
        _services.KeepAwake.LetSleep(LetSleepFor);
        if (await Power.RunAsync(WhenDone.Sleep).ConfigureAwait(true) is { } why)
        {
            _logger.LogWarning("Couldn't put the computer to sleep: {Why}", why);
            SetProblem("Couldn't put this computer to sleep:", why);
        }
    }

    /// <summary>Raised when it's time to close Deventry, as its window's close button does, to shut the computer down.</summary>
    public event Action? CloseRequested;

    // ---- From the remote clients ------------------------------------------------------------------------------------

    /// <summary>A client's <b>Cancel</b> on the countdown's message: it's off, as at the desk.</summary>
    internal RemoteResult CancelFromClient(string client)
    {
        if (ShutsDownOnClose)
        {
            return RemoteResult.No("It's shutting down already.");
        }
        if (Choice == WhenDone.Nothing)
        {
            return RemoteResult.No("Nothing is waiting to shut this computer down or put it to sleep.");
        }
        TurnOff(RemoteWhenDoneEnd.CancelledFromClient, client);
        return RemoteResult.Ok;
    }

    // ---- The rest ---------------------------------------------------------------------------------------------------

    private void SetProblem(string? title, string? problem)
    {
        _problemTitle = title;
        Problem = problem;
        Changed();
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        _countdown.Dispose();
        _tick.Dispose();
    }
}
