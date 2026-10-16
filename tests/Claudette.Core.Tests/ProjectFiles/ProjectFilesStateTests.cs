using Claudette.Core.ProjectFiles;
using Claudette.Core.Settings;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>A Project Files tab as the app's state keeps it (DESIGN.md §18, "Project files").</summary>
public sealed class ProjectFilesStateTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-project-files-state");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_files_tab_round_trips_with_its_open_files()
    {
        var store = new JsonFileStore<AppState>(_temp.Combine("state.json"));
        var state = new ProjectFilesState
        {
            Folder = "/work/game",
            Files =
            [
                new OpenFileState { Path = "/work/game/Player.cpp", IsPinned = true, Line = 42, Column = 7, Scroll = 812.5, Mode = FileViewMode.SideBySide },
                new OpenFileState { Path = "/work/game/README.md", IsPreview = true },
            ],
            ShowingPath = "/work/game/Player.cpp",
            ExpandedFolders = ["Source", "Source/Game"],
            ShowOnlyChanged = true,
            AgainstGitHead = true,
        };

        await store.SaveAsync(new AppState { ProjectFiles = [state] }, TestContext.Current.CancellationToken);
        var loaded = Assert.Single(store.Load().ProjectFiles);

        Assert.Equal((state.Id, "/work/game", "/work/game/Player.cpp", true, true), (loaded.Id, loaded.Folder, loaded.ShowingPath, loaded.ShowOnlyChanged, loaded.AgainstGitHead));
        Assert.Equal(["Source", "Source/Game"], loaded.ExpandedFolders);
        Assert.Equal(
            [("/work/game/Player.cpp", true, false, 42, 7, 812.5, FileViewMode.SideBySide), ("/work/game/README.md", false, true, 1, 1, 0.0, FileViewMode.Edit)],
            loaded.Files.Select(f => (f.Path, f.IsPinned, f.IsPreview, f.Line, f.Column, f.Scroll, f.Mode)));
        Assert.Contains("\"mode\": \"sideBySide\"", await File.ReadAllTextAsync(store.Path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public void State_from_before_project_files_has_none()
    {
        var path = _temp.Write("state.json", """{ "tabs": [] }""");

        Assert.Empty(new JsonFileStore<AppState>(path).Load().ProjectFiles);
    }

    [Fact]
    public void Each_new_files_tab_has_its_own_id()
    {
        Assert.NotEqual(new ProjectFilesState().Id, new ProjectFilesState().Id);
    }
}
