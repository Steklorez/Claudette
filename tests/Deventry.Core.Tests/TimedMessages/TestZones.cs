namespace Deventry.Core.Tests.TimedMessages;

/// <summary>
/// Time zones made here rather than read from the system's database, so the tests read the same on every system: one with
/// US Eastern's rules (UTC−5, daylight time from the second Sunday of March at 2:00 to the first Sunday of November at
/// 2:00), and one without daylight time.
/// </summary>
internal static class TestZones
{
    public static TimeZoneInfo Eastern { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test Eastern",
        TimeSpan.FromHours(-5),
        "Test Eastern",
        "Test Eastern Standard",
        "Test Eastern Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
        ]);

    public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    /// <summary>A local time in <see cref="Eastern"/> as an instant, when it's neither skipped nor shown twice.</summary>
    public static DateTimeOffset EasternAt(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Eastern.GetUtcOffset(local));
    }
}
