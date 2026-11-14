using System.Globalization;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Settings;
using Deventry.Core.TimedMessages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A day of the week's toggle in the editor: <i>Mon</i>.</summary>
public sealed partial class DayToggle(DayOfWeek day, string label, bool isOn, Action changed) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsOn { get; set; } = isOn;

    partial void OnIsOnChanged(bool value) => changed();
}

/// <summary>
/// The editor of a timed message in Settings → Timed messages (DESIGN.md §18, "Timed messages"): its name, project,
/// message, where it opens, its mode, model and effort, and when, with the next times it will be sent. Saves through
/// <see cref="TimedMessagesService.Save"/>.
/// </summary>
public sealed partial class TimedMessageEditor : ObservableObject
{
    private readonly TimedMessagesService _service;
    private readonly TimedMessage _message;
    private readonly Func<string, Task<string?>> _pickFolder;
    private readonly Action _closed;
    private readonly TimedSchedule _schedule;
    private readonly TimeProvider _time;

    /// <param name="message">A copy to change, or a new one.</param>
    /// <param name="pickFolder">Asks the user for a folder, given the dialog's title: <b>Browse…</b>.</param>
    /// <param name="closed">Saved or cancelled: the page closes the editor.</param>
    public TimedMessageEditor(AppServices services, TimedMessage message, Func<string, Task<string?>> pickFolder, Action closed)
    {
        _service = services.TimedMessages;
        _time = services.Time;
        _message = message;
        _pickFolder = pickFolder;
        _closed = closed;
        _schedule = message.Schedule.Clone();
        IsNew = _service.Find(message.Id) is null;
        Name = message.Name;
        Folder = message.Folder;
        Text = message.Text;
        InWorktree = message.InWorktree;
        FolderChoices = Folders(services, message.Folder);

        var models = services.State.KnownModels;
        ModelChoices = [new Choice(null, "Project default"), .. models.Select(m => new Choice(m.Value, m.DisplayName))];
        if (message.Model is { } model && ModelChoices.All(c => c.Value != model))
        {
            ModelChoices = [.. ModelChoices, new Choice(model, model)];
        }
        var levels = models.SelectMany(m => m.SupportedEffortLevels).Distinct(StringComparer.Ordinal).ToList();
        if (levels.Count == 0)
        {
            levels = ["low", "medium", "high", "xhigh", "max"];
        }
        if (message.Effort is { } effort && !levels.Contains(effort))
        {
            levels.Add(effort);
        }
        EffortChoices = [new Choice(null, "Project default"), .. levels.Select(e => new Choice(e, Formats.Capitalize(e)))];
        ModeChoices = [new Choice(null, "Project default"), .. PermissionModeInfo.Choices.Select(m => new Choice(m.Value, m.Label))];
        SelectedModel = ModelChoices.FirstOrDefault(c => c.Value == message.Model) ?? ModelChoices[0];
        SelectedEffort = EffortChoices.FirstOrDefault(c => c.Value == message.Effort) ?? EffortChoices[0];
        SelectedMode = ModeChoices.FirstOrDefault(c => c.Value == message.PermissionMode) ?? ModeChoices[0];

        var culture = CultureInfo.CurrentCulture;
        Days = [.. TimedSchedule.EveryDay.Select(d => new DayToggle(d, culture.DateTimeFormat.GetAbbreviatedDayName(d), _schedule.Days.Contains(d), DaysChanged))];
        // A new message once starts tomorrow, at its time.
        _schedule.Date ??= DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(services.Time.GetUtcNow(), services.Time.LocalTimeZone).Date).AddDays(1);
        Refresh();
    }

    /// <summary>A message not saved yet: the editor says <b>Add</b> rather than <b>Save</b>.</summary>
    public bool IsNew { get; }

    public string Title => IsNew ? "New timed message" : $"Edit {_message.Name}";

    public string SaveText => IsNew ? "Add" : "Save";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string Name { get; set; } = "";

    /// <summary>The project's folder: picked from the open groups and the recent folders, or <b>Browse…</b>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string Folder { get; set; } = "";

    public IReadOnlyList<string> FolderChoices { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string Text { get; set; } = "";

    /// <summary><b>Opens in</b>: a new tab in a worktree of its own, rather than in the folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InFolder))]
    public partial bool InWorktree { get; set; }

    public bool InFolder
    {
        get => !InWorktree;
        set => InWorktree = !value;
    }

    public IReadOnlyList<Choice> ModelChoices { get; }

    public IReadOnlyList<Choice> EffortChoices { get; }

    public IReadOnlyList<Choice> ModeChoices { get; }

    [ObservableProperty]
    public partial Choice SelectedModel { get; set; }

    [ObservableProperty]
    public partial Choice SelectedEffort { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBypass))]
    public partial Choice SelectedMode { get; set; }

    /// <summary>Bypass permissions: the editor warns as Tab settings does.</summary>
    public bool IsBypass => SelectedMode.Value == PermissionModeInfo.Bypass;

    // ---- When ---------------------------------------------------------------------------------------------------------

    public bool IsOnce
    {
        get => _schedule.Kind == TimedScheduleKind.Once;
        set => SetKind(value, TimedScheduleKind.Once);
    }

    public bool IsDays
    {
        get => _schedule.Kind == TimedScheduleKind.Days;
        set => SetKind(value, TimedScheduleKind.Days);
    }

    public bool IsCron
    {
        get => _schedule.Kind == TimedScheduleKind.Cron;
        set => SetKind(value, TimedScheduleKind.Cron);
    }

    /// <summary>From and Until are for the times that repeat.</summary>
    public bool HasDates => !IsOnce;

    private void SetKind(bool on, TimedScheduleKind kind)
    {
        if (on && _schedule.Kind != kind)
        {
            _schedule.Kind = kind;
            Refresh();
        }
    }

    /// <summary>The time of day, for the time picker.</summary>
    public TimeSpan? Time
    {
        get => _schedule.Time.ToTimeSpan();
        set
        {
            if (value is { } time)
            {
                _schedule.Time = TimeOnly.FromTimeSpan(time);
                Refresh();
            }
        }
    }

    /// <summary>Once's day, for the date picker.</summary>
    public DateTime? Date
    {
        get => _schedule.Date?.ToDateTime(TimeOnly.MinValue);
        set
        {
            _schedule.Date = value is { } date ? DateOnly.FromDateTime(date) : null;
            Refresh();
        }
    }

    public IReadOnlyList<DayToggle> Days { get; }

    private void DaysChanged()
    {
        _schedule.Days = [.. Days.Where(d => d.IsOn).Select(d => d.Day)];
        Refresh();
    }

    [RelayCommand]
    private void EveryDay() => SetDays(TimedSchedule.EveryDay);

    [RelayCommand]
    private void Weekdays() => SetDays(TimedSchedule.Weekdays);

    private void SetDays(IReadOnlyList<DayOfWeek> days)
    {
        foreach (var toggle in Days)
        {
            toggle.IsOn = days.Contains(toggle.Day);
        }
    }

    public string Cron
    {
        get => _schedule.Cron ?? "";
        set
        {
            _schedule.Cron = value;
            Refresh();
        }
    }

    public DateTime? From
    {
        get => _schedule.From?.ToDateTime(TimeOnly.MinValue);
        set
        {
            _schedule.From = value is { } date ? DateOnly.FromDateTime(date) : null;
            Refresh();
        }
    }

    public DateTime? Until
    {
        get => _schedule.Until?.ToDateTime(TimeOnly.MinValue);
        set
        {
            _schedule.Until = value is { } date ? DateOnly.FromDateTime(date) : null;
            Refresh();
        }
    }

    // ---- What it comes to ---------------------------------------------------------------------------------------------

    /// <summary><i>Next: tomorrow at 7:00 AM, Mon at 7:00 AM and Tue at 7:00 AM.</i>, or why there's no next.</summary>
    [ObservableProperty]
    public partial string Preview { get; private set; } = "";

    /// <summary>What has to change before it can be saved; null when it can be.</summary>
    public string? Problem =>
        Name.Trim().Length == 0 ? "Give it a name."
        : Folder.Trim().Length == 0 ? "Choose the project's folder."
        : !Directory.Exists(Folder) ? "That folder isn't there."
        : Text.Trim().Length == 0 ? "Write its message."
        : _schedule.Problem
            ?? (_service.NextTimes(_schedule, 1).Count == 0 ? (IsOnce ? "That time has gone by." : "It's never sent between those dates.") : null);

    public bool CanSave => Problem is null;

    private void Refresh()
    {
        var times = _service.NextTimes(_schedule, 3);
        Preview = _schedule.Problem is { } problem ? problem
            : times.Count == 0 ? (IsOnce ? "That time has gone by." : "It's never sent between those dates.")
            : $"Next: {Formats.List([.. times.Select(t => Formats.Upcoming(t, _time))])}.";
        OnPropertyChanged(nameof(Time));
        OnPropertyChanged(nameof(Date));
        OnPropertyChanged(nameof(Cron));
        OnPropertyChanged(nameof(From));
        OnPropertyChanged(nameof(Until));
        OnPropertyChanged(nameof(IsOnce));
        OnPropertyChanged(nameof(IsDays));
        OnPropertyChanged(nameof(IsCron));
        OnPropertyChanged(nameof(HasDates));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (await _pickFolder("Choose the project's folder") is { } folder)
        {
            if (!FolderChoices.Contains(folder, StringComparer.Ordinal))
            {
                FolderChoices = [folder, .. FolderChoices];
                OnPropertyChanged(nameof(FolderChoices));
            }
            Folder = folder;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }
        var message = _message.Clone();
        message.Name = Name.Trim();
        message.Folder = FolderHistory.Normalize(Folder.Trim());
        message.Text = Text.Trim();
        message.InWorktree = InWorktree;
        message.Model = SelectedModel.Value;
        message.Effort = SelectedEffort.Value;
        message.PermissionMode = SelectedMode.Value;
        message.Schedule = _schedule.Clone();
        message.IsOn = true;
        _service.Save(message);
        _closed();
    }

    [RelayCommand]
    private void Cancel() => _closed();

    /// <summary>The folders to pick from: the message's own, the open tabs', then the recent and favorite folders.</summary>
    private static IReadOnlyList<string> Folders(AppServices services, string own)
    {
        var state = services.State;
        IEnumerable<string> all = [own, .. state.Tabs.Select(t => t.WorktreeOf ?? t.Folder), .. state.FavoriteFolders, .. state.RecentFolders.Select(r => r.Path)];
        return [.. all.Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
