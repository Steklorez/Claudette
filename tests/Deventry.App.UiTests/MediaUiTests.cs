using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels.ToolWindows;
using Deventry.App.Views;

namespace Deventry.App.UiTests;

/// <summary>The Media window (DESIGN.md §18, "Media"), rendered: its two sections, their tiles, and folding one.</summary>
public class MediaUiTests
{
    [AvaloniaFact]
    public async Task The_media_window_shows_the_users_and_the_agents_tiles_and_a_section_folds()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1200, 800);
        Assert.Empty(StripIcons(window, "Media"));
        var clip = System.IO.Path.Combine(h.WorkFolder, "run.mp4");
        await File.WriteAllBytesAsync(clip, [0, 0, 0, 0x18], TestContext.Current.CancellationToken);

        Assert.True(tab.AddImage(TestImages.Png, "Pasted image"));
        tab.ComposerText = "Why does run.mp4 stutter here?";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use", ["id"] = "t1", ["name"] = "mcp__playwright__browser_take_screenshot", ["input"] = new JsonObject(),
            }) },
        });
        h.Transport.Emit(ScreenshotResult("t1"));
        await UiText.SettleUntilAsync(window, () => tab.Media.User.Count == 2 && tab.Media.Agent.Count == 1
            && tab.Media.Sections.SelectMany(s => s.Entries).Where(e => e.IsImage).All(e => e.HasThumbnail), "the media and its thumbnails");

        var icon = Assert.Single(StripIcons(window, "Media"));
        Assert.Equal("Media · User 2 · Agent 1", ToolTip.GetTip(icon));
        h.ShowToolWindow(ToolWindowKinds.Media);
        UiText.Settle(window);

        Assert.Equal(["Open Attached image", "Open run.mp4", "Open Image"], Tiles(window).Select(AutomationProperties.GetName));
        // An image's tile shows it; the video's, its film.
        Assert.All(Tiles(window).Where(t => t.DataContext is MediaEntry { IsImage: true }), t => Assert.NotNull(TileImage(t).Source));
        var video = Tiles(window).Single(t => t.DataContext is MediaEntry { IsVideo: true });
        Assert.True(video.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Classes.Contains("mediakind")).IsEffectivelyVisible);

        var header = Header(window, "Collapse User");
        header.Command!.Execute(null);
        UiText.Settle(window);

        Assert.Equal(["Open Image"], Tiles(window).Select(AutomationProperties.GetName));
        Assert.Same(header, Header(window, "Expand User"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_tiles_X_clears_it_under_a_line_into_a_folded_group_and_its_arrow_restores_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, 1200, 800);
        Assert.True(tab.AddImage(TestImages.Png, "Pasted image"));
        tab.ComposerText = "What's wrong here?";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_use", ["id"] = "t1", ["name"] = "mcp__playwright__browser_take_screenshot", ["input"] = new JsonObject(),
            }) },
        });
        h.Transport.Emit(ScreenshotResult("t1"));
        await UiText.SettleUntilAsync(window, () => tab.Media.User.Count == 1 && tab.Media.Agent.Count == 1, "the media");
        h.ShowToolWindow(ToolWindowKinds.Media);
        UiText.Settle(window);
        Assert.False(Divider(window).IsEffectivelyVisible);

        // There, but not seen, until the pointer is over its tile or it has the focus.
        var clear = ClearButton(window, "Clear Image");
        Assert.Equal(0, clear.Opacity);
        // Round, with its outline, as an attached image's X is in the composer.
        Assert.Equal((new Avalonia.CornerRadius(9), new Avalonia.Thickness(1)), (clear.CornerRadius, clear.BorderThickness));
        clear.Focus();
        UiText.Settle(window);
        Assert.Equal(1, clear.Opacity);

        clear.Command!.Execute(clear.CommandParameter);
        UiText.Settle(window);

        // Under the line, in Agent (Cleared), folded.
        Assert.True(Divider(window).IsEffectivelyVisible);
        Assert.Equal(["Open Attached image"], Tiles(window).Select(AutomationProperties.GetName));
        Assert.False(Header(window, "Expand User (Cleared)", visible: false).IsEffectivelyVisible);
        Header(window, "Expand Agent (Cleared)").Command!.Execute(null);
        UiText.Settle(window);
        Assert.Equal(["Open Attached image", "Open Image"], Tiles(window).Select(AutomationProperties.GetName));

        var restore = ClearButton(window, "Restore Image");
        restore.Command!.Execute(restore.CommandParameter);
        UiText.Settle(window);

        Assert.False(Divider(window).IsEffectivelyVisible);
        Assert.Equal(["Open Attached image", "Open Image"], Tiles(window).Select(AutomationProperties.GetName));
        window.Close();
    }

    private static Button ClearButton(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("mediaclear") && b.IsEffectivelyVisible && AutomationProperties.GetName(b) == name);

    private static Border Divider(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("mediadivider"));

    private static List<Button> StripIcons(Window window, string title) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("stripicon") && b.IsEffectivelyVisible && AutomationProperties.GetName(b) == title)];

    private static List<Button> Tiles(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("mediatile") && b.IsEffectivelyVisible)];

    private static Image TileImage(Button tile) => tile.GetVisualDescendants().OfType<Image>().Single();

    private static Button Header(Window window, string name, bool visible = true) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("mediasection") && AutomationProperties.GetName(b) == name
            && (!visible || b.IsEffectivelyVisible));

    private static string ScreenshotResult(string toolUseId) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolUseId,
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["data"] = Convert.ToBase64String(TestImages.Png), ["media_type"] = "image/png" },
                }),
            }),
        },
        ["parent_tool_use_id"] = null,
    }.ToJsonString();
}
