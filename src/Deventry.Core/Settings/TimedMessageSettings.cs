using Deventry.Core.TimedMessages;

namespace Deventry.Core.Settings;

/// <summary>
/// Settings → Timed messages (DESIGN.md §18, "Timed messages"). This computer's: they don't sync, since two machines
/// sending the same message would start two tabs on the same work.
/// </summary>
public sealed class TimedMessageSettings
{
    public List<TimedMessage> Messages { get; set; } = [];
}
