using System.Text.Json.Nodes;
using Deventry.Core.FromClaudette;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.FromClaudette;

/// <summary>
/// Claudette's folders becoming Deventry's the first time it starts (DESIGN.md §2, "Renaming to Deventry", "What moves
/// the first time Deventry starts").
/// </summary>
public sealed class ClaudetteFoldersTests : IDisposable
{
    private readonly TempFolder _temp = new();

    private AppPaths Claudettes => new(_temp.Combine("Local", "Claudette"), _temp.Combine("Roaming", "Claudette"));

    private AppPaths Deventrys => new(_temp.Combine("Local", "Deventry"), _temp.Combine("Roaming", "Deventry"));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Claudettes_folders_become_Deventrys_with_the_paths_saved_in_them_pointing_into_their_new_places()
    {
        var transcript = Path.Combine(Claudettes.LocalSessionsDirectory, "abc.jsonl");
        Write(Claudettes.StateFile, new JsonObject
        {
            ["tabs"] = new JsonArray(new JsonObject { ["transcriptPath"] = transcript, ["folder"] = _temp.Combine("Local", "Claudette2", "work") }),
        });
        Write(Claudettes.SettingsFile, new JsonObject { ["sessions"] = new JsonObject { ["libraryFolder"] = Claudettes.DefaultLibraryDirectory } });
        File.WriteAllText(Path.Combine(Claudettes.LocalSessionsDirectory, "abc.jsonl"), $"{{\"note\":\"{Claudettes.DataDirectory.Replace("\\", "\\\\")}\"}}");

        Assert.True(ClaudetteFolders.Move(Deventrys, Claudettes));

        Assert.False(Directory.Exists(Claudettes.DataDirectory));
        Assert.False(Directory.Exists(Claudettes.SettingsDirectory));
        var tab = Read(Deventrys.StateFile)["tabs"]![0]!;
        Assert.Equal(Path.Combine(Deventrys.LocalSessionsDirectory, "abc.jsonl"), (string?)tab["transcriptPath"]);
        // A folder whose name only starts with Claudette's isn't Claudette's.
        Assert.Equal(_temp.Combine("Local", "Claudette2", "work"), (string?)tab["folder"]);
        Assert.Equal(Deventrys.DefaultLibraryDirectory, (string?)Read(Deventrys.SettingsFile)["sessions"]!["libraryFolder"]);
        // Only the JSON files at the top are rewritten: a transcript is what Claude wrote.
        Assert.Contains("Claudette", File.ReadAllText(Path.Combine(Deventrys.LocalSessionsDirectory, "abc.jsonl")), StringComparison.Ordinal);
        Assert.Equal(Deventrys, ClaudetteFolders.WhereTheyAre(Deventrys, Claudettes));
    }

    [Fact]
    public void While_Claudette_runs_its_folders_stay_and_Deventry_uses_them_where_they_are()
    {
        Write(Claudettes.StateFile, new JsonObject());
        Write(Claudettes.SettingsFile, new JsonObject());
        using (new FileStream(Path.Combine(Claudettes.DataDirectory, ClaudetteFolders.ClaimFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(ClaudetteFolders.Move(Deventrys, Claudettes));

            Assert.True(File.Exists(Claudettes.StateFile));
            Assert.False(Directory.Exists(Deventrys.DataDirectory));
            Assert.Equal(Claudettes with { TempDirectory = null }, ClaudetteFolders.WhereTheyAre(Deventrys, Claudettes));
        }

        // Once it has let go, they move.
        Assert.True(ClaudetteFolders.Move(Deventrys, Claudettes));
        Assert.True(File.Exists(Deventrys.StateFile));
        Assert.True(File.Exists(Deventrys.SettingsFile));
    }

    [Fact]
    public void Nothing_moves_over_Deventrys_own_folders_and_a_folder_an_old_update_left_goes()
    {
        Write(Deventrys.StateFile, new JsonObject { ["mine"] = true });
        Write(Claudettes.SettingsFile, new JsonObject { ["theirs"] = true });
        Write(Deventrys.SettingsFile, new JsonObject { ["mine"] = true });
        Directory.CreateDirectory(Claudettes.DataDirectory);
        File.WriteAllText(Path.Combine(Claudettes.DataDirectory, ClaudetteFolders.ReadyFileName), "nonce");

        Assert.False(ClaudetteFolders.Move(Deventrys, Claudettes));

        Assert.True((bool)Read(Deventrys.StateFile)["mine"]!);
        Assert.True((bool)Read(Deventrys.SettingsFile)["mine"]!);
        // Only the nonce an update from a Claudette before 0.7.0 waited for was left in its data folder.
        Assert.False(Directory.Exists(Claudettes.DataDirectory));
        // Its settings folder has more in it, so it stays.
        Assert.True(File.Exists(Claudettes.SettingsFile));
    }

    [Fact]
    public void Data_and_settings_in_one_folder_move_once()
    {
        var claudettes = new AppPaths(_temp.Combine("Support", "Claudette"), _temp.Combine("Support", "Claudette"));
        var Deventrys = new AppPaths(_temp.Combine("Support", "Deventry"), _temp.Combine("Support", "Deventry"));
        Write(claudettes.SettingsFile, new JsonObject { ["path"] = claudettes.StateFile });

        Assert.True(ClaudetteFolders.Move(Deventrys, claudettes));

        Assert.Equal(Deventrys.StateFile, (string?)Read(Deventrys.SettingsFile)["path"]);
    }

    [Fact]
    public void Without_Claudettes_folders_nothing_happens()
    {
        Assert.False(ClaudetteFolders.Move(Deventrys, Claudettes));

        Assert.False(Directory.Exists(Deventrys.DataDirectory));
        Assert.Equal(Deventrys, ClaudetteFolders.WhereTheyAre(Deventrys, Claudettes));
    }

    private static void Write(string file, JsonNode json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (Path.GetFileName(file) == "state.json")
        {
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(file)!, "sessions"));
        }
        File.WriteAllText(file, json.ToJsonString());
    }

    private static JsonNode Read(string file) => JsonNode.Parse(File.ReadAllText(file))!;
}
