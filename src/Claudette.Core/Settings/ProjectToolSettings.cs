using System.Text.Json.Serialization;
using Claudette.Core.ProjectTools;

namespace Claudette.Core.Settings;

/// <summary>Unreal's editor configurations: which module DLLs the editor loads (DESIGN.md §18, "Project tools").</summary>
public enum UnrealConfiguration
{
    Development,
    DebugGame,
}

/// <summary>The project files Unreal generates.</summary>
public enum ProjectFileFormat
{
    VisualStudio,
    VSCode,
    Xcode,
}

/// <summary>How Unity compiles scripts in the editor: Debug adds <c>-debugCodeOptimization</c> (DESIGN.md §18).</summary>
public enum UnityCodeOptimization
{
    Release,
    Debug,
}

/// <summary>What <b>Open solution</b> opens a solution or workspace with.</summary>
public enum SolutionOpener
{
    /// <summary>The app the OS uses for the file.</summary>
    System,
    Rider,
    VisualStudio,
    VSCode,
    /// <summary>A program the user chose (<see cref="ProjectToolSettings.CustomIdePath"/>).</summary>
    Custom,
}

/// <summary>
/// One project type's settings on this machine (DESIGN.md §18, "Project types"): whether Claudette recognizes its
/// projects, whether it tells Claude about them, and the type's own settings by key.
/// </summary>
public sealed class ProjectTypeSettings
{
    /// <summary>Claudette recognizes this kind of project here; off, a folder with one is just a folder.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A session that starts in one of these projects gets the type's note through <c>--append-system-prompt</c>.</summary>
    public bool TellClaude { get; set; } = true;

    /// <summary>The type's own settings (<see cref="ProjectTypeSetting"/>), by key. One at its default isn't kept.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Settings → Project tools (DESIGN.md §18). They stay on each machine, like the diff tool: installed IDEs and
/// program paths differ between machines.
/// </summary>
public sealed class ProjectToolSettings
{
    /// <summary>Each project type's switches and settings, by its kind; a type with none here has its defaults.</summary>
    public Dictionary<string, ProjectTypeSettings> Types { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Claudette recognizes projects of <paramref name="kind"/> here: on unless turned off.</summary>
    public bool IsEnabled(string kind) => !Types.TryGetValue(kind, out var type) || type.Enabled;

    /// <summary>Claudette tells Claude about projects of <paramref name="kind"/>: on unless turned off.</summary>
    public bool TellsClaude(string kind) => !Types.TryGetValue(kind, out var type) || type.TellClaude;

    /// <summary>A type's own setting, or null while it's at its default.</summary>
    public string? Value(string kind, string key) => Types.TryGetValue(kind, out var type) && type.Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>A type's settings, made when it has none yet, to change.</summary>
    public ProjectTypeSettings Type(string kind)
    {
        if (!Types.TryGetValue(kind, out var type))
        {
            Types[kind] = type = new ProjectTypeSettings();
        }
        return type;
    }

    /// <summary>Sets a type's own setting; null, or empty, puts it back to its default.</summary>
    public void SetValue(string kind, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (Types.TryGetValue(kind, out var type))
            {
                type.Values.Remove(key);
            }
            return;
        }
        Type(kind).Values[key] = value.Trim();
    }

    public SolutionOpener OpenSolutionsWith { get; set; } = SolutionOpener.System;

    /// <summary>The program for <see cref="SolutionOpener.Custom"/>; it's given the solution's path.</summary>
    public string? CustomIdePath { get; set; }

    /// <summary>
    /// While the project's editor, or another of its engine's processes, runs, its log shows under the tab (DESIGN.md §18,
    /// "Editor logs").
    /// </summary>
    public bool ShowEditorLogs { get; set; } = true;

    // ---- Before project types: read from an older settings.json once, and kept by type from then on -----------------

    /// <summary>Unreal's default editor configuration, as an older settings.json had it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnrealConfiguration? UnrealConfiguration
    {
        get => null;
        set => Moved("unreal", "configuration", value?.ToString());
    }

    /// <summary>Unreal's project files, as an older settings.json had them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectFileFormat? ProjectFileFormat
    {
        get => null;
        set => Moved("unreal", "projectFiles", value?.ToString());
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TellClaudeAboutUnreal
    {
        get => null;
        set => MovedTellClaude("unreal", value);
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnityCodeOptimization? UnityCodeOptimization
    {
        get => null;
        set => Moved("unity", "codeOptimization", value?.ToString());
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TellClaudeAboutUnity
    {
        get => null;
        set => MovedTellClaude("unity", value);
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GodotPath
    {
        get => null;
        set => Moved("godot", "path", value);
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TellClaudeAboutGodot
    {
        get => null;
        set => MovedTellClaude("godot", value);
    }

    /// <summary>
    /// An older setting at its default stays a default; one the user changed is kept by its type. Unreal's and Unity's
    /// first choices were their defaults, so they aren't kept either.
    /// </summary>
    private void Moved(string kind, string key, string? value)
    {
        if (value is not null && value is not nameof(Settings.UnrealConfiguration.Development) and not nameof(Settings.UnityCodeOptimization.Release))
        {
            SetValue(kind, key, value);
        }
    }

    private void MovedTellClaude(string kind, bool? value)
    {
        if (value == false)
        {
            Type(kind).TellClaude = false;
        }
    }
}
