using System.Collections.ObjectModel;
using System.ComponentModel;
using Deventry.App.Services;
using Deventry.Core.TimedMessages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A timed message's row in Settings → Timed messages: what and when, its switch, <b>Edit…</b> and <b>Delete</b>.</summary>
public sealed partial class TimedMessageRowViewModel : ObservableObject
{
    private readonly TimedMessagesService _service;

    public TimedMessageRowViewModel(TimedMessagesService service, TimedMessageRow row, Action<TimedMessageRowViewModel> edit)
    {
        _service = service;
        Row = row;
        EditCommand = new RelayCommand(() => edit(this));
        Removal = new InlineConfirmation(() =>
        {
            _service.Delete(Row.Message.Id);
            return Task.CompletedTask;
        });
    }

    public TimedMessageRow Row { get; private set; }

    public string Name => Row.Name;

    /// <summary><i>starfall · Weekdays at 7:00 AM</i>.</summary>
    public string Detail => $"{Row.Project} · {Row.When}";

    /// <summary><i>Next: tomorrow at 7:00 AM</i>, <i>Off</i> or <i>Not again</i>.</summary>
    public string NextText => !Row.IsOn ? "Off" : Row.Next is { } next ? $"Next: {next}" : "Not again";

    public string? Skipped => Row.Skipped;

    public bool HasSkipped => Row.HasSkipped;

    /// <summary>Its <b>On</b> switch: kept, but not sent, while off.</summary>
    public bool IsOn
    {
        get => Row.IsOn;
        set => _service.SetOn(Row.Message.Id, value);
    }

    public IRelayCommand EditCommand { get; }

    public InlineConfirmation Removal { get; }

    public string RemoveText => $"Delete {Row.Name}? It won't be sent again.";

    internal void Update(TimedMessageRow row)
    {
        Row = row;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// Settings → Timed messages (DESIGN.md §18, "Timed messages"), under Your work: the messages, adding and changing them
/// in an editor on the page, and waking the computer for them. This computer's: nothing here syncs.
/// </summary>
public sealed partial class TimedMessagesPage : SettingsPage, IDisposable
{
    public TimedMessagesPage(SettingsContext context) : base(context, SettingsCategory.TimedMessages)
    {
        Section("messages", "Timed messages",
        [
            Info("state.timedMessages", "Your timed messages",
                keywords: ["schedule", "scheduled", "timer", "morning", "cron", "recurring", "every day", "weekdays", "clear", "delete"]),
        ], description: "A message Deventry sends at a time you choose, to a new tab in a project, while Deventry is running.");
        Section("wake", "Waking the computer",
        [
            Info("state.wakeComputer", "Waking the computer",
                keywords: ["sleep", "wake timer", "wake helper", "power plan", "pmset", "rtcwake", "administrator"]),
        ]);
        Rows = [.. Service.Rows.Select(NewRow)];
        Service.PropertyChanged += OnServiceChanged;
        Service.EditorRequested += OnEditorRequested;
        if (Service.TakeEditorRequest() is { } asked)
        {
            Open(asked);
        }
    }

    public void Dispose()
    {
        Service.PropertyChanged -= OnServiceChanged;
        Service.EditorRequested -= OnEditorRequested;
    }

    public override string IconKey => "IconClock";

    public override string Description => "Messages Deventry sends at times you choose, to a new tab in a project.";

    private TimedMessagesService Service => Services.TimedMessages;

    private IWakeTimer Wake => Service.Wake;

    public ObservableCollection<TimedMessageRowViewModel> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    public string NoneText => "None yet. Add one, and Deventry sends it at its time to a new tab in its project.";

    /// <summary>The message being added or changed, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial TimedMessageEditor? Editor { get; private set; }

    public bool IsEditing => Editor is not null;

    [RelayCommand]
    private void Add() => Open(TimedMessagesService.NewMessage(null));

    private void Open(TimedMessage message) =>
        Editor = new TimedMessageEditor(Services, message, title => Services.Platform.PickFolderAsync(title), () => Editor = null);

    private TimedMessageRowViewModel NewRow(TimedMessageRow row) =>
        new(Service, row, edit => Open(edit.Row.Message.Clone()));

    private void OnEditorRequested()
    {
        if (Service.TakeEditorRequest() is { } asked)
        {
            Open(asked);
        }
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimedMessagesService.Rows))
        {
            return;
        }
        var rows = Service.Rows;
        // Rows kept as they were where they can be, so a switch being flipped doesn't lose its place.
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (rows.All(r => r.Message.Id != Rows[i].Row.Message.Id))
            {
                Rows.RemoveAt(i);
            }
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var existing = Rows.FirstOrDefault(r => r.Row.Message.Id == rows[i].Message.Id);
            if (existing is null)
            {
                Rows.Insert(Math.Min(i, Rows.Count), NewRow(rows[i]));
            }
            else
            {
                existing.Update(rows[i]);
            }
        }
        OnPropertyChanged(nameof(HasRows));
    }

    // ---- Waking the computer ------------------------------------------------------------------------------------------

    /// <summary>What happens when the computer sleeps through a message's time, here.</summary>
    public string WakeText => Wake.Setup switch
    {
        WakeSetup.NotNeeded => Wake.Caveat is { } caveat
            ? $"While Deventry is running, it wakes the computer for them. {caveat}"
            : "While Deventry is running, it wakes the computer for them.",
        WakeSetup.NeedsHelper =>
            "To wake this computer for them, Deventry needs a small helper, installed once with an administrator's password. Without it, a message whose time passes while the computer sleeps is still sent if it wakes within half an hour.",
        WakeSetup.HelperInstalled => "The wake helper is installed: while Deventry is running, it wakes the computer for them.",
        _ => $"{Wake.UnavailableReason} A message whose time passes while the computer sleeps is still sent if it wakes within half an hour.",
    };

    public bool CanInstallHelper => Wake.Setup == WakeSetup.NeedsHelper;

    public bool CanRemoveHelper => Wake.Setup == WakeSetup.HelperInstalled;

    /// <summary>What installing or removing the helper came to.</summary>
    [ObservableProperty]
    public partial string? WakeStatus { get; private set; }

    [RelayCommand]
    private async Task InstallHelperAsync()
    {
        WakeStatus = "Asking for an administrator's password…";
        WakeStatus = await Wake.InstallAsync() ? null : "The wake helper wasn't installed.";
        OnWakeChanged();
    }

    [RelayCommand]
    private async Task RemoveHelperAsync()
    {
        WakeStatus = "Asking for an administrator's password…";
        WakeStatus = await Wake.UninstallAsync() ? null : "The wake helper wasn't removed.";
        OnWakeChanged();
    }

    private void OnWakeChanged()
    {
        OnPropertyChanged(nameof(WakeText));
        OnPropertyChanged(nameof(CanInstallHelper));
        OnPropertyChanged(nameof(CanRemoveHelper));
        // The wake goes through the helper from now on, or not.
        Service.SetWakeAgain();
    }
}
