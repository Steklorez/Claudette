using System.Text.Json.Nodes;
using Deventry.App.Conversation;

namespace Deventry.App.Tests;

/// <summary>The places marked beside the scroll bar, and the messages F3 and F4 go between (DESIGN.md §18, "Conversation cards").</summary>
public class ConversationLandmarksTests
{
    [Fact]
    public void Your_messages_errors_and_turns_that_changed_files_are_marked()
    {
        var turn = new TurnSummaryItem("3s");
        Assert.Null(ConversationLandmarks.KindOf(turn));
        turn.Files = [new TurnFileRow(turn, new Core.Diffs.TurnFileChange("a.cs", null, IsNew: true, BeforeKnown: true), "a.cs")];

        Assert.Equal(LandmarkKind.Prompt, ConversationLandmarks.KindOf(new UserMessageItem("fix it")));
        Assert.Equal(LandmarkKind.Error, ConversationLandmarks.KindOf(new NoteItem("Claude Code exited (code 1).", NoteKind.Error)));
        Assert.Equal(LandmarkKind.Error, ConversationLandmarks.KindOf(new HookRunItem("h", "Bash", "PreToolUse") { State = HookRunState.Failed }));
        Assert.Equal(LandmarkKind.Files, ConversationLandmarks.KindOf(turn));
        Assert.Null(ConversationLandmarks.KindOf(new NoteItem("Stopped.", NoteKind.Warning)));
        Assert.Null(ConversationLandmarks.KindOf(new AssistantTextItem()));
        Assert.Null(ConversationLandmarks.KindOf(new ToolGroupItem()));
        Assert.Equal("Your message: fix it", ConversationLandmarks.Describe(new UserMessageItem("fix\nit")));
        Assert.Equal("1 file changed", ConversationLandmarks.Describe(turn));
    }

    /// <summary>
    /// Settings → Conversation → Marks in the scroll bar (issue #99): your messages unless they're turned off, and Claude's
    /// answers, its replies that a tool call didn't follow, once they're turned on.
    /// </summary>
    [Fact]
    public void Your_messages_and_Claudes_answers_are_marked_as_settings_say()
    {
        var answer = new AssistantTextItem();
        answer.Append("All the tests pass.");
        var carriedOn = new AssistantTextItem { IsIntermediate = true };
        carriedOn.Append("Let me read the file.");
        var you = new UserMessageItem("fix it");

        Assert.Null(ConversationLandmarks.KindOf(answer));
        Assert.Equal(LandmarkKind.Answer, ConversationLandmarks.KindOf(answer, answers: true));
        Assert.Null(ConversationLandmarks.KindOf(carriedOn, answers: true));
        // One that hasn't said anything yet has nothing to go back to.
        Assert.Null(ConversationLandmarks.KindOf(new AssistantTextItem(), answers: true));
        Assert.Equal(LandmarkKind.Prompt, ConversationLandmarks.KindOf(you, answers: true));
        Assert.Null(ConversationLandmarks.KindOf(you, yourMessages: false, answers: true));
        Assert.Equal("Claude: All the tests pass.", ConversationLandmarks.Describe(answer));
    }

    [Fact]
    public void F3_and_F4_go_between_your_messages_and_Claudes_replies()
    {
        ConversationItem[] items =
        [
            new UserMessageItem("one"),            // 0
            new ToolGroupItem(),                   // 1
            new AssistantTextItem(),               // 2
            new NoteItem("note", NoteKind.Info),   // 3
            new UserMessageItem("two"),            // 4
            new ToolUseItem("t", "Bash", new JsonObject()), // 5
        ];

        Assert.Equal(0, ConversationLandmarks.First(items));
        Assert.Equal(2, ConversationLandmarks.Step(items, 0, forward: true));
        Assert.Equal(2, ConversationLandmarks.Step(items, 1, forward: true));
        Assert.Equal(4, ConversationLandmarks.Step(items, 2, forward: true));
        Assert.Equal(-1, ConversationLandmarks.Step(items, 4, forward: true));
        Assert.Equal(2, ConversationLandmarks.Step(items, 4, forward: false));
        Assert.Equal(2, ConversationLandmarks.Step(items, 3, forward: false));
        Assert.Equal(-1, ConversationLandmarks.Step(items, 0, forward: false));
        Assert.Equal(-1, ConversationLandmarks.First([]));
    }
}
