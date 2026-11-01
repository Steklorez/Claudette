using System.Globalization;
using Deventry.Core.Settings;

namespace Deventry.Core.Tests.Settings;

/// <summary>The account a session began under, as the machine's state keeps it (DESIGN.md §4, "Continuing under another account").</summary>
public sealed class SessionOriginTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-11-01T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Only_the_newest_are_kept()
    {
        var origins = new Dictionary<string, SessionOrigin>();
        // Added out of order: what's kept goes by when each session began, not by when it was recorded.
        foreach (var minutes in new[] { 3, 0, 4, 1, 2 })
        {
            origins[$"s{minutes}"] = new SessionOrigin { Email = "me@example.com" }.For(Noon.AddMinutes(minutes));
        }

        SessionOrigin.KeepNewest(origins, most: 3);

        Assert.Equal(["s2", "s3", "s4"], origins.Keys.Order(StringComparer.Ordinal));
        SessionOrigin.KeepNewest(origins, most: 3);
        Assert.Equal(3, origins.Count);
    }

    [Fact]
    public void A_copy_for_another_session_is_the_same_account()
    {
        var origin = new SessionOrigin { AccountId = "work", Name = "Work", StartedAt = Noon };

        var copy = origin.For(Noon.AddHours(1));

        Assert.NotSame(origin, copy);
        Assert.True(copy.IsSameAs(origin));
        Assert.Equal((origin.Key, Noon.AddHours(1)), (copy.Key, copy.StartedAt));
    }
}
