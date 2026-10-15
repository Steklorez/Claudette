using System.Globalization;
using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Tests.Protocol;

/// <summary>What a tool's <c>tool_use_result</c> says for its tile (DESIGN.md §18, "Conversation cards").</summary>
public sealed class ToolOutputTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public ToolOutputTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void A_Read_keeps_the_file_as_it_read_it_and_counts_its_lines()
    {
        // As 2.1.284 sends it (Fixtures/protocol/2.1.284/06-edit-original-file.jsonl).
        var output = ToolOutput.From("Read", JsonNode.Parse("""{"type":"text","file":{"filePath":"e.txt","content":"one\ntwo","numLines":2,"startLine":41,"totalLines":900}}"""))!;

        Assert.Equal("one\ntwo", output.FileContent);
        Assert.Equal(41, output.StartLine);
        Assert.Equal("2 lines of 900", output.Facts);
    }

    [Fact]
    public void A_whole_file_says_only_its_lines_and_an_image_has_no_text()
    {
        Assert.Equal("1 line", ToolOutput.From("Read", JsonNode.Parse("""{"type":"text","file":{"content":"x","numLines":1,"startLine":1,"totalLines":1}}"""))!.Facts);
        Assert.Null(ToolOutput.From("Read", JsonNode.Parse("""{"type":"image","file":{"base64":"…"}}""")));
    }

    [Theory]
    [InlineData("""{"mode":"files_with_matches","numFiles":3,"filenames":["a.cs","b.cs","c.cs"]}""", "3 files")]
    [InlineData("""{"mode":"content","numFiles":2,"filenames":[],"content":"a.cs:1:x","numLines":12}""", "12 lines")]
    [InlineData("""{"mode":"count","numFiles":2,"filenames":[],"numMatches":7}""", "7 matches")]
    [InlineData("""{"numFiles":1,"filenames":["a.cs"]}""", "1 file")]
    public void A_Grep_says_what_it_found_by_its_mode(string result, string facts) =>
        Assert.Equal(facts, ToolOutput.From("Grep", JsonNode.Parse(result))!.Facts);

    [Fact]
    public void A_Glob_lists_its_files_and_says_when_it_cut_the_list_short()
    {
        var output = ToolOutput.From("Glob", JsonNode.Parse("""{"durationMs":4,"numFiles":100,"filenames":["a.cs","b.cs"],"truncated":true}"""))!;

        Assert.Equal(["a.cs", "b.cs"], output.Filenames);
        Assert.Equal("100 files+", output.Facts);
    }

    [Fact]
    public void A_fetch_says_its_status_and_size_and_a_Write_whether_it_made_the_file()
    {
        Assert.Equal("200 OK · 14.2 KB", ToolOutput.From("WebFetch", JsonNode.Parse("""{"bytes":14540,"code":200,"codeText":"OK","result":"…","durationMs":90,"url":"https://example.com"}"""))!.Facts);
        Assert.Equal("404", ToolOutput.From("WebFetch", JsonNode.Parse("""{"code":404}"""))!.Facts);
        Assert.Equal("Created", ToolOutput.From("Write", JsonNode.Parse("""{"type":"create","filePath":"a.txt","content":"x"}"""))!.Facts);
        Assert.Equal("Updated", ToolOutput.From("Write", JsonNode.Parse("""{"type":"update","filePath":"a.txt","content":"x"}"""))!.Facts);
    }

    [Fact]
    public void Fields_of_another_shape_are_left_out_rather_than_failing()
    {
        // CLAUDE.md, "Parse tolerantly".
        var output = ToolOutput.From("Grep", JsonNode.Parse("""{"numFiles":"three","filenames":"a.cs"}"""))!;

        Assert.Null(output.Files);
        Assert.Empty(output.Filenames);
        Assert.Null(output.Facts);
        Assert.Null(ToolOutput.From("Read", JsonNode.Parse("[1,2]")));
        Assert.Null(ToolOutput.From("Bash", JsonNode.Parse("""{"stdout":"x"}""")));
        Assert.Null(ToolOutput.From("Read", null));
    }
}
