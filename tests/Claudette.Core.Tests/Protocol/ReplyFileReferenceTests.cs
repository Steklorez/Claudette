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

    [Fact]
    public void Inline_code_reads_back_from_its_escaped_link_as_it_was()
    {
        foreach (var code in new[] { "weapons.md:42", "Player.cpp#L42", "100%.md", @"C:\Work\a b.md", "docs/specs/weapons.md" })
        {
            var reference = ReplyFileReference.FromCode(code)!;
            var back = ReplyFileReference.FromLink(new Uri(Uri.EscapeDataString(code), UriKind.Relative).OriginalString);

            Assert.Equal((reference.Path, reference.Line), (back?.Path, back?.Line));
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
