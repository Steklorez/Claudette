using Claudette.Core;
using Claudette.Core.Library;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// Settings → Sessions (DESIGN.md §14): the session library (DESIGN.md §9), this machine's name in it, and syncing
/// Claudette's settings through it.
/// </summary>
public sealed partial class SessionsPage : SettingsPage
{
    private bool _moveLibrary;

    /// <summary>What each synced section of settings.json is called, for what settings sync says it syncs.</summary>
    private static readonly IReadOnlyDictionary<string, string> SyncedNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["appearance"] = "appearance (but the zoom)",
        ["newTabs"] = "what new tabs start with",
        ["usage"] = "usage warnings and limits",
        ["checkIns"] = "check-ins",
        ["quickSuffixes"] = "quick suffixes",
        ["processes"] = "the process monitor",
        ["notifications"] = "notifications",
        ["keyboard"] = "keyboard shortcuts",
    };

    public SessionsPage(SettingsContext context) : base(context, SettingsCategory.Sessions)
    {
        Section("library", "Session library",
        [
            // Where sessions live is its own decision, with its own buttons: never reset.
            Setting("sessions.libraryFolder", "Folder", null, null, keywords: ["session library folder", "move library", "dropbox", "onedrive", "google drive"]),
            Setting("sessions.syncNewTabs", "Sync new tabs to the session library", s => s.Sessions.SyncNewTabs, (s, v) => s.Sessions.SyncNewTabs = v,
                "Each tab can be switched with Sync to other machines in its menu.",
                "Sessions opened from the library keep syncing.", ["sync", "other machines"]),
            Setting("sessions.machineName", "Name for this machine", s => s.Sessions.MachineName, (s, v) => s.Sessions.MachineName = v,
                "Shown in History and when a session is in use elsewhere. Empty uses the computer's name.", keywords: ["computer name", "hostname"]),
            Setting("sessions.keepLibrarySessions", "Keep library sessions for", s => s.Sessions.KeepLibrarySessions, (s, v) => s.Sessions.KeepLibrarySessions = v,
                keywords: ["retention", "delete old sessions"]),
        ], description: "Tabs that sync copy their session here after every turn. In a folder Google Drive, Dropbox or OneDrive keeps in sync, your other machines can continue them from History.");
        Section("settingsSync", "Settings sync",
        [
            // Turning it on can ask whose settings win: its own decision, never a reset.
            Setting("sessions.syncSettings", "Sync Claudette's settings through the library", null, null,
                keywords: ["settings sync", "other machines", "roaming"]),
        ]);
    }

    public override string IconKey => "IconSettingsSessions";

    public override string Description => "The session library, which lets your other machines continue your sessions.";

    /// <summary>What settings sync copies, from what it syncs, and that what's marked "This computer" stays here.</summary>
    public static string SyncedText =>
        $"Syncs {Core.Formats.List([.. SyncedSettings.Sections.Select(s => SyncedNames.TryGetValue(s, out var name) ? name : s)])}. "
        + "Everything marked This computer stays on each machine.";

    /// <summary>Every synced section has a name to say it by: a test checks.</summary>
    internal static bool NamesEverySyncedSection => SyncedSettings.Sections.All(SyncedNames.ContainsKey);

    public IReadOnlyList<RetentionChoice> LibraryRetentionChoices { get; } =
        [.. new[] { RetentionPeriod.OneMonth, RetentionPeriod.OneYear, RetentionPeriod.Forever }.Select(p => new RetentionChoice(p))];

    public RetentionChoice KeepLibrarySessions
    {
        get => LibraryRetentionChoices.FirstOrDefault(c => c.Period == Settings.Sessions.KeepLibrarySessions) ?? LibraryRetentionChoices[^1];
        set => Set(value?.Period ?? RetentionPeriod.Forever, v => Settings.Sessions.KeepLibrarySessions = v);
    }

    /// <summary>Shown in History and in "in use on …" notices. Empty uses the computer name.</summary>
    public string MachineName
    {
        get => Settings.Sessions.MachineName ?? "";
        set => Set(value, v => Settings.Sessions.MachineName = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    public string MachineNamePlaceholder => Environment.MachineName;

    /// <summary>New tabs start syncing to the session library (DESIGN.md §9). Each tab can change it from its menu.</summary>
    public bool SyncNewTabs
    {
        get => Settings.Sessions.SyncNewTabs;
        set => Set(value, v => Settings.Sessions.SyncNewTabs = v);
    }

    // ---- Session library (DESIGN.md §9) ----------------------------------------------------------------------------

    public string LibraryFolder => Services.Library.LibraryFolder;

    public bool IsDefaultLibraryFolder => Settings.Sessions.LibraryFolder is null;

    /// <summary>A folder the user picked, waiting for them to confirm it's fine to sync transcripts there.</summary>
    public InlineConfirmation<string> LibraryFolderConfirmation => field ??= new(folder => ApplyLibraryFolderAsync(folder));

    [ObservableProperty]
    public partial string LibraryConfirmText { get; set; } = "";

    [ObservableProperty]
    public partial string? LibraryStatus { get; set; }

    /// <summary>
    /// What the library takes, under its folder (DESIGN.md §14, "Sessions"): "The library holds 1.4 GB: 23 sessions."
    /// Null until it's been measured.
    /// </summary>
    [ObservableProperty]
    public partial string? LibrarySizeText { get; set; }

    /// <summary>The latest measurement of the library, for tests to wait on.</summary>
    internal Task Measuring { get; private set; } = Task.CompletedTask;

    /// <summary>Measured each time the page shows, since sessions come and go with every tab that syncs.</summary>
    public override void OnShown() => MeasureLibrary();

    private void MeasureLibrary() => Measuring = MeasureLibraryAsync();

    /// <summary>Walks the library's folder, which may be on a slow or offline drive: not on the UI thread.</summary>
    private async Task MeasureLibraryAsync()
    {
        var folder = Services.Library.LibraryFolder;
        var time = Services.Time;
        var size = await Task.Run(() => new SessionLibrary(folder, time).Measure());
        // The folder changed meanwhile: its own measurement follows.
        if (folder != Services.Library.LibraryFolder)
        {
            return;
        }
        LibrarySizeText = size switch
        {
            { Bytes: 0, Sessions: 0 } => "The library is empty.",
            { Sessions: 0 } => $"The library holds {Formats.Size(size.Bytes)}: no sessions.",
            _ => $"The library holds {Formats.Size(size.Bytes)}: {size.Sessions} session{(size.Sessions == 1 ? "" : "s")}.",
        };
    }

    /// <summary>Browse…: use another folder from now on.</summary>
    [RelayCommand]
    private Task BrowseLibraryFolderAsync() => ChooseLibraryFolderAsync(move: false);

    /// <summary>Move library…: copy the existing sessions to the new folder, then use it.</summary>
    [RelayCommand]
    private Task MoveLibraryAsync() => ChooseLibraryFolderAsync(move: true);

    private async Task ChooseLibraryFolderAsync(bool move)
    {
        if (await Services.Platform.PickFolderAsync(move ? "Move the session library to" : "Choose a folder for the session library") is not { } folder)
        {
            return;
        }
        _moveLibrary = move;
        // Transcripts hold code and command output; say so before they go to a synced folder (DESIGN.md §9, "Privacy").
        if (SessionLibrary.IsInCloudSyncFolder(folder, out var provider))
        {
            LibraryConfirmText = $"This folder is synced by {provider}. Session transcripts contain your code, command output and anything else Claude read in your projects, and they'll be uploaded there. Use it anyway?";
            LibraryFolderConfirmation.Ask(folder);
            return;
        }
        await ApplyLibraryFolderAsync(folder);
    }

    [RelayCommand]
    private Task UseDefaultLibraryFolderAsync() => ApplyLibraryFolderAsync(null);

    private async Task ApplyLibraryFolderAsync(string? folder)
    {
        var old = Services.Library.LibraryFolder;
        if (_moveLibrary && folder is not null)
        {
            LibraryStatus = "Copying sessions…";
            try
            {
                await SessionLibrary.CopyLibraryAsync(old, folder);
            }
            catch (Exception ex)
            {
                LibraryStatus = $"Couldn't copy the library: {ex.Message}";
                return;
            }
        }
        Settings.Sessions.LibraryFolder = folder;
        Save();
        LibraryStatus = _moveLibrary && folder is not null ? $"Copied the sessions to {folder}. The old folder was left as it was." : null;
        _moveLibrary = false;
        OnPropertyChanged(nameof(LibraryFolder));
        OnPropertyChanged(nameof(IsDefaultLibraryFolder));
        LibrarySizeText = null;
        MeasureLibrary();
    }

    // ---- Settings sync (DESIGN.md §14) -------------------------------------------------------------------------------

    public bool SyncSettings
    {
        get => Settings.Sessions.SyncSettings;
        set
        {
            if (value == Settings.Sessions.SyncSettings)
            {
                return;
            }
            if (value && Services.Library.HasSyncedSettings())
            {
                // The library already has settings from another machine: ask which ones win.
                IsChoosingSyncSource = true;
                OnPropertyChanged();
                return;
            }
            Settings.Sessions.SyncSettings = value;
            Save();
            OnPropertyChanged();
            if (value)
            {
                _ = Services.Library.PublishAllSettingsAsync();
            }
        }
    }

    [ObservableProperty]
    public partial bool IsChoosingSyncSource { get; set; }

    /// <summary>The library's settings win: every page shows them.</summary>
    [RelayCommand]
    private async Task UseSyncedSettingsAsync()
    {
        IsChoosingSyncSource = false;
        Settings.Sessions.SyncSettings = true;
        Services.State.SettingsSync = null;
        Save();
        await Services.Library.SyncSettingsAsync();
        Context.RefreshAllPages();
    }

    [RelayCommand]
    private async Task ReplaceSyncedSettingsAsync()
    {
        IsChoosingSyncSource = false;
        Settings.Sessions.SyncSettings = true;
        Save();
        await Services.Library.PublishAllSettingsAsync();
        OnPropertyChanged(nameof(SyncSettings));
    }

    [RelayCommand]
    private void CancelSyncChoice()
    {
        IsChoosingSyncSource = false;
        OnPropertyChanged(nameof(SyncSettings));
    }

}
