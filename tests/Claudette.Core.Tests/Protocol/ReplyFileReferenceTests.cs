using Claudette.Core.Protocol;

namespace Claudette.Core.Tests.Protocol;

/// <summary>Which inline code and link addresses in a reply name a file, and which are programs (DESIGN.md §18, "Files named in Claude's replies").</summary>
public sealed class ReplyFileReferenceTests
{
    [Theory]
    [InlineData("weapons.md", "weapons.md", null)]
    [InlineData("docs/specs/weapons.md", "docs/specs/weapons.md", null)]
    [InlineData(@"docs\check.bat", @"docs\check.bat", null)]
    [InlineData("Player.cpp:42", "Player.cpp", 42)]
    [InlineData("src/Player.cpp:42:7", "src/Player.cpp", 42)]
    [InlineData("Player.cpp:42-51", "Player.cpp", 42)]
    [InlineData("Player.cpp#L42-L51", "Player.cpp", 42)]
    [InlineData(".gitignore", ".gitignore", null)]
    [InlineData("src/utils/", "src/utils/", null)]
    [InlineData("~/.claude/settings.json", "~/.claude/settings.json", null)]
    [InlineData(@"C:\Work\Game\Config\DefaultGame.ini", @"C:\Work\Game\Config\DefaultGame.ini", null)]
    [InlineData("  weapons.md ", "weapons.md", null)]
    public void Inline_code_that_names_a_file_is_a_reference(string code, string path, int? line)
    {
        var reference = ReplyFileReference.FromCode(code);

        Assert.NotNull(reference);
        Assert.Equal((code, path, line), (reference.Text, reference.Path, reference.Line));
    }

    [Theory]
    [InlineData("CanAttack")]
    [InlineData("Foo.Bar()")]
    [InlineData("e.g.")]
    [InlineData("git add weapons.md")]
    [InlineData("1.5")]
    [InlineData("List<string>")]
    [InlineData("a | b")]
    [InlineData("https://example.com/weapons.md")]
    [InlineData("javascript:alert(1)")]
    [InlineData("weapons.md:not-a-line")]
    [InlineData(@"\\server\share\weapons.md")]
    [InlineData("//server/share/weapons.md")]
    [InlineData("// TODO: weapons.md")]
    [InlineData("")]
    [InlineData("line one\nweapons.md")]
    public void Other_code_isnt(string code) => Assert.Null(ReplyFileReference.FromCode(code));

    [Theory]
    [InlineData("docs/specs/weapons.md", "docs/specs/weapons.md", null)]
    [InlineData("docs/specs/weapons.md#L42", "docs/specs/weapons.md", 42)]
    [InlineData("docs/specs/weapons.md#guns", "docs/specs/weapons.md", null)]
    [InlineData("my%20notes.md", "my notes.md", null)]
    [InlineData("C%23%20notes.md", "C# notes.md", null)]
    [InlineData("src/Player.cpp:42", "src/Player.cpp", 42)]
    [InlineData("README", "README", null)]
    public void A_links_address_to_a_file_is_a_reference(string address, string path, int? line)
    {
        var reference = ReplyFileReference.FromLink(address);

        Assert.NotNull(reference);
        Assert.Equal((path, line), (reference.Path, reference.Line));
    }

    [Fact]
    public void A_file_address_on_this_computer_is_its_path()
    {
        var path = OperatingSystem.IsWindows() ? @"C:\Work\weapons.md" : "/work/weapons.md";

        Assert.Equal(path, ReplyFileReference.FromLink(new Uri(path).AbsoluteUri)?.Path);
    }

    [Theory]
    [InlineData("vscode://file/C:/work/weapons.md")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file://server/share/weapons.md")]
    [InlineData(@"\\server\share\weapons.md")]
    [InlineData("%5C%5Cserver%5Cshare%5Cweapons.md")]
    [InlineData("")]
    public void A_links_address_elsewhere_isnt(string address) => Assert.Null(ReplyFileReference.FromLink(address));

    [Theory]
    [InlineData("Player.cpp:42:7", "Player.cpp", 42, 7, null)]
    [InlineData(@"C:\Work\Player.cpp:42:7", @"C:\Work\Player.cpp", 42, 7, null)]
    [InlineData("Player.cpp:42-50", "Player.cpp", 42, null, 50)]
    [InlineData("Player.cpp#L42C7", "Player.cpp", 42, 7, null)]
    [InlineData("Player.cpp#L42-L50", "Player.cpp", 42, null, 50)]
    [InlineData("Player.cpp#L42-50", "Player.cpp", 42, null, 50)]
    // A range's end column isn't kept: the range selects whole lines.
    [InlineData("Player.cpp#L42C7-L50C3", "Player.cpp", 42, 7, 50)]
    // A range that doesn't end after it starts is only its first line.
    [InlineData("Player.cpp:50-42", "Player.cpp", 50, null, null)]
    [InlineData("Player.cpp:42-42", "Player.cpp", 42, null, null)]
    // Lines and columns count from 1.
    [InlineData("Player.cpp:42:0", "Player.cpp", 42, null, null)]
    [InlineData("Player.cpp:0", "Player.cpp", null, null, null)]
    public void Inline_code_can_name_a_column_or_a_range_of_lines(string code, string path, int? line, int? column, int? endLine)
    {
        var reference = ReplyFileReference.FromCode(code);

        Assert.NotNull(reference);
        Assert.Equal((path, line, column, endLine), (reference.Path, reference.Line, reference.Column, reference.EndLine));
    }

    [Theory]
    [InlineData("src/Player.cpp#L42C7", "src/Player.cpp", 42, 7, null)]
    [InlineData("docs/specs/weapons.md#L10-L20", "docs/specs/weapons.md", 10, null, 20)]
    [InlineData("src/Player.cpp:42:7", "src/Player.cpp", 42, 7, null)]
    [InlineData("src/Player.cpp:42-50", "src/Player.cpp", 42, null, 50)]
    [InlineData("docs/specs/weapons.md#guns", "docs/specs/weapons.md", null, null, null)]
    public void A_links_address_can_name_a_column_or_a_range_of_lines(string address, string path, int? line, int? column, int? endLine)
    {
        var reference = ReplyFileReference.FromLink(address);

        Assert.NotNull(reference);
        Assert.Equal((path, line, column, endLine), (reference.Path, reference.Line, reference.Column, reference.EndLine));
    }

    [Fact]
    public void A_file_named_without_a_line_has_no_column_or_range()
    {
        var reference = ReplyFileReference.FromCode("weapons.md");

        Assert.NotNull(reference);
        Assert.Null(reference.Line);
        Assert.Null(reference.Column);
        Assert.Null(reference.EndLine);
    }

    [Fact]
    public void Inline_code_reads_back_from_its_escaped_link_as_it_was()
    {
        foreach (var code in new[] { "weapons.md:42", "Player.cpp#L42", "100%.md", @"C:\Work\a b.md", "docs/specs/weapons.md", "Player.cpp:42:7", "Player.cpp#L42-L50" })
        {
            var reference = ReplyFileReference.FromCode(code)!;
            var back = ReplyFileReference.FromLink(new Uri(Uri.EscapeDataString(code), UriKind.Relative).OriginalString);

            Assert.Equal((reference.Path, reference.Line, reference.Column, reference.EndLine), (back?.Path, back?.Line, back?.Column, back?.EndLine));
        }
    }

    [Theory]
    [InlineData("build.bat", false, true)]
    [InlineData(@"C:\Tools\setup.EXE", false, true)]
    [InlineData("Shortcut.lnk", false, true)]
    [InlineData("deploy.sh", false, true)]
    [InlineData("tools/run.py", false, true)]
    [InlineData("Claudette.app/", false, true)]
    [InlineData("weapons.md", false, false)]
    [InlineData("Player.cpp", true, false)]
    [InlineData("bin/tool", true, true)]
    [InlineData("LICENSE", false, false)]
    public void A_program_script_or_shortcut_is_shown_rather_than_opened(string path, bool isExecutable, bool isProgram) =>
        Assert.Equal(isProgram, ReplyFileReference.IsProgram(path, isExecutable));
}
