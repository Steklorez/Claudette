using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;

namespace Claudette.App.Tests;

/// <summary>The next prompt Claude Code suggests, in the empty composer (DESIGN.md §5, "Prompt suggestions").</summary>
public class PromptSuggestionTests
{
    private static JsonObject Suggestion(string text) => new()
    {
        ["type"] = "prompt_suggestion", ["suggestion"] = text, ["uuid"] = Guid.NewGuid().ToString(), ["session_id"] = "s1",
    };

    private static TabTestHarness WithSuggestions() => new(s => s.ClaudeCode.PromptSuggestions = true);

    /// <summary>Every suggestion the tab shows, as it shows it.</summary>
    private static List<string> Shown(TabViewModel tab)
    {
        var shown = new List<string>();
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabViewModel.PromptSuggestion) && tab.PromptSuggestion is { } suggestion)
            {
                shown.Add(suggestion);
            }
        };
        return shown;
    }

    /// <summary>A later turn, once it has ended: everything emitted before it has been applied too.</summary>
    private static async Task AfterNextTurnAsync(TabTestHarness h, TabViewModel tab, string reply)
    {
        h.Transport.EmitTurn(reply);
        // The reply comes while the turn is still working; its result ends it.
        await TabTestHarness.Eventually(() => tab.Items.OfType<AssistantTextItem>().Any(i => i.Text == reply), "the next turn");
        await TabTestHarness.Eventually(() => !tab.IsWorking, "the turn to end");
    }

    [Fact]
    public async Task Off_by_default_Claude_Code_isnt_asked_and_none_shows()
    {
        await using var h = new TabTestHarness();
        var tab = await h.OpenTabAsync();
        var shown = Shown(tab);
        Assert.False(h.Factory.Launches.Single().PromptSuggestions);

        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await AfterNextTurnAsync(h, tab, "later");

        Assert.Empty(shown);
        Assert.Equal(tab.Tips.ComposerPlaceholder, tab.ComposerPlaceholder);
    }

    [Fact]
    public async Task A_suggestion_after_the_turn_is_the_empty_composers_placeholder_and_Tab_puts_it_in_the_box()
    {
        await using var h = WithSuggestions();
        var tab = await h.OpenTabAsync();
        Assert.True(h.Factory.Launches.Single().PromptSuggestions);

        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await TabTestHarness.Eventually(() => tab.PromptSuggestion == "run the tests", "the suggestion");
        Assert.Equal("run the tests  (Tab to use)", tab.ComposerPlaceholder);

        Assert.True(tab.AcceptPromptSuggestion());

        Assert.Equal("run the tests", tab.ComposerText);
        Assert.Null(tab.PromptSuggestion);
        Assert.Equal(tab.Tips.ComposerPlaceholder, tab.ComposerPlaceholder);
        // Sent as any typed message is.
        await tab.SendCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Transport.SentUserTexts.Contains("run the tests"), "the message");
    }

    [Fact]
    public async Task Typing_dismisses_it_for_good()
    {
        await using var h = WithSuggestions();
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await TabTestHarness.Eventually(() => tab.PromptSuggestion is not null, "the suggestion");

        tab.ComposerText = "n";
        Assert.Null(tab.PromptSuggestion);
        tab.ComposerText = "";

        Assert.Null(tab.PromptSuggestion);
        Assert.False(tab.AcceptPromptSuggestion());
        Assert.Equal("", tab.ComposerText);
    }

    [Fact]
    public async Task One_that_arrives_while_something_is_typed_or_after_a_message_went_isnt_shown()
    {
        await using var h = WithSuggestions();
        var tab = await h.OpenTabAsync();
        var shown = Shown(tab);

        // The user is already typing something else.
        tab.ComposerText = "something else";
        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await AfterNextTurnAsync(h, tab, "first");

        // A message sent as the turn ended: the suggestion was for before it.
        tab.ComposerText = "go on";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit(Suggestion("commit this"));
        await AfterNextTurnAsync(h, tab, "second");

        Assert.Empty(shown);
    }

    [Fact]
    public async Task The_next_turn_clear_or_an_exit_takes_it_away()
    {
        await using var h = WithSuggestions();
        var tab = await h.OpenTabAsync();
        async Task SuggestAsync()
        {
            h.Transport.Emit(Suggestion("run the tests"));
            await TabTestHarness.Eventually(() => tab.PromptSuggestion is not null, "the suggestion");
        }
        h.Transport.EmitTurn();

        // A turn started some other way, such as a message from the Claude app.
        await SuggestAsync();
        h.Transport.Emit(new JsonObject { ["type"] = "system", ["subtype"] = "init", ["session_id"] = "s1", ["model"] = "claude-opus-5-5", ["permissionMode"] = "default" });
        await TabTestHarness.Eventually(() => tab.PromptSuggestion is null, "the next turn to take it");
        h.Transport.Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "ok", ["session_id"] = "s1" });

        await SuggestAsync();
        h.Transport.Emit(new JsonObject { ["type"] = "conversation_reset", ["trigger"] = "clear", ["new_conversation_id"] = "s2", ["session_id"] = "s1" });
        await TabTestHarness.Eventually(() => tab.PromptSuggestion is null, "/clear to take it");

        await SuggestAsync();
        h.Transport.Exit(0);
        await TabTestHarness.Eventually(() => tab.PromptSuggestion is null, "the exit to take it");
    }

    [Fact]
    public async Task Turning_the_setting_off_hides_it()
    {
        await using var h = WithSuggestions();
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        h.Transport.Emit(Suggestion("run the tests"));
        await TabTestHarness.Eventually(() => tab.PromptSuggestion is not null, "the suggestion");

        h.Services.Settings.ClaudeCode.PromptSuggestions = false;
        tab.OnSettingsChanged();

        Assert.Null(tab.PromptSuggestion);
        // The running session still sends them until it restarts; none shows.
        var shown = Shown(tab);
        h.Transport.Emit(Suggestion("commit this"));
        await AfterNextTurnAsync(h, tab, "later");
        Assert.Empty(shown);
    }
}
