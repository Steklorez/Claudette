using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>Images in tools' results rendered (DESIGN.md §18, "Images from Claude's tools"): under the card, and full size.</summary>
public class ToolImageUiTests
{
    [AvaloniaFact]
    public async Task An_image_Claude_read_shows_under_its_card_and_a_click_opens_it_full_size_in_its_own_window()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var path = Path.Combine(h.WorkFolder, "chart.png");

        h.Transport.Emit(new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = "t1", ["name"] = "Read", ["input"] = new JsonObject { ["file_path"] = path } }),
            },
        });
        h.Transport.Emit(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = "t1",
                    ["content"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = Png },
                    }),
                }),
            },
            ["tool_use_result"] = new JsonObject { ["type"] = "image", ["file"] = new JsonObject { ["type"] = "image/png", ["originalSize"] = 1000 } },
        });
        await UiText.SettleUntilAsync(window, () => OpenButton(window) is not null, "the image under the card");
        var button = OpenButton(window)!;

        // Headless drawing decodes no pixels, so the sizes are left to the real desktops (DESIGN.md §17).
        var preview = button.GetVisualDescendants().OfType<Image>().Single();
        Assert.IsType<Bitmap>(preview.Source);
        Assert.Equal((120, 320), (preview.MaxHeight, preview.MaxWidth));

        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        UiText.Settle(window);
        var viewer = Assert.Single(window.OwnedWindows.OfType<ImageWindow>());
        UiText.Settle(viewer);

        Assert.Equal("chart.png", viewer.Title);
        var shown = UiText.Describe(viewer);
        Assert.Contains("Copy image", shown, StringComparison.Ordinal);
        Assert.Contains("Save as…", shown, StringComparison.Ordinal);
        Assert.Contains("Open", shown, StringComparison.Ordinal);
        Assert.Contains("Read · PNG 70 bytes", shown, StringComparison.Ordinal);
        var picture = viewer.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "Picture");
        Assert.IsType<Bitmap>(picture.Source);
        Assert.Equal("chart.png", AutomationProperties.GetName(picture));
        // From now on it can be made any size.
        Assert.Equal(SizeToContent.Manual, viewer.SizeToContent);

        // Escape closes it.
        viewer.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        UiText.Settle(window);
        Assert.Empty(window.OwnedWindows.OfType<ImageWindow>());
    }

    private static Button? OpenButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == "Open chart.png" && b.IsEffectivelyVisible);

    /// <summary>A 1×1 PNG.</summary>
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
}
