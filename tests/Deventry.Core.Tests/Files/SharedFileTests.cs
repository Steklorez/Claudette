using Deventry.Core.Files;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Files;

/// <summary>Reading Claude Code's files without getting in the way of its own writes, which Windows otherwise refuses.</summary>
public sealed class SharedFileTests : IDisposable
{
    private readonly TempFolder _folder = new("deventry-shared");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_file_being_read_can_still_be_written_and_deleted()
    {
        var path = _folder.Write("tasks/s1/1.json", """{"status":"pending"}""");

        // Claude Code writing the file as Deventry reads it.
        using (SharedFile.OpenRead(path))
        {
            File.WriteAllText(path, """{"status":"completed"}""");
        }
        Assert.Equal("""{"status":"completed"}""", SharedFile.ReadAllText(path));

        // And deleting it, gone once the read is done.
        using (SharedFile.OpenRead(path))
        {
            File.Delete(path);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Its_text_is_read_as_File_reads_it_with_or_without_a_byte_order_mark()
    {
        var plain = _folder.Write("plain.json", "{\"name\":\"Café\"}");
        var marked = _folder.Combine("marked.json");
        File.WriteAllText(marked, "{\"name\":\"Café\"}", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(File.ReadAllText(plain), SharedFile.ReadAllText(plain));
        Assert.Equal("{\"name\":\"Café\"}", SharedFile.ReadAllText(marked));
    }
}
