namespace Deventry.Core.Tests;

/// <summary>Times, durations, sizes and names written the same everywhere they show.</summary>
public class FormatsTests
{
    [Theory]
    [InlineData(30, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(2 * 3600 + 59, "2 h ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(3 * 86400 + 5, "3 days ago")]
    public void Ago(int seconds, string text) => Assert.Equal(text, Formats.Ago(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(-3, "0s")]
    [InlineData(8, "8s")]
    [InlineData(65, "1m 05s")]
    [InlineData(3720, "1h 02m")]
    public void Elapsed_ticks_with_seconds_until_an_hour(int seconds, string text) => Assert.Equal(text, Formats.Elapsed(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(8, "8s")]
    [InlineData(185, "3m")]
    [InlineData(3720, "1h 02m")]
    [InlineData(2 * 86400 + 3 * 3600 + 7, "2d 3h")]
    public void Duration_is_in_round_figures(int seconds, string text) => Assert.Equal(text, Formats.Duration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(903, "903")]
    [InlineData(4_000, "4k")]
    [InlineData(8_149, "8.1k")]
    [InlineData(122_061, "122k")]
    [InlineData(999_499, "999k")]
    [InlineData(999_500, "1M")]
    [InlineData(1_582_900, "1.6M")]
    public void Tokens_are_in_round_figures(long tokens, string text) => Assert.Equal(text, Formats.Tokens(tokens));

    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-haiku-4-5-20251001", "Haiku 4.5")]
    [InlineData("claude-sonnet-4-5[1m]", "Sonnet 4.5")]
    [InlineData("claude-fable-5-1", "Fable 5.1")]
    [InlineData("opus", "opus")]
    [InlineData("claude-opus", "claude-opus")]
    [InlineData("gpt-6", "gpt-6")]
    public void A_models_name_is_read_from_its_id(string id, string name) => Assert.Equal(name, Formats.ModelName(id));

    [Fact]
    public void A_folders_name_ignores_a_separator_at_its_end()
    {
        var folder = Path.Combine(Path.GetTempPath(), "api");
        Assert.Equal("api", Formats.FolderName(folder));
        Assert.Equal("api", Formats.FolderName(folder + Path.DirectorySeparatorChar));
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root, Formats.FolderName(root));
    }

    [Fact]
    public void Sizes_read_as_people_say_them()
    {
        Assert.Equal("512 bytes", Formats.Size(512));
        Assert.Equal("12 KB", Formats.Size(12 * 1024));
        Assert.Equal("140 MB", Formats.Size(140L * 1024 * 1024));
        Assert.Equal("3.2 GB", Formats.Size((long)(3.2 * 1024 * 1024 * 1024)));
    }

    [Fact]
    public void Capitalize() => Assert.Equal(("High", ""), (Formats.Capitalize("high"), Formats.Capitalize("")));
}
