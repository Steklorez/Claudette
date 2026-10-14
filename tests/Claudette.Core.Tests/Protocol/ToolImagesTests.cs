using System.Text.Json.Nodes;
using Claudette.Core.Protocol;

namespace Claudette.Core.Tests.Protocol;

/// <summary>Images in tools' results (DESIGN.md §18, "Images from Claude's tools"), in the shapes Claude Code 2.1.299 sends.</summary>
public class ToolImagesTests
{
    /// <summary>A 1×1 PNG.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public void A_tool_results_image_blocks_are_read_with_its_text()
    {
        var result = Result($$$"""
            [{"type":"text","text":"Here is the screenshot"},
             {"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Png}}}"}},
             {"type":"image","source":{"type":"url","url":"https://example.com/a.png"}},
             {"type":"image","source":{"type":"base64","media_type":"image/jpeg","data":"not base64!"}}]
            """);

        Assert.Equal("Here is the screenshot", result.Text);
        var image = Assert.Single(result.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(Convert.FromBase64String(Png), image.Data);
    }

    [Fact]
    public void A_result_without_images_has_none()
    {
        Assert.Empty(Result("\"plain text\"").Images);
        Assert.Empty(Result("""[{"type":"text","text":"a"}]""").Images);
    }

    [Fact]
    public void A_Read_of_an_image_is_named_after_its_file_and_says_what_it_was_scaled_from()
    {
        var result = Result($$$"""[{"type":"image","source":{"type":"base64","data":"{{{Png}}}","media_type":"image/jpeg"}}]""");
        var details = Details($$$"""
            {"type":"image","file":{"base64":"{{{Png}}}","type":"image/jpeg","originalSize":11244,
             "dimensions":{"originalWidth":3000,"originalHeight":1000,"displayWidth":2000,"displayHeight":667} } }
            """);

        var image = Assert.Single(ToolImages.From("Read", Input("/work/shots/wide.png"), result, details));

        Assert.Equal("wide.png", image.Name);
        // Claude Code sent it as JPEG, so it saves as one.
        Assert.Equal("wide.jpg", image.FileName);
        Assert.Equal("/work/shots/wide.png", image.FilePath);
        Assert.Equal(new ImageSize(3000, 1000), image.ScaledFrom);
        Assert.Equal("3000 × 1000", image.ScaledFrom.ToString());
    }

    [Fact]
    public void An_image_Read_at_its_own_size_wasnt_scaled()
    {
        var result = Result($$$"""[{"type":"image","source":{"type":"base64","data":"{{{Png}}}","media_type":"image/png"}}]""");
        var details = Details($$$"""
            {"type":"image","file":{"base64":"{{{Png}}}","type":"image/png","originalSize":1351,
             "dimensions":{"originalWidth":32,"originalHeight":24,"displayWidth":32,"displayHeight":24} } }
            """);

        var image = Assert.Single(ToolImages.From("Read", Input("/work/pic.png"), result, details));

        Assert.Equal(("pic.png", "pic.png"), (image.Name, image.FileName));
        Assert.Null(image.ScaledFrom);
    }

    [Fact]
    public void A_PDFs_pages_are_named_by_their_page_numbers()
    {
        var result = Result($$$"""
            [{"type":"text","text":"PDF pages extracted: 2 page(s) from /work/report.pdf (584 bytes)"},
             {"type":"image","source":{"type":"base64","data":"{{{Png}}}","media_type":"image/jpeg"}},
             {"type":"image","source":{"type":"base64","data":"{{{Png}}}","media_type":"image/jpeg"}}]
            """);
        var details = Details("""{"type":"parts","file":{"filePath":"/work/report.pdf","originalSize":584,"outputDir":"/tmp/pdf","count":2},"firstPage":4}""");

        var images = ToolImages.From("Read", Input("/work/report.pdf", pages: "4-5"), result, details);

        Assert.Equal(["report.pdf, page 4", "report.pdf, page 5"], images.Select(i => i.Name));
        Assert.Equal(["report-page-4.jpg", "report-page-5.jpg"], images.Select(i => i.FileName));
        Assert.All(images, i => Assert.Null(i.FilePath));

        // Without firstPage, the call's pages say where they start.
        var withoutFirst = Details("""{"type":"parts","file":{"filePath":"/work/report.pdf","originalSize":584,"outputDir":"/tmp/pdf","count":2}}""");
        Assert.Equal("report.pdf, page 7", ToolImages.From("Read", Input("/work/report.pdf", pages: "7-8"), result, withoutFirst)[0].Name);
    }

    [Fact]
    public void A_notebooks_figures_are_numbered()
    {
        var result = Result($$$"""
            [{"type":"text","text":"<cell id=\"c1\">plot()</cell id=\"c1\">"},
             {"type":"image","source":{"data":"{{{Png}}}","media_type":"image/png","type":"base64"}}]
            """);

        var image = Assert.Single(ToolImages.From("Read", Input("/work/analysis.ipynb"), result, Details("""{"type":"notebook","file":{"filePath":"/work/analysis.ipynb","cells":[]}}""")));

        Assert.Equal(("analysis.ipynb, figure 1", "analysis-figure-1.png"), (image.Name, image.FileName));
    }

    [Fact]
    public void Images_from_other_tools_are_just_images()
    {
        var one = Result($$$"""[{"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Png}}}"}}]""");
        var two = Result($$$"""
            [{"type":"image","source":{"type":"base64","media_type":"image/png","data":"{{{Png}}}"}},
             {"type":"image","source":{"type":"base64","media_type":"image/webp","data":"{{{Png}}}"}}]
            """);

        var screenshot = Assert.Single(ToolImages.From("mcp__playwright__browser_take_screenshot", [], one, Details("""{"content":[]}""")));
        Assert.Equal(("Image", "image.png", null), (screenshot.Name, screenshot.FileName, screenshot.FilePath));
        Assert.Equal(["Image 1", "Image 2"], ToolImages.From("Bash", Input("/x"), two, null).Select(i => i.Name));
        Assert.Equal(["image-1.png", "image-2.webp"], ToolImages.From("Bash", [], two, null).Select(i => i.FileName));
        Assert.Empty(ToolImages.From("Read", Input("/work/a.txt"), Result("\"text\""), null));
    }

    private static ToolResultBlock Result(string content)
    {
        var line = $$$"""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":{{{content}}}}]},"session_id":"s1"}""";
        Assert.True(MessageParser.TryParse(line, out var message, out var error), error);
        return Assert.Single(Assert.IsType<UserMessage>(message).Content.OfType<ToolResultBlock>());
    }

    private static JsonObject Details(string json) => JsonNode.Parse(json)!.AsObject();

    private static JsonObject Input(string path, string? pages = null) =>
        pages is null ? new JsonObject { ["file_path"] = path } : new JsonObject { ["file_path"] = path, ["pages"] = pages };
}
