using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>
/// Files named in a rendered reply (DESIGN.md §18, "Files named in Claude's replies"): inline code that names a file
/// becomes a link once the file is found, and a click opens it; code that isn't a file, or that's inside a link, stays code.
/// </summary>
public class ReplyFileUiTests
{
    [AvaloniaFact]
    public async Task Inline_code_that_names_a_file_becomes_a_link_that_opens_it()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        await h.OpenTabAsync();
        var spec = Path.Combine(h.WorkFolder, "docs", "specs", "weapons.md");
        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);
        await File.WriteAllTextAsync(spec, "# Weapons", TestContext.Current.CancellationToken);
        var window = UiText.Show(new ShellView { DataContext = h.Shell });

        h.Transport.EmitTurn("`docs/specs/weapons.md` is written. `System.IO.Path` isn't a file, and [`weapons.md`](https://example.com/weapons) is a web page.");
        await UiText.SettleUntilAsync(window, () => Codes(window).Any(c => c.IsLink), "the file's link");

        var codes = Codes(window);
        var file = codes.Single(c => c.Code == "docs/specs/weapons.md");
        Assert.False(codes.Single(c => c.Code == "System.IO.Path").IsLink);
        // The link it's in still goes where it says.
        Assert.False(codes.Single(c => c.Code == "weapons.md").IsLink);

        // The code's own look, underlined as the link it's in is.
        var code = file.GetLogicalDescendants().OfType<CodeInline>().Single();
        Assert.Equal(TextDecorations.Underline, code.TextDecorations);
        Assert.Null(codes.Single(c => c.Code == "System.IO.Path").GetLogicalDescendants().OfType<CodeInline>().Single().TextDecorations);

        var block = file.FindLogicalAncestorOfType<MarkdownTextBlock>()!;
        var rect = block.GetCodeInlineRects(code).First();
        var at = block.TranslatePoint(rect.Center, window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        await UiText.SettleUntilAsync(window, () => h.Platform.OpenedFiles.Count > 0, "the file to open");
        Assert.Equal([spec], h.Platform.OpenedFiles);
        Assert.Empty(h.Platform.OpenedUrls);
    }

    private static List<ReplyFileCode> Codes(Window window) =>
        [.. window.GetVisualDescendants().OfType<MarkdownTextBlock>().SelectMany(b => b.GetLogicalDescendants().OfType<ReplyFileCode>())];
}
