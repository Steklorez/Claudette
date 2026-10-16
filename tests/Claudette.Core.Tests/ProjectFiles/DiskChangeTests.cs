using Claudette.Core.ProjectFiles;

namespace Claudette.Core.Tests.ProjectFiles;

/// <summary>What a change on disk does to an open file (DESIGN.md §18, "Project files", "When a file changes on disk").</summary>
public sealed class DiskChangeTests
{
    [Theory]
    [InlineData("same", "same", false)]
    [InlineData("same", "same", true)]
    [InlineData(null, null, false)]
    [InlineData(null, null, true)]
    public void A_file_still_as_it_was_read_needs_nothing(string? read, string? onDisk, bool hasUnsavedEdits) =>
        Assert.Equal(DiskChangeAction.None, DiskChange.Decide(read, onDisk, hasUnsavedEdits));

    [Theory]
    [InlineData("as read", "changed")]
    // Any difference counts, even only in case or line endings.
    [InlineData("text", "Text")]
    [InlineData("a\nb", "a\r\nb")]
    // It was missing when it was opened, and now it's there.
    [InlineData(null, "created")]
    public void A_changed_file_with_no_edits_to_lose_is_read_again(string? read, string onDisk)
    {
        Assert.Equal(DiskChangeAction.Reload, DiskChange.Decide(read, onDisk, hasUnsavedEdits: false));
        Assert.Equal(DiskChangeAction.AskChanged, DiskChange.Decide(read, onDisk, hasUnsavedEdits: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_deleted_file_always_says_so(bool hasUnsavedEdits) =>
        Assert.Equal(DiskChangeAction.AskDeleted, DiskChange.Decide("as read", null, hasUnsavedEdits));

    [Fact]
    public void An_empty_file_isnt_a_missing_one()
    {
        Assert.Equal(DiskChangeAction.AskDeleted, DiskChange.Decide("", null, hasUnsavedEdits: false));
        Assert.Equal(DiskChangeAction.Reload, DiskChange.Decide(null, "", hasUnsavedEdits: false));
    }
}
