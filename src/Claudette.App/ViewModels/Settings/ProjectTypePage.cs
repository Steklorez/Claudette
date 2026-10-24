using Claudette.Core.ProjectTools;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// One project type's page in Settings → Project tools (DESIGN.md §18, "Project types"): whether Claudette recognizes
/// its projects here and tells Claude about them, then the defaults its projects start with, each a row drawn from what
/// the type declares, so a type needs no view of its own. What every type shares is on <see cref="AllToolsPage"/>. Kept
/// on each machine, like the diff tool.
/// </summary>
public sealed class ProjectTypePage : SettingsPage
{
    public ProjectTypePage(SettingsContext context, IProjectType type) : base(context, type.Name)
    {
        Type = type;
        var settings = ToolSettings;
        var kind = type.Kind;
        Switches =
        [
            new ProjectTypeSwitchRow(this, Declare(Key("enabled"), $"Recognize {type.Name} projects",
                    () => settings.IsEnabled(kind), () => settings.Type(kind).Enabled = true,
                    "Off, a folder with one is just a folder: no engine actions, editor logs or note to Claude.", null, ["turn off", type.Name]),
                () => settings.IsEnabled(kind), on => settings.Type(kind).Enabled = on) { NeedsType = false },
            new ProjectTypeSwitchRow(this, Declare(Key("tellClaude"), $"Tell Claude about {type.Name} projects",
                    () => settings.TellsClaude(kind), () => settings.Type(kind).TellClaude = true,
                    type.NoteDescription, "Added to the session as a short note. Applies to tabs started after the change.", ["system prompt", type.Name]),
                () => settings.TellsClaude(kind), on => settings.Type(kind).TellClaude = on),
        ];
        Defaults = [.. type.Settings.Select(Row)];
        Section(kind, "Projects", [.. Switches.Select(row => row.Item)]);
        if (Defaults.Count > 0)
        {
            Section(DefaultsKey, "Defaults", [.. Defaults.Select(row => row.Item)],
                description: $"What every {type.Name} project starts with.");
        }
    }

    public IProjectType Type { get; }

    public string Kind => Type.Kind;

    public override string IconKey => ProjectTypeIcons.For(Kind);

    public override string Description => Type.Description;

    /// <summary><i>Off</i> beside its name in the sidebar while its projects aren't recognized; the page can still be picked.</summary>
    public override string? NavTag => IsEnabled ? null : "Off";

    /// <summary><b>Recognize … projects</b>, then <b>Tell Claude about … projects</b>.</summary>
    public IReadOnlyList<ProjectTypeSwitchRow> Switches { get; }

    /// <summary>The type's own settings, in the order it declares them; none for a type without.</summary>
    public IReadOnlyList<ProjectTypeRow> Defaults { get; }

    public bool HasDefaults => Defaults.Count > 0;

    /// <summary>The key of the section with the type's own settings, after its switches' (<see cref="Kind"/>).</summary>
    public string DefaultsKey => $"{Kind}.defaults";

    /// <summary>Claudette recognizes the type's projects here: its other rows apply.</summary>
    public bool IsEnabled => ToolSettings.IsEnabled(Kind);

    internal ProjectToolSettings ToolSettings => Settings.ProjectTools;

    /// <summary>Found by the group's name too, as when the types were sections of one Project tools page.</summary>
    protected override SettingsSearchResult Entry(string key, string label, string? description, string? section, IEnumerable<string> words) =>
        base.Entry(key, label, description, section, [.. words, SettingsSidebar.ProjectTools]);

    private string Key(string part) => $"projectTools.types.{Kind}.{part}";

    private SettingItem Declare(string key, string title, Func<bool>? isDefault, Action? reset, string? description, string? more, IReadOnlyList<string> keywords) =>
        Setting(key, title, isDefault, reset, description, more, keywords);

    private ProjectTypeRow Row(ProjectTypeSetting setting)
    {
        var settings = ToolSettings;
        var item = Declare(Key($"values.{setting.Key}"), setting.Title,
            () => settings.Value(Kind, setting.Key) is null, () => settings.SetValue(Kind, setting.Key, null),
            setting.Description, setting.More, [.. setting.Keywords, Type.Name]);
        return setting switch
        {
            ProjectTypeChoice choice => new ProjectTypeChoiceRow(this, item, choice),
            ProjectTypePath path => new ProjectTypePathRow(this, item, path),
            _ => throw new NotSupportedException($"Settings can't show a {setting.GetType().Name} yet."),
        };
    }

    /// <summary>A row's setting changed: saved at once, as every setting is.</summary>
    internal void Changed() => Save();

    /// <summary>Every row reads its value again, after a reset or a change to the type's switch.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(NavTag));
        foreach (var row in Switches.Concat(Defaults))
        {
            row.Refresh();
        }
    }

    protected override void OnReset() => Refresh();

    /// <summary>Picks a program or folder for a type's path setting.</summary>
    internal Task<string?> PickAsync(ProjectTypePath setting) =>
        setting.PickFolder ? Services.Platform.PickFolderAsync(setting.BrowseTitle ?? setting.Title) : Services.Platform.PickFileAsync(setting.BrowseTitle ?? setting.Title);

    /// <summary>What a type needs to look for something, such as Godot where its installers put it.</summary>
    internal ProjectToolContext ToolContext() => Services.ProjectTools.Context();
}

/// <summary>A row of a project type's page: its setting, by its key, and whether it applies now.</summary>
public abstract class ProjectTypeRow(ProjectTypePage page, SettingItem item) : ObservableObject
{
    public ProjectTypePage Page { get; } = page;

    public SettingItem Item { get; } = item;

    public string Key => Item.Key;

    /// <summary>A row that only means something while the type's projects are recognized, as every one but the switch.</summary>
    public bool NeedsType { get; init; } = true;

    public bool IsAvailable => !NeedsType || Page.IsEnabled;

    protected ProjectToolSettings Settings => Page.ToolSettings;

    protected void Changed()
    {
        Page.Changed();
        OnPropertyChanged(string.Empty);
    }

    internal virtual void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A switch: the type's own, or <b>Tell Claude about …</b>.</summary>
public sealed class ProjectTypeSwitchRow(ProjectTypePage page, SettingItem item, Func<bool> read, Action<bool> write) : ProjectTypeRow(page, item)
{
    public bool IsOn
    {
        get => read();
        set
        {
            if (value != read())
            {
                write(value);
                Changed();
                // The type's switch decides whether its other rows apply, and its tag in the sidebar.
                Page.Refresh();
            }
        }
    }
}

/// <summary>A choice among the type's options, as a dropdown. Picking its default keeps nothing.</summary>
public sealed class ProjectTypeChoiceRow(ProjectTypePage page, SettingItem item, ProjectTypeChoice choice) : ProjectTypeRow(page, item)
{
    public IReadOnlyList<SettingChoice<string>> Options { get; } = [.. choice.Options.Select(o => new SettingChoice<string>(o.Value, o.Label))];

    public SettingChoice<string>? Selected
    {
        get => Options.FirstOrDefault(o => o.Value == choice.ValueIn(Settings, Page.Kind)) ?? Options.FirstOrDefault(o => o.Value == choice.Default);
        set
        {
            if (value is not null && value.Value != choice.ValueIn(Settings, Page.Kind))
            {
                Settings.SetValue(Page.Kind, choice.Key, value.Value == choice.Default ? null : value.Value);
                Changed();
            }
        }
    }
}

/// <summary>A program or folder, typed, picked with <b>Browse…</b>, or found with <b>Detect</b>.</summary>
public sealed partial class ProjectTypePathRow(ProjectTypePage page, SettingItem item, ProjectTypePath path) : ProjectTypeRow(page, item)
{
    public string? Placeholder => path.Placeholder;

    /// <summary>Empty for none, which the type finds by itself.</summary>
    public string Value
    {
        get => Settings.Value(Page.Kind, path.Key) ?? "";
        set
        {
            if (value != Value)
            {
                Settings.SetValue(Page.Kind, path.Key, value);
                Changed();
            }
        }
    }

    public bool CanDetect => path.Detect is not null;

    /// <summary>What <b>Detect</b> found, or that it found nothing.</summary>
    [ObservableProperty]
    public partial string? DetectResult { get; private set; }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (await Page.PickAsync(path) is { } picked)
        {
            Value = picked;
        }
    }

    /// <summary>Looks where the type usually finds it, off the UI thread, and fills it in.</summary>
    [RelayCommand]
    private async Task DetectAsync()
    {
        if (path.Detect is not { } detect)
        {
            return;
        }
        var context = Page.ToolContext();
        var found = await Task.Run(() => detect(context));
        if (found is null)
        {
            DetectResult = $"{path.Title} wasn't found where it's usually installed. Browse to it instead.";
            return;
        }
        Value = found;
        DetectResult = $"Found {found}.";
    }

    internal override void Refresh()
    {
        DetectResult = null;
        base.Refresh();
    }
}
