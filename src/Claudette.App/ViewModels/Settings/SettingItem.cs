using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// What a page of settings answers to: the rows and sections of its view find their <see cref="SettingItem"/> and
/// <see cref="SettingsGroup"/> here by key (DESIGN.md §14), and resetting goes through it so the settings are saved once.
/// A Settings page is one, and so is a tab's Tab settings….
/// </summary>
public interface ISettingsSource
{
    SettingItem? FindSetting(string key);

    SettingsGroup? FindGroup(string key);

    /// <summary>Puts <paramref name="items"/> back to their defaults, then <paramref name="also"/>, saves once and shows the result.</summary>
    void Reset(IReadOnlyList<SettingItem> items, Action? also = null);
}

/// <summary>
/// One setting on a Settings page (DESIGN.md §14): the words its row shows and search finds it by, whether it's this
/// computer's alone, whether it differs from its default, and putting it back. The value itself is the page's own
/// property, which the row's control binds to.
/// </summary>
public sealed partial class SettingItem : ObservableObject
{
    private readonly Func<bool>? _isDefault;
    private ISettingsSource? _source;

    /// <param name="key">
    /// Where it's kept, as settings sync names it: <c>appearance.theme</c> in <c>settings.json</c>, or <c>state.…</c> for
    /// what's kept with this machine's state. Rows, links and search find it by this.
    /// </param>
    /// <param name="isDefault">Whether it's at its default now; null for a setting that has none to go back to.</param>
    /// <param name="reset">Puts it back to its default, without saving; null for one that's never reset.</param>
    public SettingItem(string key, string title, Func<bool>? isDefault = null, Action? reset = null)
    {
        Key = key;
        Title = title;
        _isDefault = isDefault;
        ResetValue = reset;
        IsMachineOnly = !SyncedSettings.Syncs(key);
    }

    public string Key { get; }

    public string Title { get; }

    /// <summary>One line under the title: what it does.</summary>
    public string? Description { get; init; }

    /// <summary>The rest, behind <b>More</b>: the details that only matter once you wonder about them.</summary>
    public string? More { get; init; }

    /// <summary>Other words search finds it by, such as "dark mode" for Theme.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Kept on this computer: settings sync leaves it as it is. From its key, unless said otherwise.</summary>
    public bool IsMachineOnly { get; init; }

    /// <summary>A setting, rather than a row that only does or tells something, such as <b>Clean up now</b>.</summary>
    public bool IsSetting { get; init; } = true;

    /// <summary>The key its per-tab override goes by (DESIGN.md §14, "Per-tab overrides"), for "2 tabs have their own".</summary>
    public string? OverrideKey { get; init; }

    /// <summary>Its section's <b>Reset to defaults</b> resets it too. Off for what has its own way back, such as the library folder.</summary>
    public bool ResetsWithSection { get; init; } = true;

    /// <summary>Puts it back, without saving.</summary>
    internal Action? ResetValue { get; }

    public bool CanReset => ResetValue is not null && _isDefault is not null;

    /// <summary>Differs from its default: its row is marked, and <b>Reset</b> puts it back.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial bool IsChanged { get; private set; }

    /// <summary>Its row shows "This computer": it's this computer's alone, and its section isn't all so.</summary>
    [ObservableProperty]
    public partial bool ShowsMachineMark { get; internal set; }

    /// <summary>"2 tabs have their own", while open tabs override it; null while none do.</summary>
    [ObservableProperty]
    public partial string? TabsNote { get; private set; }

    /// <summary>The tabs that have their own, for the note's tooltip.</summary>
    [ObservableProperty]
    public partial string? TabsTip { get; private set; }

    /// <summary>Search or a link brought it into view, and picks it out for a moment.</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; internal set; }

    [RelayCommand(CanExecute = nameof(IsChanged))]
    private void Reset()
    {
        // A section that asks before it resets, as quick suffixes' does, asks for its rows too.
        if (AsksVia is { } group)
        {
            group.ResetCommand.Execute(null);
            return;
        }
        _source?.Reset([this]);
    }

    /// <summary>The section whose question resetting it asks first; null resets it at once.</summary>
    internal SettingsGroup? AsksVia { get; set; }

    internal void AttachTo(ISettingsSource source) => _source = source;

    /// <summary>Reads whether it's at its default, and which tabs have their own, again.</summary>
    internal void Refresh(Func<string, IReadOnlyList<string>>? tabsWithOwn)
    {
        IsChanged = CanReset && !_isDefault!();
        var tabs = OverrideKey is { } overrideKey && tabsWithOwn is not null ? tabsWithOwn(overrideKey) : [];
        TabsNote = tabs.Count switch
        {
            0 => null,
            1 => "1 tab has its own",
            _ => $"{tabs.Count} tabs have their own",
        };
        TabsTip = tabs.Count == 0 ? null : $"Set in Tab settings… for {Core.Formats.List(tabs)}.";
    }

    /// <summary>The words search looks through.</summary>
    internal IEnumerable<string> SearchText => [Title, Description ?? "", More ?? "", .. Keywords];
}

/// <summary>
/// A section of a Settings page (DESIGN.md §14): its heading, its settings, whether they're all this computer's, and its
/// own <b>Reset to defaults</b>, which only resets the section's settings.
/// </summary>
public sealed partial class SettingsGroup : ObservableObject
{
    private readonly ISettingsSource _source;
    private readonly Action? _also;

    /// <param name="also">What else its reset does, after its settings: a list's rows read again, say.</param>
    public SettingsGroup(ISettingsSource source, string key, string title, IReadOnlyList<SettingItem> items, Action? also = null)
    {
        _source = source;
        _also = also;
        Key = key;
        Title = title;
        Items = items;
        var settings = items.Where(i => i.IsSetting).ToList();
        IsMachineOnly = settings.Count > 0 && settings.All(i => i.IsMachineOnly);
        foreach (var item in settings)
        {
            item.ShowsMachineMark = item.IsMachineOnly && !IsMachineOnly;
        }
    }

    public string Key { get; }

    public string Title { get; }

    /// <summary>One line under the heading.</summary>
    public string? Description { get; init; }

    public IReadOnlyList<SettingItem> Items { get; }

    /// <summary>Everything in it is kept on this computer: its heading says so, once, rather than each row.</summary>
    public bool IsMachineOnly { get; }

    /// <summary>Its heading shows "This computer": it's all this computer's, and its page isn't all so (which says it once, by its title).</summary>
    [ObservableProperty]
    public partial bool ShowsMachineMark { get; internal set; }

    /// <summary>It has a <b>Reset to defaults</b>: some of its settings have defaults to go back to.</summary>
    public bool CanReset => _also is not null || Items.Any(i => i.CanReset && i.ResetsWithSection);

    /// <summary>Something in it differs from its default, so its <b>Reset to defaults</b> has something to do.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial bool HasChanges { get; private set; }

    /// <summary>Search or a link brought it into view, and picks it out for a moment.</summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; internal set; }

    /// <summary>A section whose reset does more than its settings (<c>also</c>) can always be reset.</summary>
    private bool CanResetNow => HasChanges || _also is not null;

    /// <summary>
    /// What its <b>Reset to defaults</b> asks first, for a reset that loses the user's own entries, such as their quick
    /// suffixes; null resets at once.
    /// </summary>
    public string? ConfirmText { get; init; }

    /// <summary>The question <see cref="ConfirmText"/> asks, in place under the heading.</summary>
    public InlineConfirmation Confirmation => field ??= new(() =>
    {
        ResetNow();
        return Task.CompletedTask;
    });

    [RelayCommand(CanExecute = nameof(CanResetNow))]
    private void Reset()
    {
        if (ConfirmText is not null)
        {
            Confirmation.Ask();
            return;
        }
        ResetNow();
    }

    private void ResetNow() => _source.Reset([.. Items.Where(i => i.CanReset && i.ResetsWithSection && i.IsChanged)], _also);

    internal void Refresh() => HasChanges = Items.Any(i => i.ResetsWithSection && i.IsChanged);
}
