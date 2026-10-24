using Claudette.Core.ProjectTools;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// Settings → Project tools (DESIGN.md §18, "Project tools"): a section for each project type Claudette knows, drawn
/// from what the type declares (its switch, telling Claude, its own settings), then how solutions open and the editor
/// logs. Kept on each machine, like the diff tool.
/// </summary>
public sealed partial class ProjectToolsPage : SettingsPage
{
    public ProjectToolsPage(SettingsContext context) : base(context, SettingsCategory.ProjectTools)
    {
        Types = [.. Services.ProjectTools.Detector.Types.Select(type => new ProjectTypeSection(this, type))];
        foreach (var type in Types)
        {
            Section(type.Kind, type.Name, [.. type.Rows.Select(row => row.Item)], description: type.Description);
        }
        Section("solutions", "Solutions",
        [
            Setting("projectTools.openSolutionsWith", "Open solutions with", s => s.ProjectTools.OpenSolutionsWith, (s, v) => s.ProjectTools.OpenSolutionsWith = v,
                keywords: ["ide", "rider", "visual studio", "vs code"]),
            Setting("projectTools.customIdePath", "Program", s => s.ProjectTools.CustomIdePath, (s, v) => s.ProjectTools.CustomIdePath = v,
                "It's given the solution's path.", keywords: ["ide", "another program"]),
        ]);
        Section("editorLogs", "Editor logs",
        [
            Setting("projectTools.showEditorLogs", "Show editor logs under the tab", s => s.ProjectTools.ShowEditorLogs, (s, v) => s.ProjectTools.ShowEditorLogs = v,
                "While the editor, a commandlet or a batch run is going, its log is a row under one of the project's tabs.",
                "Read as it's written, without holding the file open. The row stays after the editor closes.", ["log", "unreal", "unity", "godot"]),
        ]);
    }

    public override string IconKey => "IconProject";

    public override string Description => "The kinds of project Claudette knows, what they start with, and how solutions open.";

    /// <summary>Each project type's section, in the order Claudette knows them.</summary>
    public IReadOnlyList<ProjectTypeSection> Types { get; }

    /// <summary>A row's setting, for <see cref="ProjectTypeSection"/>, declared on this page so it's found by key and reset with its section.</summary>
    internal SettingItem Declare(string key, string title, Func<bool>? isDefault, Action? reset, string? description, string? more, IReadOnlyList<string> keywords) =>
        Setting(key, title, isDefault, reset, description, more, keywords);

    /// <summary>A type's setting changed: saved at once, as every setting is.</summary>
    internal void Changed() => Save();

    /// <summary>The project types' rows read their values again.</summary>
    protected override void OnReset()
    {
        foreach (var type in Types)
        {
            type.Refresh();
        }
    }

    // ---- Solutions ---------------------------------------------------------------------------------------------------

    public IReadOnlyList<SettingChoice<SolutionOpener>> SolutionOpenerChoices { get; } =
    [
        new(SolutionOpener.System, "The OS's default app"),
        new(SolutionOpener.Rider, "Rider"),
        new(SolutionOpener.VisualStudio, "Visual Studio"),
        new(SolutionOpener.VSCode, "VS Code"),
        new(SolutionOpener.Custom, "Another program…"),
    ];

    public SettingChoice<SolutionOpener> OpenSolutionsWith
    {
        get => SolutionOpenerChoices.First(c => c.Value == Settings.ProjectTools.OpenSolutionsWith);
        set
        {
            if (value is not null)
            {
                Set(value.Value, v => Settings.ProjectTools.OpenSolutionsWith = v);
                OnPropertyChanged(nameof(IsCustomIde));
            }
        }
    }

    public bool IsCustomIde => Settings.ProjectTools.OpenSolutionsWith == SolutionOpener.Custom;

    /// <summary>The program <b>Open solution</b> starts with the solution's path, for "Another program…".</summary>
    public string CustomIdePath
    {
        get => Settings.ProjectTools.CustomIdePath ?? "";
        set => Set(value, v => Settings.ProjectTools.CustomIdePath = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    [RelayCommand]
    private async Task BrowseCustomIdeAsync()
    {
        if (await Services.Platform.PickFileAsync("Choose the program to open solutions with") is { } path)
        {
            CustomIdePath = path;
        }
    }

    // ---- Editor logs (DESIGN.md §18, "Editor logs") -------------------------------------------------------------------

    public bool ShowEditorLogs
    {
        get => Settings.ProjectTools.ShowEditorLogs;
        set => Set(value, v => Settings.ProjectTools.ShowEditorLogs = v);
    }

    /// <summary>Picks a program or folder for a type's path setting.</summary>
    internal Task<string?> PickAsync(ProjectTypePath setting) =>
        setting.PickFolder ? Services.Platform.PickFolderAsync(setting.BrowseTitle ?? setting.Title) : Services.Platform.PickFileAsync(setting.BrowseTitle ?? setting.Title);

    /// <summary>What a type needs to look for something, such as Godot where its installers put it.</summary>
    internal ProjectToolContext ToolContext() => Services.ProjectTools.Context();

    internal ProjectToolSettings ToolSettings => Settings.ProjectTools;
}

/// <summary>
/// One project type's section of Settings → Project tools (DESIGN.md §18, "Project types"): whether Claudette recognizes
/// its projects here, whether it tells Claude about them, and the type's own settings, each a row drawn from what the
/// type declares.
/// </summary>
public sealed class ProjectTypeSection : ObservableObject
{
    private readonly ProjectToolsPage _page;

    public ProjectTypeSection(ProjectToolsPage page, IProjectType type)
    {
        _page = page;
        Type = type;
        var settings = page.ToolSettings;
        var kind = type.Kind;
        var enabled = new ProjectTypeSwitchRow(this, page.Declare(Key("enabled"), $"Recognize {type.Name} projects",
                () => settings.IsEnabled(kind), () => settings.Type(kind).Enabled = true,
                "Off, a folder with one is just a folder: no engine actions, editor logs or note to Claude.", null, ["turn off", type.Name]),
            () => settings.IsEnabled(kind), on => settings.Type(kind).Enabled = on) { NeedsType = false };
        var tell = new ProjectTypeSwitchRow(this, page.Declare(Key("tellClaude"), $"Tell Claude about {type.Name} projects",
                () => settings.TellsClaude(kind), () => settings.Type(kind).TellClaude = true,
                type.NoteDescription, "Added to the session as a short note. Applies to tabs started after the change.", ["system prompt", type.Name]),
            () => settings.TellsClaude(kind), on => settings.Type(kind).TellClaude = on);
        Rows =
        [
            enabled,
            tell,
            .. type.Settings.Select(setting => Row(setting)),
        ];
    }

    public IProjectType Type { get; }

    public string Kind => Type.Kind;

    public string Name => Type.Name;

    public string Description => Type.Description;

    /// <summary>Its switch, then <b>Tell Claude about …</b>, then the type's own settings.</summary>
    public IReadOnlyList<ProjectTypeRow> Rows { get; }

    /// <summary>Claudette recognizes the type's projects here: its other rows apply.</summary>
    public bool IsEnabled => _page.ToolSettings.IsEnabled(Kind);

    internal ProjectToolsPage Page => _page;

    private string Key(string part) => $"projectTools.types.{Kind}.{part}";

    private ProjectTypeRow Row(ProjectTypeSetting setting)
    {
        var settings = _page.ToolSettings;
        var item = _page.Declare(Key($"values.{setting.Key}"), setting.Title,
            () => settings.Value(Kind, setting.Key) is null, () => settings.SetValue(Kind, setting.Key, null),
            setting.Description, setting.More, [.. setting.Keywords, Name]);
        return setting switch
        {
            ProjectTypeChoice choice => new ProjectTypeChoiceRow(this, item, choice),
            ProjectTypePath path => new ProjectTypePathRow(this, item, path),
            _ => throw new NotSupportedException($"Settings can't show a {setting.GetType().Name} yet."),
        };
    }

    /// <summary>Every row reads its value again, after a reset or a change to the type's switch.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        foreach (var row in Rows)
        {
            row.Refresh();
        }
    }
}

/// <summary>A row of a project type's section: its setting, by its key, and whether it applies now.</summary>
public abstract class ProjectTypeRow(ProjectTypeSection section, SettingItem item) : ObservableObject
{
    public ProjectTypeSection Section { get; } = section;

    public SettingItem Item { get; } = item;

    public string Key => Item.Key;

    /// <summary>A row that only means something while the type's projects are recognized, as every one but the switch.</summary>
    public bool NeedsType { get; init; } = true;

    public bool IsAvailable => !NeedsType || Section.IsEnabled;

    protected ProjectToolSettings Settings => Section.Page.ToolSettings;

    protected void Changed()
    {
        Section.Page.Changed();
        OnPropertyChanged(string.Empty);
    }

    internal virtual void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A switch: the type's own, or <b>Tell Claude about …</b>.</summary>
public sealed class ProjectTypeSwitchRow(ProjectTypeSection section, SettingItem item, Func<bool> read, Action<bool> write) : ProjectTypeRow(section, item)
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
                // The type's switch decides whether its other rows apply.
                Section.Refresh();
            }
        }
    }
}

/// <summary>A choice among the type's options, as a dropdown. Picking its default keeps nothing.</summary>
public sealed class ProjectTypeChoiceRow(ProjectTypeSection section, SettingItem item, ProjectTypeChoice choice) : ProjectTypeRow(section, item)
{
    public IReadOnlyList<SettingChoice<string>> Options { get; } = [.. choice.Options.Select(o => new SettingChoice<string>(o.Value, o.Label))];

    public SettingChoice<string>? Selected
    {
        get => Options.FirstOrDefault(o => o.Value == choice.ValueIn(Settings, Section.Kind)) ?? Options.FirstOrDefault(o => o.Value == choice.Default);
        set
        {
            if (value is not null && value.Value != choice.ValueIn(Settings, Section.Kind))
            {
                Settings.SetValue(Section.Kind, choice.Key, value.Value == choice.Default ? null : value.Value);
                Changed();
            }
        }
    }
}

/// <summary>A program or folder, typed, picked with <b>Browse…</b>, or found with <b>Detect</b>.</summary>
public sealed partial class ProjectTypePathRow(ProjectTypeSection section, SettingItem item, ProjectTypePath path) : ProjectTypeRow(section, item)
{
    public string? Placeholder => path.Placeholder;

    /// <summary>Empty for none, which the type finds by itself.</summary>
    public string Value
    {
        get => Settings.Value(Section.Kind, path.Key) ?? "";
        set
        {
            if (value != Value)
            {
                Settings.SetValue(Section.Kind, path.Key, value);
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
        if (await Section.Page.PickAsync(path) is { } picked)
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
        var context = Section.Page.ToolContext();
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
