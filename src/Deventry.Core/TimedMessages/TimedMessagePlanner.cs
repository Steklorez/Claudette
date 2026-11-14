namespace Deventry.Core.TimedMessages;

/// <summary>A timed message to send now, for its time <see cref="At"/>.</summary>
public sealed record TimedMessageDue(TimedMessage Message, DateTimeOffset At);

/// <summary>A timed message whose time <see cref="At"/> came and went too long ago to send it, as the computer slept.</summary>
public sealed record TimedMessageSkip(TimedMessage Message, DateTimeOffset At);

/// <summary>What <see cref="TimedMessagePlanner.Plan"/> found.</summary>
/// <param name="Due">The messages to send now.</param>
/// <param name="Skipped">The messages whose time passed too long ago, noticed now.</param>
/// <param name="Next">The next time any message is sent after now, and which; null when none is.</param>
public sealed record TimedMessagePlan(IReadOnlyList<TimedMessageDue> Due, IReadOnlyList<TimedMessageSkip> Skipped, TimedMessageDue? Next)
{
    /// <summary>
    /// When to wake the computer: a minute before <see cref="Next"/>, so the network is back by then (DESIGN.md §18,
    /// "Timed messages", "Late"). Null with nothing to send, or when that minute has begun.
    /// </summary>
    public DateTimeOffset? WakeAt(DateTimeOffset now) =>
        Next is { At: var at } && at - TimedMessagePlanner.WakeEarly > now ? at - TimedMessagePlanner.WakeEarly : null;

    /// <summary>Sleep is held off from a little before the next time (DESIGN.md §18, "Timed messages", "Keeping the computer awake for it").</summary>
    public bool IsDueSoon(DateTimeOffset now) => Next is { At: var at } && at - now <= TimedMessagePlanner.HoldBefore;
}

/// <summary>
/// Which timed messages to send, which to skip, and when the next one is (DESIGN.md §18, "Timed messages"). Pure: the
/// app's service calls it on its clock. A message is sent for a time only while Deventry is running then
/// (<c>runningSince</c>), only after it was saved, never twice for the same time (<c>handled</c>), and only up to
/// <see cref="MissedAfter"/> late; of several times that went by while the computer slept, only the latest counts.
/// </summary>
public static class TimedMessagePlanner
{
    /// <summary>How late a message is still sent, when the computer couldn't be woken for it.</summary>
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(30);

    /// <summary>How long before a message's time the computer is woken.</summary>
    public static readonly TimeSpan WakeEarly = TimeSpan.FromMinutes(1);

    /// <summary>How long before a message's time sleep is held off.</summary>
    public static readonly TimeSpan HoldBefore = TimeSpan.FromMinutes(2);

    /// <summary>How many times that went by are looked through for the latest: an every-minute cron over a long sleep.</summary>
    private const int MostPassed = 100_000;

    /// <param name="handled">When each message was last sent or skipped, by its id: the time it was for.</param>
    /// <param name="runningSince">When Deventry started: a time before it isn't sent.</param>
    public static TimedMessagePlan Plan(IEnumerable<TimedMessage> messages, IReadOnlyDictionary<string, DateTimeOffset> handled, DateTimeOffset now,
        DateTimeOffset runningSince, TimeZoneInfo zone)
    {
        var due = new List<TimedMessageDue>();
        var skipped = new List<TimedMessageSkip>();
        TimedMessageDue? next = null;
        foreach (var message in messages)
        {
            if (!message.IsOn || message.Schedule.Problem is not null)
            {
                continue;
            }
            var since = Since(message, handled, runningSince);
            // The latest of its times since then that has come.
            DateTimeOffset? latest = null;
            var passed = 0;
            for (var at = message.Schedule.Next(since, zone); at is { } time && time <= now && passed < MostPassed; at = message.Schedule.Next(time, zone))
            {
                latest = time;
                passed++;
            }
            if (latest is { } last)
            {
                if (now - last <= MissedAfter)
                {
                    due.Add(new TimedMessageDue(message, last));
                }
                else
                {
                    skipped.Add(new TimedMessageSkip(message, last));
                }
            }
            if (message.Schedule.Next(Max(since, now), zone) is { } coming && (next is null || coming < next.At))
            {
                next = new TimedMessageDue(message, coming);
            }
        }
        return new TimedMessagePlan(due, skipped, next);
    }

    /// <summary>The next time <paramref name="message"/> is sent after now, for the cup's menu and Settings; null when it isn't.</summary>
    public static DateTimeOffset? NextFor(TimedMessage message, IReadOnlyDictionary<string, DateTimeOffset> handled, DateTimeOffset now, DateTimeOffset runningSince,
        TimeZoneInfo zone) =>
        message.IsOn ? message.Schedule.Next(Max(Since(message, handled, runningSince), now), zone) : null;

    /// <summary>A message's times count after this: what was handled, Deventry's start, and the message's last save.</summary>
    private static DateTimeOffset Since(TimedMessage message, IReadOnlyDictionary<string, DateTimeOffset> handled, DateTimeOffset runningSince)
    {
        var since = Max(runningSince, message.SavedAt);
        return handled.TryGetValue(message.Id, out var last) ? Max(since, last) : since;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
