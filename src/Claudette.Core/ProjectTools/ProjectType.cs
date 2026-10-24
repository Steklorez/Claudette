namespace Claudette.Core.ProjectTools;

/// <summary>
/// A kind of project Claudette knows (DESIGN.md §18, "Project types"): it finds its projects for a tab's folder, says
/// what can be done with one, and declares its settings, which Settings → Project tools draws in its section with its
/// on and off switch and <b>Tell Claude about …</b>. Unreal Engine, Unity and Godot are the first. Adding one is a class
/// that implements this and a line in the app's list of them (DESIGN.md §18, "Adding a project type").
/// </summary>
/// <remarks>
/// <see cref="Find"/> and <see cref="Describe"/> run off the UI thread and must be cheap: bounded walks, small files,
/// nothing started. Commands are built by pure functions that take the OS, so every OS's are tested on any machine.
/// </remarks>
public interface IProjectType
{
    /// <summary>A stable id, such as <c>unreal</c>: its projects' <see cref="ProjectInfo.Kind"/>, and its settings' key.</summary>
    string Kind { get; }

    /// <summary>"Unreal Engine": its section's heading in Settings, and its projects' kind.</summary>
    string Name { get; }

    /// <summary>One line in Settings: what it recognizes and what it does with it.</summary>
    string Description { get; }

    /// <summary>What its note to Claude says, for <b>Tell Claude about …</b> in Settings.</summary>
    string NoteDescription { get; }

    /// <summary>Its own settings, in the order Settings shows them.</summary>
    IReadOnlyList<ProjectTypeSetting> Settings { get; }

    /// <summary>
    /// Folders its projects keep their builds and caches in, such as Unreal's <c>Intermediate</c>: the search for
    /// projects never looks into them, for any type.
    /// </summary>
    IReadOnlySet<string> SkippedFolders { get; }

    /// <summary>The projects it finds for <paramref name="folder"/>, nearest first.</summary>
    IReadOnlyList<ProjectCandidate> Find(string folder, ProjectToolContext context);

    /// <summary>
    /// Reads a project and builds its actions. Never throws for a broken project: <see cref="ProjectInfo.Problem"/> says
    /// what's wrong. Its <see cref="ProjectInfo.SystemPromptNote"/> is always written; Claudette leaves it out when
    /// Settings says not to tell Claude.
    /// </summary>
    ProjectInfo Describe(ProjectCandidate candidate, ProjectToolContext context);
}

/// <summary>A setting a project type has, which Settings → Project tools shows in the type's section.</summary>
/// <param name="Key">Where its value is kept, in the type's <see cref="Settings.ProjectTypeSettings.Values"/>.</param>
public abstract record ProjectTypeSetting(string Key, string Title)
{
    /// <summary>One line under its title.</summary>
    public string? Description { get; init; }

    /// <summary>The rest, behind <b>More</b>.</summary>
    public string? More { get; init; }

    /// <summary>Other words search finds it by.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Its value as the type reads it: what's kept, else its default.</summary>
    public string? ValueIn(Settings.ProjectToolSettings settings, string kind) => settings.Value(kind, Key) ?? DefaultValue;

    /// <summary>What it is until it's changed; null for none, such as a path that's found.</summary>
    public abstract string? DefaultValue { get; }
}

/// <summary>A choice among options, shown as a dropdown: Unreal's default editor configuration.</summary>
public sealed record ProjectTypeChoice(string Key, string Title, IReadOnlyList<ProjectChoiceOption> Options, string Default) : ProjectTypeSetting(Key, Title)
{
    public override string? DefaultValue => Default;
}

/// <summary>A program or folder, typed or picked with <b>Browse…</b>, and found with <b>Detect</b> when the type can: the Godot executable.</summary>
public sealed record ProjectTypePath(string Key, string Title) : ProjectTypeSetting(Key, Title)
{
    /// <summary>What the empty box says, such as "Found automatically".</summary>
    public string? Placeholder { get; init; }

    /// <summary><b>Browse…</b>'s title.</summary>
    public string? BrowseTitle { get; init; }

    /// <summary>Picks a folder rather than a file.</summary>
    public bool PickFolder { get; init; }

    /// <summary><b>Detect</b>: where it's usually found, ignoring the setting; null for a type that can't look.</summary>
    public Func<ProjectToolContext, string?>? Detect { get; init; }

    public override string? DefaultValue => null;
}
