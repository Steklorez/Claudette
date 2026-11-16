using System.Text;
using Deventry.Core.Remote;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Remote;

/// <summary>A client's images and files made ready for a tab (DESIGN.md §18, "Remote clients", "Attachments from a client").</summary>
public class RemoteAttachmentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 14, 32, 5, TimeSpan.FromHours(-5));

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    [Fact]
    public void Images_go_in_the_message_text_inline_a_pdf_saved_and_mentioned_and_a_binary_refused()
    {
        using var folder = new TempFolder();
        RemoteAttachment[] attachments =
        [
            new("shot.png", Png),
            new("notes.txt", Encoding.UTF8.GetBytes("The rope snaps at 40 m.")),
            new("Design Review.pdf", "%PDF-1.7 rest of it"u8.ToArray()),
            new("tool.exe", [0x4D, 0x5A, 0x90, 0, 3, 0]),
        ];

        var prepared = RemoteAttachments.Prepare(attachments, folder.Path, Now, keepFor: TimeSpan.FromDays(31));

        Assert.Equal("image/png", Assert.Single(prepared.Images).MediaType);
        Assert.Equal("Attached file notes.txt:\n\nThe rope snaps at 40 m.", Assert.Single(prepared.Pastes));
        // Named for when it came, with no spaces, so the mention reads as one path.
        Assert.Equal("@.deventry/attachments/20261116-143205-Design-Review.pdf", Assert.Single(prepared.Mentions));
        Assert.True(File.Exists(Path.Combine(folder.Path, ".deventry", "attachments", "20261116-143205-Design-Review.pdf")));
        Assert.Equal("tool.exe: Claude can't read it. Images, text files and PDFs can be sent.", Assert.Single(prepared.Refused));
    }

    [Fact]
    public void The_folder_keeps_itself_out_of_git_and_a_long_log_is_saved_rather_than_put_in_the_message()
    {
        using var folder = new TempFolder();
        var log = Encoding.UTF8.GetBytes(new string('x', (int)RemoteAttachments.InlineTextLimit + 1));

        var prepared = RemoteAttachments.Prepare([new RemoteAttachment("build.log", log), new RemoteAttachment("build.log", log)], folder.Path, Now, null);

        Assert.Empty(prepared.Pastes);
        // Two of a name in the same second don't overwrite each other.
        Assert.Equal(["@.deventry/attachments/20261116-143205-build.log", "@.deventry/attachments/20261116-143205-build-2.log"], prepared.Mentions);
        Assert.Contains("*", File.ReadAllLines(Path.Combine(folder.Path, ".deventry", ".gitignore")));
    }

    [Fact]
    public void Files_older_than_what_Settings_keeps_are_deleted_and_the_rest_left()
    {
        using var folder = new TempFolder();
        var saved = RemoteAttachments.FolderOf(folder.Path);
        Directory.CreateDirectory(saved);
        var old = Path.Combine(saved, "20261001-090000-old.pdf");
        var recent = Path.Combine(saved, "20261115-090000-recent.pdf");
        File.WriteAllText(old, "%PDF-");
        File.WriteAllText(recent, "%PDF-");
        File.SetLastWriteTimeUtc(old, Now.UtcDateTime.AddDays(-40));
        File.SetLastWriteTimeUtc(recent, Now.UtcDateTime.AddDays(-1));

        RemoteAttachments.Prune(folder.Path, TimeSpan.FromDays(31), Now);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        // Forever keeps them all, and a project with none is left alone.
        RemoteAttachments.Prune(folder.Path, null, Now.AddYears(5));
        Assert.True(File.Exists(recent));
        using var empty = new TempFolder();
        RemoteAttachments.Prune(empty.Path, TimeSpan.FromDays(1), Now);
        Assert.False(Directory.Exists(Path.Combine(empty.Path, ".deventry")));
    }
}
