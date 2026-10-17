using Claudette.Core.Composer;
using Claudette.Core.Settings;
using Claudette.Core.Subagents;

namespace Claudette.Core.Tests.Subagents;

/// <summary>What Claudette tells Claude about subagents (DESIGN.md §5, "Subagents").</summary>
public class SubagentMessagesTests
{
    [Fact]
    public void Claude_starts_with_a_note_only_when_subagents_are_discouraged_or_turned_off()
    {
        Assert.Null(SubagentMessages.StartNote(SubagentUse.ClaudeDecides));
        Assert.Contains("only when the user asks for one", SubagentMessages.StartNote(SubagentUse.Discourage), StringComparison.Ordinal);
        Assert.Contains("don't call the Agent tool", SubagentMessages.StartNote(SubagentUse.DontAllow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_in_a_running_tab_has_a_note_and_a_line_for_each_setting()
    {
        Assert.All(Enum.GetValues<SubagentUse>(), use =>
        {
            Assert.StartsWith("[Claudette] ", SubagentMessages.ChangeNote(use), StringComparison.Ordinal);
            Assert.StartsWith("Told Claude ", SubagentMessages.ChangeNoteLine(use), StringComparison.Ordinal);
        });
        Assert.Equal("Told Claude it can use subagents again", SubagentMessages.ChangeNoteLine(SubagentUse.ClaudeDecides));
    }

    [Fact]
    public void The_refusal_is_found_inside_what_Claude_Code_puts_around_it()
    {
        // As Claude Code 2.1.284 reports a PreToolUse hook's deny.
        Assert.True(SubagentMessages.IsRefusal($"PreToolUse:Agent hook error: {SubagentMessages.Refused}"));
        Assert.False(SubagentMessages.IsRefusal("Agent terminated early due to an API error"));
        Assert.False(SubagentMessages.IsRefusal(null));
    }

    [Theory]
    [InlineData("Agent", true)]
    [InlineData("Task", true)]
    [InlineData("SendMessage", false)]
    [InlineData(null, false)]
    public void Agent_and_its_older_name_start_subagents(string? tool, bool starts) =>
        Assert.Equal(starts, SubagentMessages.StartsSubagent(tool));

    [Fact]
    public void A_name_is_mentioned_quoted_when_it_has_spaces()
    {
        Assert.Equal("@reviewer", ComposerTokens.NameMention("reviewer"));
        Assert.Equal("@\"Review the change\"", ComposerTokens.NameMention("Review the change"));
    }
}
