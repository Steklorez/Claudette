using System.Collections.ObjectModel;
using Claudette.App.Services;
using Claudette.Core.Credentials;
using Claudette.Core.Perforce;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>A password source in Settings → Perforce.</summary>
public sealed record PerforcePasswordSourceChoice(PerforcePasswordSource Source, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One per-folder server and user override in Settings → Perforce.</summary>
public sealed class PerforceOverrideEditor(PerforceFolderOverride entry, Action changed) : ObservableObject
{
    public PerforceFolderOverride Entry { get; } = entry;

    public string Folder => Entry.Folder;

    public string Server
    {
        get => Entry.Server ?? "";
        set
        {
            Entry.Server = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            changed();
        }
    }

    public string User
    {
        get => Entry.User ?? "";
        set
        {
            Entry.User = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            changed();
        }
    }
}

/// <summary>
/// Settings → Perforce (DESIGN.md §18, "Perforce ticket handling"). Off by default. The stored password goes only to
/// the OS credential store, never to settings.json.
/// </summary>
public sealed partial class PerforcePage : SettingsPage
{
    public PerforcePage(SettingsContext context) : base(context, SettingsCategory.Perforce)
    {
        Section("logins", "Logins",
        [
            Setting("perforce.enabled", "Keep Perforce logins fresh", s => s.Perforce.Enabled, (s, v) => s.Perforce.Enabled = v,
                "In a workspace, log in again before the ticket expires, so p4 commands keep working.",
                "Claudette tells Claude the tab is in a Perforce workspace. Claude never sees the password. Applies to tabs started after the change.",
                ["p4", "ticket", "login"]),
            Setting("perforce.passwordSource", "Password source", s => s.Perforce.PasswordSource, (s, v) => s.Perforce.PasswordSource = v,
                keywords: ["p4passwd", "password"]),
            Setting("perforce.renewBeforeMinutes", "Renew the ticket when less is left than (minutes)", s => s.Perforce.RenewBeforeMinutes, (s, v) => s.Perforce.RenewBeforeMinutes = v,
                keywords: ["ticket", "expire"]),
            Setting("perforce.allHostsTickets", "Request tickets valid on all hosts", s => s.Perforce.AllHostsTickets, (s, v) => s.Perforce.AllHostsTickets = v,
                "Logs in with p4 login -a.", keywords: ["ticket", "hosts"]),
            Setting("perforce.showChangelistOnTabs", "Show changelist on tabs", s => s.Perforce.ShowChangelistOnTabs, (s, v) => s.Perforce.ShowChangelistOnTabs = v,
                "A CL 12345 badge at the right of a tab's second line.",
                "The tab info card always shows the changelist Claude is working in.", ["changelist", "cl", "tab rows"]),
        ]);
        Section("files", "Opening files in Perforce",
        [
            Setting("perforce.checkOutBeforeChanges", "Check out files before they're changed",
                s => s.Perforce.CheckOutBeforeChanges, (s, v) => s.Perforce.CheckOutBeforeChanges = v,
                "As you start editing one in a Files tab, and before Claude's Edit or Write changes one.",
                "A file Perforce hasn't checked out is read-only, so it couldn't be saved or written otherwise. It goes in the changelist the tab is working in, or the default one.",
                ["p4 edit", "check out", "checkout", "read-only"]),
            Setting("perforce.addNewFiles", "Add new files", s => s.Perforce.AddNewFiles, (s, v) => s.Perforce.AddNewFiles = v,
                "Files made with a Files tab's New file, or created by Claude's Write, are marked for add.",
                "Perforce's own ignore rules (P4IGNORE) still apply.", ["p4 add", "mark for add"]),
            Setting("perforce.deleteRemovedFiles", "Mark deleted files for delete", s => s.Perforce.DeleteRemovedFiles, (s, v) => s.Perforce.DeleteRemovedFiles = v,
                "Files and folders deleted from a Files tab's explorer are marked for delete.",
                "Files Claude deletes with a command aren't seen: Claude uses p4 delete for those, as its note asks.", ["p4 delete", "mark for delete"]),
            Setting("perforce.moveRenamedFiles", "Rename with p4 move", s => s.Perforce.MoveRenamedFiles, (s, v) => s.Perforce.MoveRenamedFiles = v,
                "Renaming in a Files tab's explorer moves the file in Perforce too.", keywords: ["p4 move", "rename"]),
            Setting("perforce.showFileState", "Show Perforce state in the Files tab", s => s.Perforce.ShowFileState, (s, v) => s.Perforce.ShowFileState = v,
                "Beside each file's name: edit, add, delete or move, as Perforce says.",
                "Asked of Perforce in the background, a folder at a time as it's shown, and again when files change.", ["p4 fstat", "explorer", "status", "opened"]),
        ], description: "In a Perforce workspace, Claudette opens files in Perforce as they're changed, made, renamed and deleted.");
        Section("password", "Stored password",
        [
            Info("perforce.storedPassword", "Save a Perforce password", keywords: ["forget password", "credential manager", "keychain"]),
        ]);
        Section("folders", "Per-folder server and user",
        [
            Info("perforce.folderOverrides", "Folders", keywords: ["p4port", "p4user", "server", "user", "override"]),
        ], description: "Perforce finds the server and user itself, as Claude's p4 commands do. Set them here for a folder when that isn't right.");
        FillPerforceLogin();
    }

    public override string IconKey => Logos.Perforce;

    public override string Description => "Keeping Perforce logins fresh, so Claude's p4 commands keep working, and opening files in Perforce as they change.";

    private PerforceSettings Perforce => Settings.Perforce;

    public bool PerforceEnabled
    {
        get => Perforce.Enabled;
        set => Set(value, v => Perforce.Enabled = v);
    }

    // ---- Opening files in Perforce (DESIGN.md §18, "Opening files in Perforce") --------------------------------------

    public bool CheckOutBeforeChanges
    {
        get => Perforce.CheckOutBeforeChanges;
        set => Set(value, v => Perforce.CheckOutBeforeChanges = v);
    }

    public bool AddNewFiles
    {
        get => Perforce.AddNewFiles;
        set => Set(value, v => Perforce.AddNewFiles = v);
    }

    public bool DeleteRemovedFiles
    {
        get => Perforce.DeleteRemovedFiles;
        set => Set(value, v => Perforce.DeleteRemovedFiles = v);
    }

    public bool MoveRenamedFiles
    {
        get => Perforce.MoveRenamedFiles;
        set => Set(value, v => Perforce.MoveRenamedFiles = v);
    }

    public bool ShowFileState
    {
        get => Perforce.ShowFileState;
        set => Set(value, v => Perforce.ShowFileState = v);
    }

    public IReadOnlyList<PerforcePasswordSourceChoice> PerforcePasswordSources { get; } =
    [
        new(PerforcePasswordSource.Stored, "Stored by Claudette (recommended)"),
        new(PerforcePasswordSource.PerforceConfig, "Perforce's own configuration (P4PASSWD)"),
        new(PerforcePasswordSource.AskEachTime, "Ask each time"),
    ];

    public PerforcePasswordSourceChoice SelectedPasswordSource
    {
        get => PerforcePasswordSources.FirstOrDefault(c => c.Source == Perforce.PasswordSource) ?? PerforcePasswordSources[0];
        set
        {
            Set(value?.Source ?? PerforcePasswordSource.Stored, v => Perforce.PasswordSource = v);
            OnPropertyChanged(nameof(IsPerforceConfigSource));
            OnPropertyChanged(nameof(IsStoredPasswordSource));
        }
    }

    /// <summary>P4PASSWD in a P4CONFIG file, <c>p4 set</c> or the environment is plain text; Settings says so.</summary>
    public bool IsPerforceConfigSource => Perforce.PasswordSource == PerforcePasswordSource.PerforceConfig;

    public bool IsStoredPasswordSource => Perforce.PasswordSource == PerforcePasswordSource.Stored;

    public decimal? PerforceRenewBeforeMinutes
    {
        get => Perforce.RenewBeforeMinutes;
        set => Set(value, v => Perforce.RenewBeforeMinutes = Math.Clamp((int)(v ?? PerforceSettings.DefaultRenewBeforeMinutes), 1, 24 * 60));
    }

    public bool PerforceAllHostsTickets
    {
        get => Perforce.AllHostsTickets;
        set => Set(value, v => Perforce.AllHostsTickets = v);
    }

    public bool ShowChangelistOnTabs
    {
        get => Perforce.ShowChangelistOnTabs;
        set => Set(value, v => Perforce.ShowChangelistOnTabs = v);
    }

    // ---- Per-folder overrides ---------------------------------------------------------------------------------------

    public ObservableCollection<PerforceOverrideEditor> PerforceOverrides => field ??= new(Perforce.FolderOverrides.Select(o => new PerforceOverrideEditor(o, Save)));

    [RelayCommand]
    private async Task AddPerforceOverrideAsync()
    {
        if (await Services.Platform.PickFolderAsync("Choose a folder to set a Perforce server or user for") is not { } folder)
        {
            return;
        }
        var editors = PerforceOverrides;
        if (Perforce.FolderOverrides.All(o => o.Folder != folder))
        {
            var entry = new PerforceFolderOverride { Folder = folder };
            Perforce.FolderOverrides.Add(entry);
            editors.Add(new PerforceOverrideEditor(entry, Save));
            Save();
        }
    }

    [RelayCommand]
    private void RemovePerforceOverride(PerforceOverrideEditor? editor)
    {
        if (editor is not null)
        {
            Perforce.FolderOverrides.Remove(editor.Entry);
            PerforceOverrides.Remove(editor);
            Save();
        }
    }

    // ---- The stored password ("Stored by Claudette") ---------------------------------------------------------------

    private ICredentialStore Credentials => Services.Perforce.Credentials;

    public string CredentialStoreName => Credentials.Name;

    public bool IsCredentialStoreAvailable => Credentials.IsAvailable;

    public string? CredentialStoreUnavailableText => Credentials.IsAvailable ? null : Credentials.UnavailableReason;

    public string StoredPasswordNote =>
        $"Saved in {Credentials.Name} under the server and user, never in Claudette's settings file, and never synced. Claudette also offers to save it the first time a tab logs in.";

    /// <summary>The server to save or forget a password for; starts as the first workspace a tab found.</summary>
    [ObservableProperty]
    public partial string PerforcePasswordServer { get; set; } = "";

    [ObservableProperty]
    public partial string PerforcePasswordUser { get; set; } = "";

    [ObservableProperty]
    public partial string PerforcePassword { get; set; } = "";

    [ObservableProperty]
    public partial string? PerforcePasswordStatus { get; set; }

    [RelayCommand]
    private async Task SavePerforcePasswordAsync()
    {
        var server = PerforcePasswordServer.Trim();
        var user = PerforcePasswordUser.Trim();
        if (server.Length == 0 || user.Length == 0 || PerforcePassword.Length == 0)
        {
            PerforcePasswordStatus = "Enter the server, the user and the password.";
            return;
        }
        try
        {
            await Credentials.WriteAsync(PerforceService.CredentialKey(server, user), PerforceService.CredentialLabel(server, user), PerforcePassword);
            PerforcePasswordStatus = $"Saved the password for {user} @ {server} in {Credentials.Name}.";
        }
        catch (CredentialStoreException ex)
        {
            PerforcePasswordStatus = ex.Message;
        }
        finally
        {
            PerforcePassword = "";
        }
    }

    [RelayCommand]
    private async Task ForgetPerforcePasswordAsync()
    {
        var server = PerforcePasswordServer.Trim();
        var user = PerforcePasswordUser.Trim();
        if (server.Length == 0 || user.Length == 0)
        {
            PerforcePasswordStatus = "Enter the server and the user.";
            return;
        }
        try
        {
            PerforcePasswordStatus = await Credentials.DeleteAsync(PerforceService.CredentialKey(server, user))
                ? $"Removed the password for {user} @ {server} from {Credentials.Name}."
                : $"No password was saved for {user} @ {server}.";
        }
        catch (CredentialStoreException ex)
        {
            PerforcePasswordStatus = ex.Message;
        }
    }

    private void FillPerforceLogin()
    {
        if (Services.Perforce.KnownLogins.FirstOrDefault() is { } login)
        {
            PerforcePasswordServer = login.Server;
            PerforcePasswordUser = login.User;
        }
    }
}
