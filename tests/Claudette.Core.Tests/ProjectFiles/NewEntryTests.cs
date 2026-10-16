using Claudette.Core.ProjectFiles;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>A new file or folder made from the explorer (DESIGN.md §18, "Project files", "New files and folders").</summary>
public sealed class NewEntryTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-new-entry");

    public void Dispose() => _temp.Dispose();

    private string Project => _temp.Combine("project");

    private string Write(string relative, string text = "") => _temp.Write($"project/{relative}", text);

    [Fact]
    public void A_name_makes_its_path_in_the_folder_and_slashes_make_the_folders_on_the_way()
    {
        Directory.CreateDirectory(Project);

        Assert.Equal(new NewEntryName(Path.Combine(Project, "Brain.cpp"), null), NewEntry.Check(Project, "  Brain.cpp "));
        Assert.Equal(Path.Combine(Project, "AI", "Brain.cpp"), NewEntry.Check(Project, "AI/Brain.cpp").Path);
        // Slashes at the ends, or two together, are passed over.
        Assert.Equal(Path.Combine(Project, "AI", "Brain.cpp"), NewEntry.Check(Project, "/AI//Brain.cpp/").Path);
    }

    [Fact]
    public void Nothing_typed_makes_nothing_and_says_nothing()
    {
        Assert.Equal(new NewEntryName(null, null), NewEntry.Check(Project, "  "));
        Assert.Equal(new NewEntryName(null, null), NewEntry.Check(Project, "/"));
    }

    [Fact]
    public void A_name_already_there_is_refused_saying_whether_its_a_file_or_a_folder()
    {
        Write("Source/Player.cpp");

        Assert.Equal("There's already a file called Player.cpp here.", NewEntry.Check(Path.Combine(Project, "Source"), "Player.cpp").Problem);
        Assert.Equal("There's already a folder called Source here.", NewEntry.Check(Project, "Source").Problem);
        Assert.Equal("There's already a file called Source/Player.cpp here.", NewEntry.Check(Project, "Source/Player.cpp").Problem);
        Assert.Equal("Source/Player.cpp is a file, so nothing can go in it.", NewEntry.Check(Project, "Source/Player.cpp/Dash.cpp").Problem);
    }

    [Fact]
    public void A_name_leading_out_of_the_folder_is_refused()
    {
        Assert.Equal("A name can't be . or ..", NewEntry.Check(Project, "../outside.txt").Problem);
        Assert.Equal("A name can't be . or ..", NewEntry.Check(Project, "./here.txt").Problem);
    }

    [Fact]
    public void Control_characters_are_refused_everywhere()
    {
        Assert.Equal("A name can't have a tab, a line break or another control character in it.", NewEntry.Check(Project, "a\tb", windows: false).Problem);
    }

    [Fact]
    public void Windows_refuses_its_own_characters_endings_and_device_names()
    {
        Assert.Equal("A name can't have ? in it.", NewEntry.Check(Project, "what?.txt", windows: true).Problem);
        Assert.Equal("A name can't have : in it.", NewEntry.Check(Project, "C:/x.txt", windows: true).Problem);
        Assert.Equal("A name can't end with a space or a dot.", NewEntry.Check(Project, "notes.", windows: true).Problem);
        Assert.Equal("A name can't end with a space or a dot.", NewEntry.Check(Project, "AI /Brain.cpp", windows: true).Problem);
        Assert.Equal("nul.txt is a name Windows keeps for itself.", NewEntry.Check(Project, "nul.txt", windows: true).Problem);
        Assert.Equal("COM1 is a name Windows keeps for itself.", NewEntry.Check(Project, "COM1", windows: true).Problem);
        // A backslash is a slash there.
        Assert.Equal(Path.Combine(Project, "AI", "Brain.cpp"), NewEntry.Check(Project, @"AI\Brain.cpp", windows: true).Path);
        // Names that only start like a device's are fine.
        Assert.Null(NewEntry.Check(Project, "console.txt", windows: true).Problem);
    }

    [Fact]
    public void Elsewhere_those_characters_are_names_like_any_other()
    {
        Assert.Null(NewEntry.Check(Project, "what?.txt", windows: false).Problem);
        Assert.Null(NewEntry.Check(Project, "notes.", windows: false).Problem);
    }

    [Fact]
    public void Make_makes_an_empty_file_or_a_folder_with_the_folders_on_its_way()
    {
        var file = Path.Combine(Project, "AI", "Brain.cpp");
        var folder = Path.Combine(Project, "Art", "Icons");

        Assert.Null(NewEntry.Make(file, isFolder: false));
        Assert.Null(NewEntry.Make(folder, isFolder: true));

        Assert.Equal(0, new FileInfo(file).Length);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Make_never_writes_over_a_file_that_appeared_since_the_name_was_checked()
    {
        var path = Write("Brain.cpp", "kept");

        var problem = NewEntry.Make(path, isFolder: false);

        Assert.StartsWith("Claudette couldn't make it. ", problem);
        Assert.Equal("kept", File.ReadAllText(path));
    }
}
