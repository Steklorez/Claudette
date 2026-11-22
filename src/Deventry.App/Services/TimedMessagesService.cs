using System.Globalization;
using Deventry.App.ViewModels;
using Deventry.Core;
using Deventry.Core.TimedMessages;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>A timed message as the cup's menu and Settings show it (DESIGN.md §18, "Timed messages").</summary>
/// <param name="Project">The folder's name, <i>starfall</i>.</param>
/// <param name="When">When it's sent, in words: <i>Weekdays at 7:00 AM</i>.</param>
/// <param name="Next">The next time it's sent, <i>tomorrow at 7:00 AM</i>, or null when it isn't again.</param>
/// <param name="Skipped">Why the last time it was due went by without it, until it's next sent; null when it didn't.</param>
public sealed record TimedMessageRow(TimedMessage Message, string Project, string When, string? Next, string? Skipped)
{
    public string Name => Message.Name;

    public bool IsOn => Message.IsOn;

    /// <summary>Its line in the cup's menu: <i>Morning triage · starfall · tomorrow at 7:00 AM</i>.</summary>
    public string MenuText => $"{Name} · {Project} · {(!IsOn ? "off" : Next ?? "not again")}";

    public bool HasSkipped => Skipped is not null;
}

/// <summary>
/// Sends the timed messages at their times (DESIGN.md §18, "Timed messages"), while Deventry runs: it opens each one's
/// tab through the shell and sends it, skips one noticed too late, sets the computer's wake for the next, and tells
/// <see cref="KeepAwakeService"/> when to hold sleep off. It times them on the injected clock: a timer to the next time,
/// and a tick every <see cref="TickInterval"/>, which also notices time that went by while the computer slept. Keeps the
/// messages in Settings → Timed messages and when each was last sent in the app state. Use on the UI thread.
/// </summary>
public sealed partial class TimedMessagesService : ObservableObject, IDisposable
{
    /// <summary>How often it looks, whatever the next time is: after sleep, a timer to it may come late.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly AppServices _services;
    private readonly DateTimeOffset _runningSince;
    private readonly ILogger _logger;

    /// <summary>The tabs timed messages opened, until their first turn ends; and of those, the ones that have worked.</summary>
    private readonly HashSet<string> _openedTabs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenWorking = new(StringComparer.Ordinal);

    private ShellViewModel? _shell;
    private ITimer? _tick;
    private ITimer? _next;
    private DateTimeOffset? _wake;
    private bool _wakeSet;
    private bool _dueSoon;
    private DateTimeOffset? _nextAt;
    private int _working;
    private bool _disposed;

    public TimedMessagesService(AppServices services, IWakeTimer wake)
    {
        _services = services;
        Wake = wake;
        _runningSince = services.Time.GetUtcNow();
        _logger = services.Loggers.CreateLogger("TimedMessages");
        Refresh(null);
    }

    /// <summary>What wakes the computer for the next one.</summary>
    public IWakeTimer Wake { get; }

    private List<TimedMessage> Messages => _services.Settings.TimedMessages.Messages;

    private TimeProvider Time => _services.Time;

    /// <summary>Any timed message is on: the cup has its clock.</summary>
    [ObservableProperty]
    public partial bool HasAny { get; private set; }

    /// <summary>Every timed message, as the cup's menu and Settings show them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TimedMessageRow> Rows { get; private set; } = [];

    /// <summary><i>Next timed message: Morning triage in starfall, tomorrow at 7:00 AM.</i>, for the cup's tip; null with none.</summary>
    [ObservableProperty]
    public partial string? NextLine { get; private set; }

    /// <summary>The tabs are up: from now on, messages are sent.</summary>
    public void Start(ShellViewModel shell)
    {
        _shell = shell;
        _tick ??= Time.CreateTimer(_ => _services.Dispatcher.Post(Check), null, TickInterval, TickInterval);
        Check();
    }

    // ---- Keeping them ---------------------------------------------------------------------------------------------

    /// <summary>The message with <paramref name="id"/>, or null.</summary>
    public TimedMessage? Find(string id) => Messages.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// Adds <paramref name="message"/>, or puts it in the place of the one with its id. Saved now, so a time before now is
    /// not one it missed.
    /// </summary>
    public void Save(TimedMessage message)
    {
        message.SavedAt = Time.GetUtcNow();
        var index = Messages.FindIndex(m => m.Id == message.Id);
        if (index >= 0)
        {
            Messages[index] = message;
        }
        else
        {
            Messages.Add(message);
        }
        _services.State.TimedMessagesSkipped.Remove(message.Id);
        _services.SaveSettings();
        _services.SaveState();
        Check();
    }

    /// <summary>Turns a message on or off, keeping it.</summary>
    public void SetOn(string id, bool on)
    {
        if (Find(id) is { } message && message.IsOn != on)
        {
            var changed = message.Clone();
            changed.IsOn = on;
            Save(changed);
        }
    }

    /// <summary><b>Clear…</b> in the cup's menu, and <b>Delete</b> in Settings: it won't be sent again.</summary>
    public void Delete(string id)
    {
        if (Messages.RemoveAll(m => m.Id == id) == 0)
        {
            return;
        }
        Forget(id);
        _services.SaveSettings();
        _services.SaveState();
        Check();
    }

    /// <summary><b>Clear all…</b>.</summary>
    public void DeleteAll()
    {
        foreach (var message in Messages)
        {
            Forget(message.Id);
        }
        Messages.Clear();
        _services.SaveSettings();
        _services.SaveState();
        Check();
    }

    private void Forget(string id)
    {
        _services.State.TimedMessagesHandled.Remove(id);
        _services.State.TimedMessagesSkipped.Remove(id);
    }

    // ---- The editor, asked for from outside Settings --------------------------------------------------------------

    private TimedMessage? _editorRequest;

    /// <summary>An editor was asked for (<see cref="RequestEditor"/>): Settings → Timed messages, if it's open, takes it.</summary>
    public event Action? EditorRequested;

    /// <summary>
    /// Settings → Timed messages opens on an editor for <paramref name="message"/>: a new one, or a copy of one to change.
    /// The cup's menu, a group's menu and the command palette ask, then open the page.
    /// </summary>
    public void RequestEditor(TimedMessage message)
    {
        _editorRequest = message;
        EditorRequested?.Invoke();
    }

    /// <summary>The editor asked for, once: the page takes it as it opens, or when told.</summary>
    public TimedMessage? TakeEditorRequest()
    {
        var message = _editorRequest;
        _editorRequest = null;
        return message;
    }

    /// <summary>A new message in <paramref name="folder"/>, weekdays at 7:00, for the editor to fill in.</summary>
    public static TimedMessage NewMessage(string? folder) => new() { Folder = folder ?? "" };

    /// <summary>The next times <paramref name="schedule"/> gives from now, up to <paramref name="count"/>, for the editor.</summary>
    public IReadOnlyList<DateTimeOffset> NextTimes(TimedSchedule schedule, int count)
    {
        var times = new List<DateTimeOffset>(count);
        var after = Time.GetUtcNow();
        while (times.Count < count && schedule.Next(after, Time.LocalTimeZone) is { } next)
        {
            times.Add(next);
            after = next;
        }
        return times;
    }

    // ---- Sending them ---------------------------------------------------------------------------------------------

    /// <summary>Looks at the time: sends what's due, skips what's too late, and sets the next timer and wake.</summary>
    internal void Check()
    {
        if (_disposed)
        {
            return;
        }
        var now = Time.GetUtcNow();
        var plan = TimedMessagePlanner.Plan(Messages, _services.State.TimedMessagesHandled, now, _runningSince, Time.LocalTimeZone);
        if (_shell is not null && (plan.Due.Count > 0 || plan.Skipped.Count > 0))
        {
            foreach (var skip in plan.Skipped)
            {
                _services.State.TimedMessagesHandled[skip.Message.Id] = skip.At;
                _services.State.TimedMessagesSkipped[skip.Message.Id] = skip.At;
                _services.Notifications.Notify(NotificationKind.TimedMessage, $"Timed message skipped: {skip.Message.Name}",
                    $"Its time, {Formats.When(skip.At, Time)}, went by while the computer was asleep.", key: skip.Message.Id);
            }
            foreach (var due in plan.Due)
            {
                // Recorded before it goes, so the next look doesn't send it again.
                _services.State.TimedMessagesHandled[due.Message.Id] = due.At;
                _services.State.TimedMessagesSkipped.Remove(due.Message.Id);
                _ = SendAsync(due.Message);
            }
            _services.SaveState();
        }
        Schedule(plan, now);
        Refresh(plan);
    }

    private async Task SendAsync(TimedMessage message)
    {
        var project = Formats.FolderName(message.Folder);
        try
        {
            if (!Directory.Exists(message.Folder) || _shell is not { } shell)
            {
                NotSent(message, $"{project}'s folder isn't there: {message.Folder}", null);
                return;
            }
            var tab = await shell.OpenTimedMessageTabAsync(message);
            if (tab is null)
            {
                NotSent(message, $"Its tab couldn't be opened in {project}.", null);
                return;
            }
            _openedTabs.Add(tab.Id);
            shell.OnTabWorkChanged();
            if (await tab.SendTimedAsync(message.Text, message.Name) is { } problem)
            {
                _openedTabs.Remove(tab.Id);
                NotSent(message, problem, tab.Id);
            }
            else if (tab.IsAskingTrust)
            {
                _services.Notifications.Notify(NotificationKind.TimedMessage, $"Timed message waiting: {message.Name}",
                    $"Waiting for you to trust {project}.", tab.Id);
            }
            else
            {
                _services.Notifications.Notify(NotificationKind.TimedMessage, $"Timed message sent: {message.Name} in {project}",
                    Shorten(message.Text), tab.Id);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Couldn't send the timed message {Name}.", message.Name);
            NotSent(message, ex.Message, null);
        }
        finally
        {
            _shell?.OnTabWorkChanged();
        }
    }

    private void NotSent(TimedMessage message, string why, string? tabId) =>
        _services.Notifications.Notify(NotificationKind.TimedMessage, $"Timed message not sent: {message.Name}", why, tabId, key: message.Id);

    private static string Shorten(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length > 120 ? line[..117] + "…" : line;
    }

    /// <summary>The wake helper was installed or removed: the next wake is set again, through it or not.</summary>
    internal void SetWakeAgain()
    {
        _wakeSet = false;
        Check();
    }

    /// <summary>The timer to the next time (or to holding sleep off before it), the wake, and keeping the computer awake.</summary>
    private void Schedule(TimedMessagePlan plan, DateTimeOffset now)
    {
        _next?.Dispose();
        _next = null;
        if (plan.Next is { At: var at } && _shell is not null)
        {
            var hold = at - TimedMessagePlanner.HoldBefore;
            var due = (hold > now ? hold : at) - now;
            _next = Time.CreateTimer(_ => _services.Dispatcher.Post(Check), null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        var wake = plan.WakeAt(now);
        if (!_wakeSet || wake != _wake)
        {
            _wakeSet = true;
            _wake = wake;
            _ = Wake.WakeAtAsync(wake);
        }
        _dueSoon = plan.IsDueSoon(now);
        _nextAt = plan.Next?.At;
        _services.KeepAwake.SetTimedMessages(_dueSoon, _working);
        _services.WhenDone.SetTimedMessages(_nextAt, _dueSoon, _working);
    }

    /// <summary>
    /// The tabs' work changed: a timed message's tab holds sleep off until its first turn ends, or it's closed, stops with
    /// an error, or waits for the folder to be trusted, which nobody may be there to do.
    /// </summary>
    public void OnTabWork(IEnumerable<TabViewModel> tabs)
    {
        var byId = tabs.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var working = 0;
        foreach (var id in _openedTabs.ToArray())
        {
            if (!byId.TryGetValue(id, out var tab) || tab.Status is TabStatus.Error or TabStatus.Exited || tab.IsAskingTrust)
            {
                Done(id);
            }
            else if (tab.Status == TabStatus.Working)
            {
                _seenWorking.Add(id);
                working++;
            }
            else if (_seenWorking.Contains(id))
            {
                // Its first turn is over.
                Done(id);
            }
            else
            {
                // Starting, or about to send.
                working++;
            }
        }
        _working = working;
        _services.KeepAwake.SetTimedMessages(_dueSoon, working);
        _services.WhenDone.SetTimedMessages(_nextAt, _dueSoon, working);

        void Done(string id)
        {
            _openedTabs.Remove(id);
            _seenWorking.Remove(id);
        }
    }

    /// <summary>The cup's clock, menu and tip, and Settings' rows.</summary>
    private void Refresh(TimedMessagePlan? plan)
    {
        var now = Time.GetUtcNow();
        var culture = CultureInfo.CurrentCulture;
        var handled = _services.State.TimedMessagesHandled;
        Rows = [.. Messages.Select(m => new TimedMessageRow(
            m,
            Formats.FolderName(m.Folder),
            m.Schedule.Describe(culture),
            TimedMessagePlanner.NextFor(m, handled, now, _runningSince, Time.LocalTimeZone) is { } next ? Formats.Upcoming(next, Time) : null,
            _services.State.TimedMessagesSkipped.TryGetValue(m.Id, out var skipped)
                ? $"Skipped the one {Formats.When(skipped, Time)}: the computer was asleep."
                : null))];
        HasAny = Messages.Any(m => m.IsOn);
        NextLine = plan?.Next is { } coming
            ? $"Next timed message: {coming.Message.Name} in {Formats.FolderName(coming.Message.Folder)}, {Formats.Upcoming(coming.At, Time)}."
            : null;
        _services.KeepAwake.SetNextTimedMessage(NextLine);
    }

    /// <summary>For Settings → Advanced → Diagnostics.</summary>
    public string Describe()
    {
        var on = Messages.Count(m => m.IsOn);
        var messages = Messages.Count switch
        {
            0 => "No timed messages.",
            1 => $"1 timed message, {(on == 1 ? "on" : "off")}.",
            var n => $"{n} timed messages, {on} on.",
        };
        var next = NextLine is { } line ? $" {line}" : "";
        return $"{messages}{next} Wake: {Wake.Describe()}";
    }

    public void Dispose()
    {
        _disposed = true;
        _tick?.Dispose();
        _next?.Dispose();
        // Deventry is closing: a message isn't sent while it's closed, so nothing should wake the computer for one.
        Wake.Dispose();
    }
}
