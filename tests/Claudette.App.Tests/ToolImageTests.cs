using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Settings;

namespace Claudette.App.Tests;

/// <summary>
/// Images in tools' results, under their cards and full size (DESIGN.md §18, "Images from Claude's tools"), in the shapes
/// Claude Code 2.1.299 sends.
/// </summary>
public class ToolImageTests
{
    /// <summary>A 1×1 PNG.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public async Task A_Read_of_an_image_shows_it_under_its_card_and_full_size_with_Copy_Save_as_and_Open()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var path = Path.Combine(h.WorkFolder, "wide.png");

        h.Transport.Emit(Wire.Tool(null, "t1", "Read", new JsonObject { ["file_path"] = path }));
        h.Transport.Emit(Result("t1", [ImageBlock()], ReadImage(scaled: true)));
        var card = await CardAsync(tab);

        var image = Assert.Single(card.Images);
        Assert.Equal(("wide.png", path), (image.Name, image.FilePath));
        Assert.False(card.HasOutput);

        ImageViewerViewModel? viewer = null;
        h.Shell.ShowImageWindow = v => viewer = v;
        tab.OpenImageCommand.Execute(image);
        Assert.NotNull(viewer);
        Assert.Equal("wide.png", viewer.Title);
        Assert.Equal("Read · PNG 70 bytes · scaled down from 3000 × 1000 to send", viewer.Details);
        Assert.Equal(Convert.FromBase64String(Png), viewer.Data);

        // Copy image says "Copied" for a moment.
        await viewer.CopyCommand.ExecuteAsync(null);
        Assert.Equal(Convert.FromBase64String(Png), h.Platform.ClipboardImage);
        Assert.True(viewer.IsCopied);
        h.Time.Advance(TabViewModel.CopiedFor);
        Assert.False(viewer.IsCopied);

        // Save as… suggests the file's name, as the type it is.
        var saved = Path.Combine(h.WorkFolder, "copy.png");
        h.Platform.SavePathToPick = saved;
        await viewer.SaveAsCommand.ExecuteAsync(null);
        Assert.Null(viewer.Problem);
        Assert.Equal(Convert.FromBase64String(Png), await File.ReadAllBytesAsync(saved, TestContext.Current.CancellationToken));
        Assert.Equal("wide.png", h.Platform.LastSaveDialog?.Name);
        Assert.Equal([new SaveFileType("PNG image", "png")], h.Platform.LastSaveDialog?.Types);
        Assert.False(viewer.HasProblem);

        // Open: the file Claude read.
        Assert.True(viewer.CanOpen);
        await viewer.OpenCommand.ExecuteAsync(null);
        Assert.Equal([path], h.Platform.OpenedFiles);
        viewer.Dispose();
    }

    [Fact]
    public async Task A_screenshot_from_an_MCP_tool_shows_beside_its_text_and_has_no_file_to_open()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Wire.Tool(null, "t1", "mcp__playwright__browser_take_screenshot", []));
        h.Transport.Emit(Result("t1", [new JsonObject { ["type"] = "text", ["text"] = "Took a screenshot of the page" }, ImageBlock()],
            new JsonObject { ["content"] = new JsonArray() }));
        var card = await CardAsync(tab);

        Assert.Equal("Took a screenshot of the page", card.Output);
        var image = Assert.Single(card.Images);
        ImageViewerViewModel? viewer = null;
        h.Shell.ShowImageWindow = v => viewer = v;
        tab.OpenImageCommand.Execute(image);

        Assert.Equal(("Image", "mcp__playwright__browser_take_screenshot · PNG 70 bytes"), (viewer!.Title, viewer.Details));
        Assert.False(viewer.CanOpen);
        h.Platform.SavePathToPick = null;
        await viewer.SaveAsCommand.ExecuteAsync(null);
        Assert.Equal("image.png", h.Platform.LastSaveDialog?.Name);
        viewer.Dispose();
    }

    [Fact]
    public async Task Bash_output_that_is_an_image_shows_the_image_rather_than_its_data_URL()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();

        h.Transport.Emit(Wire.Tool(null, "t1", "Bash", new JsonObject { ["command"] = "python plot.py" }));
        h.Transport.Emit(Result("t1", [ImageBlock()], new JsonObject
        {
            ["stdout"] = $"data:image/png;base64,{Png}", ["stderr"] = "", ["interrupted"] = false, ["isImage"] = true,
        }));
        var card = await CardAsync(tab);

        Assert.Single(card.Images);
        Assert.False(card.HasOutput);
        Assert.DoesNotContain(Png, card.ResultSummary ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_fails_says_why()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        h.Transport.Emit(Wire.Tool(null, "t1", "Read", new JsonObject { ["file_path"] = Path.Combine(h.WorkFolder, "a.png") }));
        h.Transport.Emit(Result("t1", [ImageBlock()], ReadImage(scaled: false)));
        var card = await CardAsync(tab);
        ImageViewerViewModel? viewer = null;
        h.Shell.ShowImageWindow = v => viewer = v;
        tab.OpenImageCommand.Execute(card.Images[0]);
        Assert.Equal("Read · PNG 70 bytes", viewer!.Details);

        // A folder by that name is in the way.
        var folder = Path.Combine(h.WorkFolder, "a.png");
        Directory.CreateDirectory(folder);
        h.Platform.SavePathToPick = folder;
        await viewer.SaveAsCommand.ExecuteAsync(null);

        Assert.True(viewer.HasProblem);
        Assert.StartsWith("Couldn't save the image: ", viewer.Problem, StringComparison.Ordinal);
        viewer.Dispose();
    }

    [Fact]
    public async Task A_restored_tab_shows_the_images_its_transcript_keeps()
    {
        await using var h = new TabTestHarness();
        var path = Path.Combine(h.WorkFolder, "pic.png");
        var use = new JsonObject { ["type"] = "tool_use", ["id"] = "t1", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = path } };
        var result = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "t1", ["content"] = new JsonArray(ImageBlock()) }),
        };
        h.WriteTranscript("s1",
            Wire.Entry("assistant", "2026-09-28T11:00:00Z", Wire.Message(use)),
            Wire.Entry("user", "2026-09-28T11:00:01Z", result, ReadImage(scaled: false)));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1" }];

        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();

        // The transcript is replayed off the test's thread, and a card gets its images just after its result.
        await TabTestHarness.Eventually(() => tab.Items.ToolCalls().SingleOrDefault() is { IsComplete: true, HasImages: true }, "the tool's images");
        var card = tab.Items.ToolCalls().Single();
        Assert.Equal(("pic.png", path), (card.Images.Single().Name, card.Images.Single().FilePath));
    }

    private static async Task<ToolUseItem> CardAsync(TabViewModel tab)
    {
        await TabTestHarness.Eventually(() => tab.Items.ToolCalls().SingleOrDefault() is { IsComplete: true }, "the tool's result");
        return tab.Items.ToolCalls().Single();
    }

    private static JsonObject ImageBlock() =>
        new() { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["data"] = Png, ["media_type"] = "image/png" } };

    /// <summary>A Read's <c>tool_use_result</c> for an image, which Claude Code scaled down from 3000 × 1000 or sent as it was.</summary>
    private static JsonObject ReadImage(bool scaled) => new()
    {
        ["type"] = "image",
        ["file"] = new JsonObject
        {
            ["base64"] = Png,
            ["type"] = "image/png",
            ["originalSize"] = 70,
            ["dimensions"] = scaled
                ? new JsonObject { ["originalWidth"] = 3000, ["originalHeight"] = 1000, ["displayWidth"] = 2000, ["displayHeight"] = 667 }
                : new JsonObject { ["originalWidth"] = 1, ["originalHeight"] = 1, ["displayWidth"] = 1, ["displayHeight"] = 1 },
        },
    };

    private static string Result(string toolUseId, JsonObject[] content, JsonObject toolUseResult) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = new JsonArray(content.Select(b => (JsonNode?)b).ToArray()),
            }),
        },
        ["parent_tool_use_id"] = null,
        ["tool_use_result"] = toolUseResult,
    }.ToJsonString();
}
