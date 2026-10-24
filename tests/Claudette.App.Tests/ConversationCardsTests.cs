using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.App.Tests;

/// <summary>The conversation as cards: Tool calls cards, quieter steps, times and the turn's footer (DESIGN.md §18, "Conversation cards").</summary>
public class ConversationCardsTests
{
    private readonly ObservableCollection<ConversationItem> _items = [];
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));
    private readonly ConversationBuilder _builder;

    public ConversationCardsTests()
    {
        _builder = new ConversationBuilder(_items) { Time = _time };
    }

    [Fact]
    public void Calls_in_a_row_share_a_card_and_anything_between_them_starts_another()
    {
        _builder.AddUserMessage("look around");
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}},{"type":"tool_use","id":"g1","name":"Grep","input":{"pattern":"TODO"}}]}}""");
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls"}}]}}""");
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"Found them."},{"type":"tool_use","id":"e1","name":"Edit","input":{"file_path":"a.cs","old_string":"x","new_string":"y"}}]}}""");

        Assert.Collection(_items,
            item => Assert.IsType<UserMessageItem>(item),
            item => Assert.Equal(["Read", "Grep", "Bash"], Assert.IsType<ToolGroupItem>(item).Calls.Select(c => c.Name)),
            item => Assert.IsType<AssistantTextItem>(item),
            item => Assert.Equal("Edit", Assert.Single(Assert.IsType<ToolGroupItem>(item).Calls).Name));
        var first = (ToolGroupItem)_items[1];
        Assert.All(first.Calls, call => Assert.Same(first, call.Group));
        Assert.Equal("3 calls · 3 running", first.CountText);
    }

    [Fact]
    public void A_subagent_is_a_card_of_its_own_and_its_calls_are_carded_inside_it()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}},{"type":"tool_use","id":"a1","name":"Agent","input":{"description":"Look"}}]}}""");
        Apply("""{"type":"assistant","parent_tool_use_id":"a1","message":{"content":[{"type":"tool_use","id":"g1","name":"Grep","input":{"pattern":"x"}},{"type":"tool_use","id":"g2","name":"Glob","input":{"pattern":"*.cs"}}]}}""");

        Assert.Collection(_items,
            item => Assert.Equal("Read", Assert.Single(Assert.IsType<ToolGroupItem>(item).Calls).Name),
            item => Assert.Equal(["Grep", "Glob"], Assert.IsType<ToolGroupItem>(Assert.Single(Assert.IsType<SubagentItem>(item).Items)).Calls.Select(c => c.Name)));
        Assert.Equal(["Read", "a1"], _items.ToolCalls().Select(c => c is SubagentItem ? c.ToolUseId : c.Name));
        Assert.Equal(["Read", "Agent", "Grep", "Glob"], _items.Descendants().OfType<ToolUseItem>().Select(c => c.Name));
    }

    [Fact]
    public void One_tile_is_open_at_a_time()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls"}},{"type":"tool_use","id":"b2","name":"Bash","input":{"command":"pwd"}}]}}""");
        var card = Assert.IsType<ToolGroupItem>(Assert.Single(_items));
        var (first, second) = (card.Calls[0], card.Calls[1]);

        first.IsExpanded = true;
        Assert.Same(first, card.Expanded);
        second.IsExpanded = true;

        Assert.False(first.IsExpanded);
        Assert.Same(second, card.Expanded);
        second.ToggleCommand.Execute(null);
        Assert.Null(card.Expanded);
        Assert.False(card.HasExpanded);
    }

    [Fact]
    public void A_call_takes_the_time_it_was_made_and_says_how_long_it_took()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"src/a.cs"}}]}}""");
        var read = Assert.Single(Assert.IsType<ToolGroupItem>(Assert.Single(_items)).Calls);
        Assert.Equal("Running…", read.StateText);
        Assert.Equal(_time.GetUtcNow(), read.StartedAt);
        Assert.NotNull(read.TimeText);

        _time.Advance(TimeSpan.FromMilliseconds(400));
        Apply("""{"type":"user","tool_use_result":{"type":"text","file":{"filePath":"src/a.cs","content":"one\ntwo\nthree","numLines":3,"startLine":1,"totalLines":3}},"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"1\tone\n2\ttwo\n3\tthree"}]}}""");

        Assert.Equal("0.4s", read.DurationText);
        Assert.Equal("3 lines · 0.4s", read.StateText);
        // The file as Claude read it, for the tile's detail and the call's window.
        Assert.Equal("one\ntwo\nthree", read.ReadText);
        Assert.True(read.CanExpand);
    }

    [Fact]
    public void A_failed_call_says_so_and_its_card_counts_it()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"false"}},{"type":"tool_use","id":"b2","name":"Bash","input":{"command":"true"}}]}}""");
        _time.Advance(TimeSpan.FromSeconds(2));
        Apply("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"b1","content":"Exit code 1","is_error":true},{"type":"tool_result","tool_use_id":"b2","content":""}]}}""");

        var card = Assert.IsType<ToolGroupItem>(Assert.Single(_items));
        Assert.Equal("Failed · 2.0s", card.Calls[0].StateText);
        Assert.Equal("Done · 2.0s", card.Calls[1].StateText);
        Assert.Equal("2 calls · 1 failed", card.CountText);
        Assert.True(card.HasFailed);
        Assert.False(card.IsRunning);
    }

    [Fact]
    public void A_file_tile_shows_the_end_of_its_path_and_a_screen_reader_hears_all_of_it()
    {
        var edit = new ToolUseItem("e1", "Edit", new JsonObject { ["file_path"] = @"C:\Demo\starfall\Source\Player\Character.cpp" });
        var read = new ToolUseItem("r1", "Read", new JsonObject { ["file_path"] = "src/a.cs", ["offset"] = 40 });
        var bash = new ToolUseItem("b1", "Bash", new JsonObject { ["command"] = "dotnet test" });

        Assert.Equal(@"…\Player\Character.cpp", edit.TileSummary);
        Assert.Equal(@"Edit C:\Demo\starfall\Source\Player\Character.cpp", edit.TileName);
        Assert.Equal("src/a.cs (from line 40)", read.TileSummary);
        Assert.Equal("dotnet test", bash.TileSummary);
    }

    [Fact]
    public void A_reply_Claude_carried_on_after_is_a_step_on_the_way_and_the_turns_answer_isnt()
    {
        _builder.AddUserMessage("fix it");
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"I'll look first."},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}}]}}""");
        Apply("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r1","content":"x"}]}}""");
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"Fixed."}]}}""");
        Apply("""{"type":"result","subtype":"success","is_error":false,"duration_ms":3000,"session_id":"s"}""");
        _builder.AddUserMessage("and the tests?");
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"Running them."},{"type":"tool_use","id":"t1","name":"TodoWrite","input":{"todos":[]}}]}}""");

        var replies = _items.OfType<AssistantTextItem>().ToList();
        Assert.Equal([true, false, true], replies.Select(r => r.IsIntermediate));
    }

    [Fact]
    public void A_turns_footer_counts_its_calls_and_cache_reads_and_takes_the_time_it_ended()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}},{"type":"tool_use","id":"a1","name":"Agent","input":{"description":"Look"}}]}}""");
        Apply("""{"type":"assistant","parent_tool_use_id":"a1","message":{"content":[{"type":"tool_use","id":"g1","name":"Grep","input":{"pattern":"x"}}]}}""");
        _time.Advance(TimeSpan.FromMinutes(1));
        Apply("""{"type":"result","subtype":"success","is_error":false,"duration_ms":64000,"session_id":"s","usage":{"input_tokens":200,"cache_read_input_tokens":62000,"cache_creation_input_tokens":0,"output_tokens":904}}""");

        var footer = Assert.IsType<TurnSummaryItem>(_items[^1]);
        Assert.Equal(3, footer.ToolCalls);
        Assert.Equal(62000, footer.CacheReadTokens);
        Assert.Equal("1m 04s · 62.2k in · 904 out · 62k from cache · 3 tool calls", footer.StatsText);
        Assert.Equal(_time.GetUtcNow(), footer.EndedAt);
        Assert.NotNull(footer.TimeText);

        // The next turn counts from nothing.
        Apply("""{"type":"result","subtype":"success","is_error":false,"duration_ms":1000,"session_id":"s"}""");
        Assert.Equal(0, Assert.IsType<TurnSummaryItem>(_items[^1]).ToolCalls);
    }

    [Fact]
    public void A_restored_conversation_takes_its_calls_times_from_the_transcript()
    {
        var sent = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        Replay("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls"}}]}}""", sent);
        Replay("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"b1","content":"a.cs"}]}}""", sent.AddSeconds(3));

        var bash = Assert.Single(Assert.IsType<ToolGroupItem>(Assert.Single(_items)).Calls);
        Assert.Equal(sent, bash.StartedAt);
        Assert.Equal("3.0s", bash.DurationText);
    }

    [Fact]
    public void Find_matches_each_call_in_a_card_and_one_joining_a_card_while_it_looks()
    {
        var search = new ConversationSearch(_items) { IsOpen = true };
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"vulkan --check"}},{"type":"tool_use","id":"b2","name":"Bash","input":{"command":"ls"}}]}}""");
        search.Query = "vulkan";
        var card = Assert.IsType<ToolGroupItem>(Assert.Single(_items));
        Assert.Same(card.Calls[0], search.Current);

        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b3","name":"Bash","input":{"command":"vulkan --run"}}]}}""");

        Assert.Equal(2, search.Count);
        Assert.Same(card.Calls[0], search.Current);
        Assert.Equal("1 of 2", search.CountText);
    }

    [Fact]
    public void The_export_keeps_every_call_in_a_card()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}},{"type":"tool_use","id":"b1","name":"Bash","input":{"command":"ls"}}]}}""");

        var markdown = ConversationExport.ToMarkdown("Calls", _items);

        Assert.Contains("- **Read** `a.cs`", markdown, StringComparison.Ordinal);
        Assert.Contains("- **Bash** `ls`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_conversation_tree_finds_the_card_a_call_is_in()
    {
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"hi"},{"type":"tool_use","id":"r1","name":"Read","input":{"file_path":"a.cs"}},{"type":"tool_use","id":"a1","name":"Agent","input":{"description":"Look"}}]}}""");
        Apply("""{"type":"assistant","parent_tool_use_id":"a1","message":{"content":[{"type":"tool_use","id":"g1","name":"Grep","input":{"pattern":"x"}}]}}""");
        var read = _items.ToolCalls().First();
        var grep = _items.Descendants().OfType<ToolUseItem>().Single(c => c.Name == "Grep");

        Assert.Same(_items[1], _items.TopLevelOf(read));
        Assert.Same(_items[2], _items.TopLevelOf(grep));
        Assert.Same(_items[0], _items.TopLevelOf(_items[0]));
        Assert.Null(_items.TopLevelOf(new NoteItem("elsewhere", NoteKind.Info)));
    }

    private void Replay(string line, DateTimeOffset at)
    {
        Assert.True(MessageParser.TryParse(line, out var message, out var error), error);
        _builder.Replay(message switch
        {
            AssistantMessage a => new AssistantMessageReceived(a),
            UserMessage u => new ToolResultsReceived(u),
            _ => throw new InvalidOperationException(message.GetType().Name),
        }, at);
    }

    private void Apply(string line)
    {
        Assert.True(MessageParser.TryParse(line, out var message, out var error), error);
        _builder.Apply(message switch
        {
            AssistantMessage a => new AssistantMessageReceived(a),
            UserMessage u => new ToolResultsReceived(u),
            ResultMessage r => new TurnCompleted(r, ModelUsageTotals.From(r.ModelUsage)),
            _ => throw new InvalidOperationException(message.GetType().Name),
        });
    }
}
