using Claudette.Core.ProjectTools;
using Claudette.Core.ProjectTools.Unreal;
using Claudette.Core.Settings;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectTools;

/// <summary>Builds pretend projects and engines in a temporary folder. No real engine is ever used.</summary>
internal static class ProjectToolFixtures
{
    /// <summary>The built-in project types, as the app has them, with no registry entries.</summary>
    public static ProjectToolDetector Detector(IUnrealEngineRegistry? registry = null) =>
        new([new UnrealProvider(registry), new Core.ProjectTools.Unity.UnityProvider(), new Core.ProjectTools.Godot.GodotProvider()]);

    /// <summary>A context as the app makes one: the search skips every built-in type's folders.</summary>
    public static ProjectToolContext Context(TempFolder temp, ToolOS? os = null, ProjectToolSettings? settings = null, IProjectMemory? memory = null,
        ISystemProcesses? processes = null) => new()
    {
        OS = os ?? ToolOSExtensions.Current,
        Settings = settings ?? new ProjectToolSettings(),
        Paths = new ProjectToolPaths(temp.Combine("home"), temp.Combine("appdata"), temp.Combine("localappdata"), temp.Combine("programdata"), temp.Combine("programfiles")),
        Memory = memory ?? NoProjectMemory.Instance,
        Processes = processes,
        SkippedFolders = Detector().SkippedFolders,
    };

    /// <summary>The settings with one of a project type's own settings changed, as Settings → Project tools changes it.</summary>
    public static ProjectToolSettings With(this ProjectToolSettings settings, string kind, string key, string? value)
    {
        settings.SetValue(kind, key, value);
        return settings;
    }

    /// <summary>The settings with <b>Tell Claude about …</b> off for a project type.</summary>
    public static ProjectToolSettings Quiet(this ProjectToolSettings settings, string kind)
    {
        settings.Type(kind).TellClaude = false;
        return settings;
    }

    /// <summary>A <c>.uproject</c> written the way people edit them: a comment, a trailing comma, a field Claudette doesn't know.</summary>
    public static string UProject(TempFolder temp, string relativePath, string? association = "5.4", bool code = true)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath);
        var associationLine = association is null ? "" : $"\"EngineAssociation\": \"{association}\",";
        var path = temp.Write(relativePath, $$"""
            {
              // Written by the editor, then edited by hand.
              "FileVersion": 3,
              {{associationLine}}
              "Category": "",
              "Modules": [
                {{(code ? $$"""{ "Name": "{{name}}", "Type": "Runtime", "LoadingPhase": "Default", },""" : "")}}
              ],
              "Plugins": [
                { "Name": "ModelingToolsEditorMode", "Enabled": true, "TargetAllowList": ["Editor"] },
                { "Name": "OldThing", "Enabled": false },
              ],
              "SomethingNew": { "nested": [1, 2, 3] },
            }
            """);
        if (code)
        {
            var root = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            var prefix = root.Length > 0 ? root + "/" : "";
            temp.Write($"{prefix}Source/{name}.Target.cs", "// game target");
            temp.Write($"{prefix}Source/{name}Editor.Target.cs", "// editor target");
        }
        return path;
    }

    /// <summary>An engine folder with <c>Engine/Build/Build.version</c>, its build script and its editor.</summary>
    public static string Engine(TempFolder temp, string relativePath, int major = 5, int minor = 4, int patch = 2, bool installed = true, bool editor = true)
    {
        var prefix = relativePath.Length > 0 ? relativePath + "/" : "";
        temp.Write($"{prefix}Engine/Build/Build.version", $$"""
            {
              "MajorVersion": {{major}},
              "MinorVersion": {{minor}},
              "PatchVersion": {{patch}},
              "Changelist": 0,
              "CompatibleChangelist": 0,
              "IsLicenseeVersion": 0,
              "IsPromotedBuild": 1,
              "BranchName": "++UE{{major}}+Release-{{major}}.{{minor}}"
            }
            """);
        if (installed)
        {
            temp.Write($"{prefix}Engine/Build/InstalledBuild.txt", "");
        }
        var os = ToolOSExtensions.Current;
        var root = relativePath.Length > 0 ? temp.Combine(relativePath.Split('/')) : temp.Path;
        var script = UnrealCommands.BuildScript(root, os);
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "");
        if (editor)
        {
            var exe = UnrealCommands.EditorPath(root, major == 4 ? "UE4Editor" : "UnrealEditor", os);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");
        }
        return root;
    }
}

internal sealed class FakeUnrealRegistry : IUnrealEngineRegistry
{
    public Dictionary<string, string> LauncherInstalls { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Builds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? FindLauncherInstall(string version) => LauncherInstalls.GetValueOrDefault(version);

    public IReadOnlyDictionary<string, string> SourceBuilds() => Builds;
}

internal sealed class DictionaryMemory : IProjectMemory
{
    public Dictionary<(string Path, string Key), string> Values { get; } = [];

    public string? Get(string projectPath, string key) => Values.GetValueOrDefault((projectPath, key));
}

internal sealed class FakeSystemProcesses : ISystemProcesses
{
    public List<SystemProcess> Running { get; } = [];

    public List<int> Killed { get; } = [];

    public IReadOnlyList<SystemProcess> Find(IReadOnlyCollection<string> names) =>
        Running.Where(p => SystemProcessNames.Matches(p.Name, names)).ToArray();

    public void KillTree(SystemProcess process)
    {
        Killed.Add(process.Pid);
        Running.RemoveAll(p => p.Pid == process.Pid);
    }
}
