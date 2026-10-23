using System.Text.Json.Nodes;
using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Claudette.App.Tests;

/// <summary>The tasks Claude is working through, for the side panel's Tasks page (DESIGN.md §5, "Tasks").</summary>
public class TaskBoardTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly TodoList _list;

    public TaskBoardTests() => _list = new TodoList { Time = _time };

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private void Create(string toolUseId, string id, string subject, string? description = null)
    {
        var input = new JsonObject { ["subject"] = subject };
        if (description is not null)
        {
            input["description"] = description;
        }
        _list.ApplyToolUse(toolUseId, "TaskCreate", input);
        _list.ApplyToolResult(toolUseId, $"Task #{id} created", new JsonObject { ["task"] = new JsonObject { ["id"] = id, ["subject"] = subject } });
    }

    [Fact]
    public void An_error_result_which_is_only_text_changes_nothing()
    {
        _list.ApplyToolUse("t1", "TaskCreate", new JsonObject { ["subject"] = "Write tests" });

        // Claude Code gives a failed call's details as its error text.
        _list.ApplyToolResult("t1", "<tool_use_error>No such task</tool_use_error>", JsonValue.Create("Error: No such task"));
        _list.ApplyToolResult("t2", "Error", JsonValue.Create("Error"));
        _list.ApplyToolResult("t3", "", new JsonObject { ["task"] = "not an object" });

        var item = Assert.Single(_list.Items);
        Assert.Null(item.Id);
    }

    [Fact]
    public void A_restored_tab_dates_its_tasks_by_the_transcript_not_the_restore()
    {
        var builder = new ConversationBuilder([], _list) { Time = _time };
        var written = DateTimeOffset.Parse("2026-09-30T16:20:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(MessageParser.TryParse("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"TodoWrite","input":{"todos":[{"content":"Fix it","status":"in_progress","activeForm":"Fixing it"}]}}]}}""", out var message, out _));

        builder.Replay(new AssistantMessageReceived((AssistantMessage)message!), written);

        Assert.Equal(written, _list.Items.Single().CreatedAt);
        Assert.Equal(written, _list.Items.Single().StartedAt);
    }

    [Fact]
    public void Tasks_keep_their_description_owner_and_times_and_count_toward_the_badge()
    {
        Create("t1", "1", "Read the build script", "Find where the shipping config is chosen.");
        Create("t2", "2", "Fix the config");
        Assert.Equal("0 of 2", _list.Badge);

        _time.Advance(TimeSpan.FromMinutes(1));
        _list.ApplyToolUse("t3", "TaskUpdate", Json("""{"taskId":"1","status":"in_progress","owner":"explorer"}"""));
        Assert.Same(_list.Items[0], _list.Current);
        _time.Advance(TimeSpan.FromMinutes(4));
        _list.ApplyToolUse("t4", "TaskUpdate", Json("""{"taskId":"1","status":"completed"}"""));

        var first = _list.Items[0];
        Assert.Equal("#1", first.NumberText);
        Assert.Equal("Find where the shipping config is chosen.", first.Description);
        Assert.Equal("explorer", first.Owner);
        Assert.Equal("Took 4m", first.TimeText);
        // One line under the task: who's on it, then its time, with nothing in front when nobody is.
        Assert.Equal("explorer · Took 4m", first.DetailText);
        Assert.StartsWith("Added ", _list.Items[1].DetailText, StringComparison.Ordinal);
        Assert.Equal("1 of 2", _list.Badge);
        Assert.Null(_list.Current);
    }

    [Fact]
    public void A_task_waits_on_its_blockers_until_theyre_done()
    {
        Create("t1", "1", "Write the migration");
        Create("t2", "2", "Run the migration");
        _list.ApplyToolUse("t3", "TaskUpdate", Json("""{"taskId":"2","addBlockedBy":["1"]}"""));

        Assert.Equal("Waiting on #1", _list.Items[1].BlockedText);

        _list.ApplyToolUse("t4", "TaskUpdate", Json("""{"taskId":"1","status":"completed"}"""));
        Assert.Null(_list.Items[1].BlockedText);
    }

    [Fact]
    public void A_task_list_result_fills_in_what_the_calls_didnt_show()
    {
        Create("t1", "1", "Known");

        _list.ApplyToolResult("t9", "", Json("""{"tasks":[{"id":"1","subject":"Known","status":"in_progress","blockedBy":[]},{"id":"2","subject":"From a subagent","status":"pending","owner":"helper","blockedBy":["1"]}]}"""));

        Assert.Equal(["Known", "From a subagent"], _list.Items.Select(i => i.Content));
        Assert.True(_list.Items[0].IsActive);
        Assert.Equal("helper", _list.Items[1].Owner);
        Assert.Equal("Waiting on #1", _list.Items[1].BlockedText);
    }

    private void Update(string toolUseId, string id, string status)
    {
        _list.ApplyToolUse(toolUseId, "TaskUpdate", new JsonObject { ["taskId"] = id, ["status"] = status });
        _list.ApplyToolResult(toolUseId, $"Updated task #{id} status", new JsonObject { ["success"] = true, ["taskId"] = id });
    }

    private static ClaudeTask Listed(string id, string subject, string status = "pending") => new(id, subject, null, null, null, status, []);

    [Fact]
    public void A_number_Claude_Code_gives_again_means_it_started_its_list_over()
    {
        // Seen in a long session: a new version of Claude Code began a new list, numbered from #1 again.
        Create("c1", "1", "Old first");
        Create("c2", "2", "Old second");
        Update("u1", "1", "completed");

        Create("c3", "1", "New first");
        Update("u2", "1", "in_progress");

        // The tasks from before it are gone, and #1 is the new one.
        var task = Assert.Single(_list.Items);
        Assert.Equal("New first", task.Content);
        Assert.True(task.IsActive);
        Assert.Equal("0 of 1", _list.Badge);
    }

    [Fact]
    public void A_list_started_over_by_calls_made_together_keeps_all_of_them_whatever_order_their_numbers_come_in()
    {
        Create("c1", "1", "Old first");
        Create("c2", "2", "Old second");
        Create("c3", "3", "Old third");

        foreach (var (call, subject) in new[] { ("n1", "New first"), ("n2", "New second"), ("n3", "New third") })
        {
            _list.ApplyToolUse(call, "TaskCreate", new JsonObject { ["subject"] = subject });
        }
        foreach (var (call, id) in new[] { ("n2", "2"), ("n3", "3"), ("n1", "1") })
        {
            _list.ApplyToolResult(call, $"Task #{id} created", new JsonObject { ["task"] = new JsonObject { ["id"] = id } });
        }

        Assert.Equal(["#1 New first", "#2 New second", "#3 New third"], _list.Items.OrderBy(i => i.Id).Select(i => i.Title));
    }

    [Fact]
    public void Numbers_that_carry_on_past_deleted_tasks_dont_start_the_list_over()
    {
        Create("c1", "1", "Done and deleted");
        Create("c2", "2", "Still here");
        Update("u1", "1", "deleted");

        Create("c3", "3", "Next");

        Assert.Equal(["#2 Still here", "#3 Next"], _list.Items.Select(i => i.Title));
    }

    [Fact]
    public void A_task_list_result_is_the_whole_list_and_a_task_it_hasnt_got_is_gone()
    {
        Create("c1", "1", "Deleted where Claudette didn't see it");
        Create("c2", "2", "Kept");
        _list.ApplyToolUse("l1", "TaskList", new JsonObject());
        // Made while the list was being read: the result can't have it yet.
        Create("c3", "3", "Made meanwhile");

        _list.ApplyToolResult("l1", "#2 [pending] Kept", Json("""{"tasks":[{"id":"2","subject":"Kept","status":"pending","blockedBy":[]}]}"""));

        Assert.Equal(["#2 Kept", "#3 Made meanwhile"], _list.Items.Select(i => i.Title));
    }

    [Fact]
    public void Claude_Codes_tasks_folder_is_the_whole_list_once_Claude_is_done_changing_it()
    {
        Create("c1", "1", "Read the build script");
        Create("c2", "2", "Deleted elsewhere");
        Create("c3", "3", "Fix the config");
        var readAt = _list.Version;

        Assert.True(_list.ApplyFolder([Listed("1", "Read the build script", "completed"), Listed("3", "Fix the config", "in_progress"), Listed("4", "From another session")], readAt));

        Assert.Equal(["#1 Read the build script", "#3 Fix the config", "#4 From another session"], _list.Items.Select(i => i.Title));
        Assert.True(_list.Items[0].IsDone);
        Assert.Same(_list.Items[1], _list.Current);
    }

    [Fact]
    public void A_folder_read_from_before_Claudes_latest_change_or_while_its_calls_wait_is_left_to_the_next()
    {
        Create("c1", "1", "Fix the config");
        var readAt = _list.Version;
        Update("u1", "1", "in_progress");

        // Read before the update: it would put the task back.
        Assert.True(_list.ApplyFolder([Listed("1", "Fix the config")], readAt));
        Assert.True(_list.Items.Single().IsActive);

        // A task still waiting for its number: the folder may have it under that number already.
        _list.ApplyToolUse("c2", "TaskCreate", new JsonObject { ["subject"] = "Run the tests" });
        Assert.True(_list.ApplyFolder([Listed("1", "Fix the config", "in_progress"), Listed("2", "Run the tests")], _list.Version));
        Assert.Equal(2, _list.Items.Count);
        Assert.Null(_list.Items[1].Id);

        // A change not yet answered keeps its task as the call left it.
        _list.ApplyToolResult("c2", "Task #2 created", new JsonObject { ["task"] = new JsonObject { ["id"] = "2" } });
        _list.ApplyToolUse("u2", "TaskUpdate", new JsonObject { ["taskId"] = "1", ["status"] = "completed" });
        Assert.True(_list.ApplyFolder([Listed("2", "Run the tests")], _list.Version));
        Assert.Equal(["#1 Fix the config", "#2 Run the tests"], _list.Items.Select(i => i.Title));
        Assert.True(_list.Items[0].IsDone);
    }

    [Fact]
    public void A_folder_without_the_newest_task_Claude_made_isnt_where_Claude_Code_keeps_this_list()
    {
        Create("c1", "1", "Old");
        Create("c2", "2", "Newest");

        Assert.False(_list.ApplyFolder([Listed("1", "Old")], _list.Version));
        // Nor is one whose task by that number is another.
        Assert.False(_list.ApplyFolder([Listed("1", "Old"), Listed("2", "Someone else's")], _list.Version));
        Assert.Equal(["#1 Old", "#2 Newest"], _list.Items.Select(i => i.Title));

        // Once Claude deleted it, the folder is right not to have it.
        Update("u1", "2", "deleted");
        Assert.True(_list.ApplyFolder([], _list.Version));
        Assert.Empty(_list.Items);
    }

    [Fact]
    public void Once_the_folder_has_shown_the_newest_task_its_going_is_a_deletion()
    {
        Create("c1", "1", "Old");
        Create("c2", "2", "Newest");
        Assert.True(_list.ApplyFolder([Listed("1", "Old"), Listed("2", "Newest")], _list.Version));

        // Another session sharing the list deleted it.
        Assert.True(_list.ApplyFolder([Listed("1", "Old")], _list.Version));

        Assert.Equal(["#1 Old"], _list.Items.Select(i => i.Title));
    }

    [Fact]
    public void Calls_that_wont_be_answered_dont_hold_back_the_folder()
    {
        Create("c1", "1", "Fix the config");
        _list.ApplyToolUse("c2", "TaskCreate", new JsonObject { ["subject"] = "Never numbered" });

        _list.CallsEnded();

        Assert.True(_list.ApplyFolder([Listed("1", "Fix the config", "completed")], _list.Version));
        Assert.True(_list.Items[0].IsDone);
    }

    [Fact]
    public void An_approved_plan_heads_the_list_until_its_cleared()
    {
        _list.OnPlanAnswered("p1", approved: true, "  1. Read\n2. Fix  ");

        Assert.Equal("1. Read\n2. Fix", _list.Plan);
        Assert.True(_list.HasAnything);
        Assert.Equal(_time.GetUtcNow(), _list.PlanAt);
        Assert.Equal(PlanSource.Approved, _list.PlanSource);

        _list.Clear();
        Assert.False(_list.HasAnything);
    }

    [Fact]
    public void A_restored_tab_shows_the_plan_its_transcript_approved_dated_by_the_approval()
    {
        // Claude Code 2.1.285 and later: the call has no plan of its own; the approved result carries it.
        var builder = new ConversationBuilder([], _list) { Time = _time };
        var asked = DateTimeOffset.Parse("2026-09-30T16:20:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var approved = asked.AddMinutes(3);

        builder.Replay(Assistant(Wire.Tool(null, "p1", "ExitPlanMode", new JsonObject())), asked);
        builder.Replay(Results(Wire.Result("p1", "User has approved your plan.", toolUseResult: Json("""{"plan":"## Fix\n1. Read","isAgent":false,"filePath":"plan.md"}"""))), approved);

        Assert.Equal("## Fix\n1. Read", _list.Plan);
        Assert.Equal(approved, _list.PlanAt);
        Assert.Equal(PlanSource.Approved, _list.PlanSource);
        Assert.StartsWith("Approved ", _list.NewestPlan!.TimeText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_sent_back_to_planning_is_kept_as_a_version_and_a_subagents_plan_isnt_one()
    {
        var builder = new ConversationBuilder([], _list) { Time = _time };

        builder.Apply(Assistant(Wire.Tool(null, "p1", "ExitPlanMode", Json("""{"plan":"1. Read"}"""))));
        builder.Apply(Results(Wire.Result("p1", "The user doesn't want to proceed with this tool use.", isError: true)));
        builder.Apply(Assistant(Wire.Agent("a1", "Plan it", "Make a plan")));
        builder.Apply(Assistant(Wire.Tool("a1", "p2", "ExitPlanMode", Json("""{"plan":"2. Fix"}"""))));
        builder.Apply(Results(Wire.Result("p2", "Approved.", parent: "a1", toolUseResult: Json("""{"plan":"2. Fix","isAgent":true}"""))));

        var version = Assert.Single(_list.Plans);
        Assert.Equal(PlanState.SentBack, version.State);
        Assert.Equal("1. Read", version.Text);
        Assert.StartsWith("Sent back ", version.TimeText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_from_a_reply_says_so_with_when_Claude_wrote_it()
    {
        var written = DateTimeOffset.Parse("2026-09-30T16:20:00Z", System.Globalization.CultureInfo.InvariantCulture);

        _list.ChooseReply("Here's my plan.", written);
        Assert.Equal($"From Claude's reply at {written.ToLocalTime():t}", _list.NewestPlan!.TimeText);

        // Another reply replaces it, rather than adding a version.
        _list.ChooseReply("Here's my plan.", null);
        Assert.Equal("From Claude's reply", Assert.Single(_list.Plans).TimeText);
    }

    private static AssistantMessageReceived Assistant(string line)
    {
        Assert.True(MessageParser.TryParse(line, out var message, out _));
        return new AssistantMessageReceived((AssistantMessage)message!);
    }

    private static ToolResultsReceived Results(string line)
    {
        Assert.True(MessageParser.TryParse(line, out var message, out _));
        return new ToolResultsReceived((UserMessage)message!);
    }

    [Fact]
    public void A_rewritten_todo_list_keeps_the_times_of_items_that_stayed()
    {
        _list.ApplyToolUse("w1", "TodoWrite", Json("""{"todos":[{"content":"A","status":"in_progress"},{"content":"B","status":"pending"}]}"""));
        var started = _list.Items[0].StartedAt;
        _time.Advance(TimeSpan.FromMinutes(2));

        _list.ApplyToolUse("w2", "TodoWrite", Json("""{"todos":[{"content":"A","status":"completed"},{"content":"B","status":"in_progress"}]}"""));

        Assert.Equal(started, _list.Items[0].StartedAt);
        Assert.Equal("Took 2m", _list.Items[0].TimeText);
        Assert.Equal(_time.GetUtcNow(), _list.Items[1].StartedAt);
    }

    [Fact]
    public void The_pinned_list_stays_closed_as_Claude_works_through_it_and_opens_for_a_new_one()
    {
        _list.ApplyToolUse("w1", "TodoWrite", Json("""{"todos":[{"content":"A","activeForm":"Doing A","status":"in_progress"},{"content":"B","activeForm":"Doing B","status":"pending"}]}"""));
        Assert.True(_list.IsExpanded);
        Assert.Null(_list.ClosedLineTask);

        // Closed, its line names the task in progress, and Claude's changes leave it closed.
        _list.ToggleExpandedCommand.Execute(null);
        Assert.Equal("Doing A", _list.ClosedLineTask);
        _list.ApplyToolUse("w2", "TodoWrite", Json("""{"todos":[{"content":"A","activeForm":"Doing A","status":"completed"},{"content":"B","activeForm":"Doing B","status":"in_progress"}]}"""));
        Assert.False(_list.IsExpanded);
        Assert.Equal("Doing B", _list.ClosedLineTask);

        // Opened again: it stays open, until every task is done.
        _list.ToggleExpandedCommand.Execute(null);
        Assert.Null(_list.ClosedLineTask);
        _list.ApplyToolUse("w3", "TodoWrite", Json("""{"todos":[{"content":"A","status":"completed"},{"content":"B","status":"completed"},{"content":"C","status":"pending"}]}"""));
        Assert.True(_list.IsExpanded);
        _list.ApplyToolUse("w4", "TodoWrite", Json("""{"todos":[{"content":"A","status":"completed"},{"content":"B","status":"completed"},{"content":"C","status":"completed"}]}"""));
        Assert.False(_list.IsExpanded);

        // A finished list the user opens stays open as Claude reads it; a new list opens.
        _list.ToggleExpandedCommand.Execute(null);
        _list.ApplyToolUse("l1", "TaskList", new JsonObject());
        _list.ApplyToolResult("l1", "", new JsonObject { ["tasks"] = new JsonArray() });
        Assert.True(_list.IsExpanded);
        _list.ToggleExpandedCommand.Execute(null);
        _list.ApplyToolUse("w5", "TodoWrite", Json("""{"todos":[{"content":"D","status":"pending"}]}"""));
        Assert.True(_list.IsExpanded);
    }
}
