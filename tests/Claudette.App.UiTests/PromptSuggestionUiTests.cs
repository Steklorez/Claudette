using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>The next prompt Claude Code suggests, as the empty composer's placeholder, and Tab or → taking it (DESIGN.md §5, "Prompt suggestions").</summary>
public class PromptSuggestionUiTests
{
    private static JsonObject Suggestion(string text) => new()
    {
        ["type"] = "prompt_suggestion", ["suggestion"] = text, ["uuid"] = Guid.NewGuid().ToString(), ["session_id"] = "s1",
    };

    [AvaloniaFact]
    public async Task The_suggestion_is_the_placeholder_and_Tab_or_Right_puts_it_in_the_box()
    {
        await using var h = new TabTestHarness(s => s.ClaudeCode.PromptSuggestions = true, dispatcher: new AvaloniaUiDispatcher());
        var tab = await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell });
        var composer = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "Composer");
        var placeholder = composer.PlaceholderText;

        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await UiText.SettleUntilAsync(window, () => composer.PlaceholderText == "run the tests  (Tab to use)", "the suggestion");

        composer.Focus();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal("run the tests", composer.Text);
        Assert.Equal("run the tests".Length, composer.CaretIndex);
        // The box kept the focus: Tab didn't move it on.
        Assert.True(composer.IsFocused);
        Assert.Equal(placeholder, composer.PlaceholderText);

        // → does the same, once the box is empty and there's a new one.
        tab.ComposerText = "";
        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("commit this"));
        await UiText.SettleUntilAsync(window, () => composer.PlaceholderText == "commit this  (Tab to use)", "the next suggestion");
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        UiText.Settle(window);

        Assert.Equal("commit this", composer.Text);
    }
}
