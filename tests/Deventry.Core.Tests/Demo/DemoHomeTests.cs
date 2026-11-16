using System.Globalization;
using System.Text.RegularExpressions;
using Deventry.Core.Demo;
using Deventry.Core.Settings;

namespace Deventry.Core.Tests.Demo;

/// <summary>
/// The demo's folder (DESIGN.md §11, "Demo mode"): Deventry's settings and tabs, the transcripts, and in the app's demo
/// the projects' folders, empty.
/// </summary>
public sealed class DemoHomeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("deventry-demo-home").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task The_apps_demo_has_its_tabs_and_transcripts_and_empty_project_folders()
    {
        var now = new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.FromHours(-4));
        var projects = Path.Combine(_root, "projects");

        var written = await DemoHome.WriteAsync(_root, projects, now, new DemoOptions { ClaudePath = "deventry-itself", Style = AppStyle.Claude }, DemoContent.EmptyFolder);

        var settings = new JsonFileStore<AppSettings>(written.Paths.SettingsFile).Load();
        Assert.Equal(("deventry-itself", AppStyle.Claude), (settings.ClaudeCode.ClaudePath, settings.Appearance.Style));
        // Nothing that would reach the network.
        Assert.False(settings.General.CheckForAppUpdates || settings.General.ShowServiceStatus || settings.ClaudeCode.CheckForUpdates);
        var state = new JsonFileStore<AppState>(written.Paths.StateFile).Load();
        Assert.Equal(7, state.Tabs.Count);
        Assert.Equal("tab-grapple", state.SelectedTabId);
        Assert.All(state.Tabs, t => Assert.True(File.Exists(t.TranscriptPath), t.TranscriptPath));
        // Earlier the same day.
        Assert.All(state.Tabs, t => Assert.Contains("\"timestamp\":\"2026-10-30T", File.ReadAllText(t.TranscriptPath!), StringComparison.Ordinal));
        // The projects' folders, with nothing in them.
        foreach (var project in new[] { "starfall", "tidepool", "orbit-game" })
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(projects, project)));
        }
        Assert.Equal(Path.Combine(_root, "claude-config"), written.ClaudeConfig);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(written.ClaudeConfig, "projects", "demo"), "*.jsonl"));
    }

    [Theory]
    [InlineData(8, 30)]
    [InlineData(15, 0)]
    public async Task Its_conversations_end_over_an_hour_before_its_made_whatever_the_time(int hour, int minute)
    {
        var now = new DateTimeOffset(2026, 10, 30, hour, minute, 0, TimeSpan.FromHours(-4));

        var written = await DemoHome.WriteAsync(_root, Path.Combine(_root, "projects"), now, new DemoOptions { ClaudePath = "deventry-itself" }, DemoContent.EmptyFolder);

        var state = new JsonFileStore<AppState>(written.Paths.StateFile).Load();
        var times = state.Tabs
            .SelectMany(t => Regex.Matches(File.ReadAllText(t.TranscriptPath!), "\"timestamp\":\"([^\"]+)\"").Select(m => DateTimeOffset.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)))
            .ToList();
        // Past the prompt cache's hour, so its chip says it expired, and that morning, so the cards show only times.
        Assert.True(times.Max() <= now.AddMinutes(-61), $"The last turn ended at {times.Max():O}, made at {now:O}.");
        Assert.All(times, t => Assert.Equal(now.Date, t.ToOffset(now.Offset).Date));
    }
}
