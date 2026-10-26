using Claudette.Core.ProjectFiles;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>
/// Renaming and deleting from the explorer (DESIGN.md §18, "Project files", "Renaming and deleting"): the new name
/// checked as it's typed, the rename, and what deleting needs besides the trash.
/// </summary>
public sealed class RenameEntryTests : IDisposable
{
    private readonly TempFolder _temp = new("claudette-rename-entry");

    public void Dispose() => _temp.Dispose();

    private string Project => _temp.Combine("project");

    private string Write(string relative, string text = "") => _temp.Write($"project/{relative}", text);

    [Fact]
    public void A_new_name_is_its_path_in_the_same_folder_and_one_left_as_it_was_or_empty_does_nothing()
    {
        var brain = Write("AI/Brain.cpp");

        Assert.Equal(new NewEntryName(Path.Combine(Project, "AI", "Mind.cpp"), null), RenameEntry.Check(brain, "  Mind.cpp "));
        Assert.Equal(new NewEntryName(null, null), RenameEntry.Check(brain, "Brain.cpp"));
        Assert.Equal(new NewEntryName(null, null), RenameEntry.Check(brain, " "));
    }

    [Fact]
    public void A_name_with_a_slash_or_taken_or_one_the_system_refuses_is_refused()
    {
        var brain = Write("AI/Brain.cpp");
        Write("AI/Mind.cpp");
        Directory.CreateDirectory(Path.Combine(Project, "AI", "Parts"));

        Assert.Equal("A name can't have / in it.", RenameEntry.Check(brain, "Parts/Brain.cpp").Problem);
        Assert.Equal("A name can't have \\ in it.", RenameEntry.Check(brain, "Parts\\Brain.cpp", windows: true).Problem);
        Assert.Equal("There's already a file called Mind.cpp here.", RenameEntry.Check(brain, "Mind.cpp").Problem);
        Assert.Equal("There's already a folder called Parts here.", RenameEntry.Check(brain, "Parts").Problem);
        Assert.Equal("A name can't be \".\" or \"..\".", RenameEntry.Check(brain, "..").Problem);
        Assert.Equal("CON is a name Windows keeps for itself.", RenameEntry.Check(brain, "CON", windows: true).Problem);
    }

    [Fact]
    public void Renaming_moves_it_in_its_folder_never_over_another()
    {
        var brain = Write("AI/Brain.cpp", "think");
        var mind = Path.Combine(Project, "AI", "Mind.cpp");

        Assert.Null(RenameEntry.Rename(brain, mind));

        Assert.False(File.Exists(brain));
        Assert.Equal("think", File.ReadAllText(mind));

        var other = Write("AI/Other.cpp", "other");
        Assert.StartsWith("Claudette couldn't rename it.", RenameEntry.Rename(mind, other));
        Assert.Equal("other", File.ReadAllText(other));
        Assert.Equal("think", File.ReadAllText(mind));
    }

    [Fact]
    public void A_folder_is_renamed_with_everything_in_it()
    {
        Write("AI/Parts/Eye.cpp", "see");

        Assert.Null(RenameEntry.Rename(Path.Combine(Project, "AI"), Path.Combine(Project, "Brain")));

        Assert.Equal("see", File.ReadAllText(Path.Combine(Project, "Brain", "Parts", "Eye.cpp")));
        Assert.False(Directory.Exists(Path.Combine(Project, "AI")));
    }

    [Fact]
    public void Only_its_case_changing_renames_it_where_a_disk_ignores_case()
    {
        var brain = Write("AI/brain.cpp", "think");

        var check = RenameEntry.Check(brain, "Brain.cpp");

        Assert.Equal(new NewEntryName(Path.Combine(Project, "AI", "Brain.cpp"), null), check);
        Assert.Null(RenameEntry.Rename(brain, check.Path!));
        Assert.Equal(["Brain.cpp"], Directory.GetFiles(Path.Combine(Project, "AI")).Select(Path.GetFileName));
        Assert.Equal("think", File.ReadAllText(check.Path!));
    }

    [Fact]
    public void Another_entry_named_but_for_its_case_is_taken_where_a_disk_heeds_case()
    {
        var lower = Write("AI/brain.cpp");
        // Only where the disk keeps both: Linux's, and some Macs'.
        Assert.SkipWhen(File.Exists(Path.Combine(Project, "AI", "BRAIN.CPP")), "This disk ignores case.");
        Write("AI/Brain.cpp");

        Assert.Equal("There's already a file called Brain.cpp here.", RenameEntry.Check(lower, "Brain.cpp").Problem);
    }

    [Fact]
    public void Deleting_counts_a_folders_files_hidden_too_up_to_a_limit()
    {
        Write("AI/Brain.cpp");
        Write("AI/Parts/Eye.cpp");
        var hidden = Write("AI/.cache");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(Project, "AI", "Empty"));

        Assert.Equal(3, DeleteEntry.CountFiles(Path.Combine(Project, "AI"), 100));
        Assert.Equal(2, DeleteEntry.CountFiles(Path.Combine(Project, "AI"), 2));
        Assert.Equal(0, DeleteEntry.CountFiles(Path.Combine(Project, "AI", "Empty"), 100));
    }

    [Fact]
    public void Deleting_for_good_takes_a_file_or_a_folder_read_only_files_too()
    {
        var file = Write("Notes.txt");
        var locked = Write("AI/Parts/Locked.cpp");
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        Assert.Null(DeleteEntry.DeletePermanently(file));
        Assert.Null(DeleteEntry.DeletePermanently(Path.Combine(Project, "AI")));

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.Combine(Project, "AI")));
        Assert.StartsWith("Claudette couldn't delete it.", DeleteEntry.DeletePermanently(file));
    }

    [Fact]
    public void Deleting_a_link_for_good_leaves_what_it_leads_to()
    {
        var target = Write("Real/Brain.cpp", "think");
        var link = Path.Combine(Project, "Link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetDirectoryName(target)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows makes links only in developer mode, or for an administrator.
            Assert.Skip($"This system won't make a link here: {ex.Message}");
        }

        Assert.Null(DeleteEntry.DeletePermanently(link));

        Assert.False(Directory.Exists(link));
        Assert.Equal("think", File.ReadAllText(target));
    }
}
