using Avalonia.Headless.XUnit;
using Claudette.App.Conversation;
using LiveMarkdown.Avalonia;

namespace Claudette.App.UiTests;

/// <summary>A message's Markdown, parsed once for the renderers that show it (DESIGN.md §5).</summary>
public class KeptMarkdownTests
{
    [AvaloniaFact]
    public void A_renderer_has_the_document_as_soon_as_it_shows_it()
    {
        var text = new ObservableStringBuilder("# A heading\n\nSome **bold** text.");
        var kept = new KeptMarkdown(text);

        var first = new MarkdownRenderer { UpdateProducer = kept };
        var again = new MarkdownRenderer { UpdateProducer = kept };

        Assert.Equal(text.Version, first.DocumentUpdate?.Version);
        // Parsed once: the second renderer gets the same document.
        Assert.Same(first.DocumentUpdate, again.DocumentUpdate);
    }

    [AvaloniaFact]
    public async Task Text_streaming_in_while_shown_is_parsed_as_it_comes_and_kept_for_the_next_renderer()
    {
        var text = new ObservableStringBuilder("The start");
        var kept = new KeptMarkdown(text);
        var renderer = new MarkdownRenderer { UpdateProducer = kept };
        var window = UiText.Show(renderer, 400, 300);

        text.Append(" and **more**");
        await UiText.SettleUntilAsync(window, () => renderer.DocumentUpdate?.Version == text.Version, "the change parsed");
        text.Append(", and the end.");
        await UiText.SettleUntilAsync(window, () => renderer.DocumentUpdate?.Version == text.Version, "the next change parsed");

        var next = new MarkdownRenderer { UpdateProducer = kept };
        Assert.Same(renderer.DocumentUpdate, next.DocumentUpdate);
        window.Close();
    }

    [AvaloniaFact]
    public void A_renderer_moved_to_another_message_stops_following_the_first()
    {
        var (one, two) = (new KeptMarkdown(new ObservableStringBuilder("One")), new KeptMarkdown(new ObservableStringBuilder("Two")));
        var renderer = new MarkdownRenderer { UpdateProducer = one };
        Assert.Equal(1, one.RendererCount);

        renderer.UpdateProducer = two;
        Assert.Equal((0, 1), (one.RendererCount, two.RendererCount));
        renderer.UpdateProducer = KeptMarkdown.None;
        Assert.Equal(0, two.RendererCount);
    }
}
