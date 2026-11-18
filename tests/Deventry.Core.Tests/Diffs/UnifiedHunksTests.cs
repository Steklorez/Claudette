using System.Text.Json.Nodes;
using Deventry.Core.Diffs;

namespace Deventry.Core.Tests.Diffs;

/// <summary>A change reported as a unified diff's hunks, as Codex reports one (DESIGN.md §18, "Codex, a second agent").</summary>
public sealed class UnifiedHunksTests
{
    [Fact]
    public void Hunks_become_a_structured_patchs_hunks()
    {
        var patch = UnifiedHunks.ToStructuredPatch("@@ -1,2 +1,3 @@\n # Title\n-old line\n+new line\n+another\n@@ -10 +11 @@\n-x\n+y\n");

        Assert.Equal(2, patch.Count);
        var first = Assert.IsType<JsonObject>(patch[0]);
        Assert.Equal(1, first["oldStart"]!.GetValue<int>());
        Assert.Equal(2, first["oldLines"]!.GetValue<int>());
        Assert.Equal(1, first["newStart"]!.GetValue<int>());
        Assert.Equal(3, first["newLines"]!.GetValue<int>());
        Assert.Equal([" # Title", "-old line", "+new line", "+another"], first["lines"]!.AsArray().Select(l => l!.GetValue<string>()));
        // A count left out is one.
        var second = Assert.IsType<JsonObject>(patch[1]);
        Assert.Equal(1, second["oldLines"]!.GetValue<int>());
        Assert.Equal(1, second["newLines"]!.GetValue<int>());
    }

    [Fact]
    public void File_headers_and_the_no_newline_marker_are_passed_over()
    {
        var patch = UnifiedHunks.ToStructuredPatch("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+b\n");

        var hunk = Assert.IsType<JsonObject>(Assert.Single(patch));
        Assert.Equal(["-a", "+b"], hunk["lines"]!.AsArray().Select(l => l!.GetValue<string>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("written by the mock\n")]
    public void Text_without_hunks_has_none(string? diff) => Assert.Empty(UnifiedHunks.ToStructuredPatch(diff));

    [Fact]
    public void The_text_before_is_the_change_taken_back()
    {
        var after = "# Spike repo, edited\nsecond\n";

        Assert.Equal("# Spike repo\nsecond\n", UnifiedHunks.Before(after, "@@ -1 +1 @@\n-# Spike repo\n+# Spike repo, edited\n"));
    }

    [Fact]
    public void Hunks_that_dont_fit_the_text_give_nothing() =>
        Assert.Null(UnifiedHunks.Before("something else\n", "@@ -1 +1 @@\n-old\n+new\n"));
}
