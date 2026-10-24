using System.Text.Json;
using Claudette.Core.ProjectTools;
using Claudette.Core.ProjectTools.Godot;
using Claudette.Core.ProjectTools.Unity;
using Claudette.Core.ProjectTools.Unreal;
using Claudette.Core.Settings;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectTools;

/// <summary>
/// Project types (DESIGN.md §18, "Project types"): each is switched on and off on this machine, says whether Claude is
/// told about its projects, and keeps its own settings by key; an older settings.json's are moved to them.
/// </summary>
public sealed class ProjectTypeTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-project-types");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Every_type_has_its_own_kind_a_name_and_settings_of_its_own()
    {
        var types = ProjectToolFixtures.Detector().Types;

        Assert.Equal([UnrealProvider.KindId, UnityProvider.KindId, GodotProvider.KindId], types.Select(t => t.Kind));
        Assert.All(types, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.False(string.IsNullOrWhiteSpace(t.NoteDescription));
            Assert.Equal(t.Settings.Count, t.Settings.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
        });
        Assert.Equal(types.Count, types.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_type_switched_off_finds_nothing_and_the_others_still_do()
    {
        _temp.CreateFolder("repo/.git");
        ProjectToolFixtures.UProject(_temp, "repo/NightOwl.uproject");
        _temp.Write("repo/Godot/project.godot", "config_version=5\n[application]\nconfig/name=\"Moth\"\n");
        var settings = new ProjectToolSettings();
        var context = ProjectToolFixtures.Context(_temp, settings: settings);
        var detector = ProjectToolFixtures.Detector();
        Assert.Equal([UnrealProvider.KindId, GodotProvider.KindId], detector.Detect(_temp.Combine("repo"), context).Candidates.Select(c => c.Kind));

        settings.Type(UnrealProvider.KindId).Enabled = false;

        var found = detector.Detect(_temp.Combine("repo"), context);
        Assert.Equal([GodotProvider.KindId], found.Candidates.Select(c => c.Kind));
        Assert.Equal("Moth", found.Project!.Name);
        settings.Type(GodotProvider.KindId).Enabled = false;
        Assert.Same(ProjectDetection.None, detector.Detect(_temp.Combine("repo"), context));
    }

    [Fact]
    public void With_Tell_Claude_off_a_project_has_no_note()
    {
        _temp.CreateFolder("repo/.git");
        ProjectToolFixtures.UProject(_temp, "repo/NightOwl.uproject");
        var settings = new ProjectToolSettings();
        var context = ProjectToolFixtures.Context(_temp, settings: settings);
        var detector = ProjectToolFixtures.Detector();
        Assert.NotNull(detector.Detect(_temp.Combine("repo"), context).Project!.SystemPromptNote);

        settings.Quiet(UnrealProvider.KindId);

        var project = detector.Detect(_temp.Combine("repo"), context).Project!;
        Assert.Null(project.SystemPromptNote);
        Assert.Equal("NightOwl", project.Name);
    }

    /// <summary>The search skips every type's build and cache folders, whether the type is on or not.</summary>
    [Fact]
    public void The_search_skips_the_shared_folders_and_every_types_own()
    {
        var skipped = ProjectToolFixtures.Detector().SkippedFolders;

        Assert.Superset(new HashSet<string>(ProjectSearch.SkippedFolders, StringComparer.OrdinalIgnoreCase), new HashSet<string>(skipped, StringComparer.OrdinalIgnoreCase));
        Assert.Contains("Intermediate", skipped);
        Assert.Contains("Library", skipped);
        Assert.Contains("node_modules", skipped);
        Assert.DoesNotContain("Intermediate", ProjectSearch.SkippedFolders);
    }

    [Fact]
    public void A_setting_set_back_to_nothing_is_its_default_again()
    {
        var settings = new ProjectToolSettings();
        settings.SetValue(UnrealProvider.KindId, UnrealProvider.ConfigurationKey, " DebugGame ");
        Assert.Equal(UnrealConfiguration.DebugGame, UnrealProvider.DefaultConfiguration(settings));

        settings.SetValue(UnrealProvider.KindId, UnrealProvider.ConfigurationKey, "  ");

        Assert.Null(settings.Value(UnrealProvider.KindId, UnrealProvider.ConfigurationKey));
        Assert.Equal(UnrealConfiguration.Development, UnrealProvider.DefaultConfiguration(settings));
        // Nothing to remove is nothing made.
        settings.SetValue(GodotProvider.KindId, GodotProvider.PathKey, null);
        Assert.False(settings.Types.ContainsKey(GodotProvider.KindId));
        Assert.True(settings.IsEnabled(GodotProvider.KindId));
        Assert.True(settings.TellsClaude(GodotProvider.KindId));
    }

    /// <summary>An older settings.json's project tool settings are kept by their types, and aren't written back.</summary>
    [Fact]
    public void An_older_settings_file_moves_its_choices_to_their_types()
    {
        var path = _temp.Write("settings.json", """
            {
              "projectTools": {
                "unrealConfiguration": "debugGame",
                "projectFileFormat": "vsCode",
                "tellClaudeAboutUnreal": true,
                "unityCodeOptimization": "release",
                "tellClaudeAboutUnity": false,
                "godotPath": "/opt/godot/godot",
                "showEditorLogs": false
              }
            }
            """);
        var store = new JsonFileStore<AppSettings>(path);

        var tools = store.Load().ProjectTools;

        Assert.Equal(UnrealConfiguration.DebugGame, UnrealProvider.DefaultConfiguration(tools));
        Assert.Equal(ProjectFileFormat.VSCode, UnrealProvider.FormatFor(tools, ToolOS.Windows));
        Assert.True(tools.TellsClaude(UnrealProvider.KindId));
        // Release was Unity's default, so it stays one.
        Assert.Null(tools.Value(UnityProvider.KindId, UnityProvider.OptimizationKey));
        Assert.False(tools.TellsClaude(UnityProvider.KindId));
        Assert.Equal("/opt/godot/godot", tools.Value(GodotProvider.KindId, GodotProvider.PathKey));
        Assert.False(tools.ShowEditorLogs);

        var written = JsonFileStore<AppSettings>.Serialize(new AppSettings { ProjectTools = tools });
        using var json = JsonDocument.Parse(written);
        var names = json.RootElement.GetProperty("projectTools").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("types", names);
        Assert.DoesNotContain(names, n => n is "unrealConfiguration" or "projectFileFormat" or "tellClaudeAboutUnreal" or "unityCodeOptimization" or "tellClaudeAboutUnity" or "godotPath" or "tellClaudeAboutGodot");
        Assert.Equal(UnrealConfiguration.DebugGame, UnrealProvider.DefaultConfiguration(JsonSerializer.Deserialize<AppSettings>(written, JsonFileStore<AppSettings>.Options)!.ProjectTools));
    }
}
