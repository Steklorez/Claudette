namespace Deventry.Core.TimedMessages;

/// <summary>
/// A message the user wrote ahead, which Deventry sends at the times its <see cref="Schedule"/> gives to a new tab in
/// <see cref="Folder"/> (DESIGN.md §18, "Timed messages"). Kept in Settings → Timed messages, on this computer only.
/// </summary>
public sealed class TimedMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>"Morning triage": what the cup's menu, the notifications and the message's card call it.</summary>
    public string Name { get; set; } = "";

    /// <summary>The project's folder the tab opens in.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The message, as it's sent.</summary>
    public string Text { get; set; } = "";

    /// <summary>A new tab in a worktree of its own, named after the message, rather than in the folder itself.</summary>
    public bool InWorktree { get; set; }

    /// <summary>The tab's permission mode, or null for what a new tab in the folder starts in.</summary>
    public string? PermissionMode { get; set; }

    /// <summary>The tab's model, or null for the project's.</summary>
    public string? Model { get; set; }

    /// <summary>The tab's effort, or null for the project's.</summary>
    public string? Effort { get; set; }

    public TimedSchedule Schedule { get; set; } = new();

    /// <summary>Kept but not sent while off.</summary>
    public bool IsOn { get; set; } = true;

    /// <summary>
    /// When it was last saved. A time before it isn't one it missed: a message made at 7:05 for 7:00 is first sent the
    /// next day.
    /// </summary>
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>A worktree's name from the message's: <c>morning-triage</c> for <i>Morning triage</i>.</summary>
    public string WorktreeName
    {
        get
        {
            var name = new string([.. Name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
            while (name.Contains("--", StringComparison.Ordinal))
            {
                name = name.Replace("--", "-", StringComparison.Ordinal);
            }
            name = name.Trim('-');
            return name.Length > 0 ? name : "timed-message";
        }
    }

    public TimedMessage Clone() => new()
    {
        Id = Id,
        Name = Name,
        Folder = Folder,
        Text = Text,
        InWorktree = InWorktree,
        PermissionMode = PermissionMode,
        Model = Model,
        Effort = Effort,
        Schedule = Schedule.Clone(),
        IsOn = IsOn,
        SavedAt = SavedAt,
    };
}
