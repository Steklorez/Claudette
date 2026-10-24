using Claudette.App.Services;
using Claudette.Core.Settings;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// What the Settings window's pages share (DESIGN.md §14): the services, the settings they change, saving them, the
/// shortcut being recorded, which Keyboard and its quick suffixes both set, and which tabs have their own settings.
/// Dispose it with the window.
/// </summary>
public sealed class SettingsContext : IDisposable
{
    private bool _saving;

    /// <param name="tabsWithOwn">The names of the open tabs that override a setting, by its override key; null without tabs.</param>
    public SettingsContext(AppServices services, Func<string, IReadOnlyList<string>>? tabsWithOwn = null)
    {
        Services = services;
        TabsWithOwn = tabsWithOwn;
        Shortcuts = new ShortcutRecorder(services.Settings, Save);
        services.SettingsChanged += OnSettingsChanged;
    }

    public AppServices Services { get; }

    public AppSettings Settings => Services.Settings;

    public ShortcutRecorder Shortcuts { get; }

    /// <summary>The open tabs that override a setting (DESIGN.md §14, "Per-tab overrides"), for "2 tabs have their own".</summary>
    public Func<string, IReadOnlyList<string>>? TabsWithOwn { get; }

    /// <summary>Saves the settings, which applies them: changes apply immediately, with no Save button.</summary>
    public void Save()
    {
        _saving = true;
        try
        {
            Services.SaveSettings();
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>Every page shows its settings again, after settings sync took another machine's (DESIGN.md §14).</summary>
    public void RefreshAllPages() => AllPagesChanged?.Invoke();

    /// <summary>Raised by <see cref="RefreshAllPages"/>; each page refreshes everything it shows.</summary>
    public event Action? AllPagesChanged;

    /// <summary>The settings were saved: each page's settings read whether they're at their defaults again.</summary>
    public event Action? SettingsSaved;

    /// <summary>
    /// Settings changed. One saved from elsewhere while the window is open, such as the header's cup or the zoom keys,
    /// shows on every page.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_saving)
        {
            SettingsSaved?.Invoke();
        }
        else
        {
            RefreshAllPages();
        }
    }

    public void Dispose() => Services.SettingsChanged -= OnSettingsChanged;
}
